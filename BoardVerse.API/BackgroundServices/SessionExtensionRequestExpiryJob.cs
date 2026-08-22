using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Quét mỗi 1 phút, đánh Expired các yêu cầu gia hạn
/// quá 10 phút mà không có staff xử lý.
///
/// Extension request có thời hạn 10 phút — nếu không được approve/reject
/// trong 10 phút, tự động chuyển sang Expired để không spam POS.
/// </summary>
public class SessionExtensionRequestExpiryJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SessionExtensionRequestExpiryJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);
    private readonly TimeSpan _expiryThreshold = TimeSpan.FromMinutes(10);

    public SessionExtensionRequestExpiryJob(
        IServiceScopeFactory scopeFactory,
        ILogger<SessionExtensionRequestExpiryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SessionExtensionRequestExpiryJob started. Running every {Interval} minute(s)",
            _interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredRequestsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("SessionExtensionRequestExpiryJob stopped (host shutdown).");
                break;
            }
            catch (Npgsql.PostgresException pgEx) when (pgEx.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                // GAP-R6-RT-NEW Fix: graceful skip khi table SessionExtensionRequests chưa migrate.
                // Trước đây: mỗi phút spam LogError → log noise + monitoring alert.
                // Sau: chỉ log Warning một lần mỗi 10 phút để báo hiệu cần apply migration.
                if (_lastUndefinedTableLog + TimeSpan.FromMinutes(10) < DateTime.UtcNow)
                {
                    _logger.LogWarning(
                        "SessionExtensionRequestExpiryJob: table SessionExtensionRequests chưa tồn tại trong DB. Cần apply migration AddSessionExtensionRequestTable. Job sẽ skip cho đến khi table sẵn sàng. Error: {Message}",
                        pgEx.Message);
                    _lastUndefinedTableLog = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SessionExtensionRequestExpiryJob: error during processing");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private DateTime _lastUndefinedTableLog = DateTime.MinValue;

    private async Task ProcessExpiredRequestsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISessionExtensionRequestRepository>();

        var cutoff = DateTime.UtcNow.Subtract(_expiryThreshold);
        var affectedRows = await repo.ExpireBatchAsync(cutoff, batchSize: 500, ct);

        if (affectedRows == 0)
        {
            _logger.LogDebug("SessionExtensionRequestExpiryJob: No expired extension requests found");
            return;
        }

        _logger.LogInformation(
            "SessionExtensionRequestExpiryJob: Marked {Count} extension requests as Expired",
            affectedRows);
    }
}