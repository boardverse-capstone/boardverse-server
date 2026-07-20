using BoardVerse.Services.IServices;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Background job tự động close registration cho các tournament
/// đã hết hạn đăng ký (RegistrationDeadline &lt; now) nhưng manager
/// quên thao tác close thủ công.
///
/// Job gọi <c>ITournamentService.AutoCloseExpiredRegistrationsAsync</c>
/// mỗi 60 giây, idempotent (chỉ xử lý tournament đang ở trạng thái OpenRegistration).
/// </summary>
public class TournamentExpiryJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TournamentExpiryJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    public TournamentExpiryJob(
        IServiceProvider serviceProvider,
        ILogger<TournamentExpiryJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TournamentExpiryJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var closed = await AutoCloseExpiredRegistrationsAsync(stoppingToken);
                if (closed > 0)
                {
                    _logger.LogInformation(
                        "TournamentExpiryJob auto-closed {Closed} expired registrations.",
                        closed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // App shutting down — exit cleanly.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TournamentExpiryJob");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("TournamentExpiryJob stopped.");
    }

    private async Task<int> AutoCloseExpiredRegistrationsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = _serviceProvider.CreateScope();
        var tournamentService = scope.ServiceProvider
            .GetRequiredService<ITournamentService>();

        return await tournamentService.AutoCloseExpiredRegistrationsAsync(DateTime.UtcNow, ct);
    }
}