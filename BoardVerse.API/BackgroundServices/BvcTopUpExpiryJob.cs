using BoardVerse.Services.IServices;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Background job auto-expire BVC top-up request Pending quá ExpiresAt (mặc định 30 phút).
/// Idempotent: chỉ chuyển status Pending → Expired, không cộng/trừ ví (vì tiền thật chưa về).
/// Cluster-safe: WalletService.ExpirePendingTopUpsAsync dùng FOR UPDATE SKIP LOCKED + batch tx.
/// </summary>
public class BvcTopUpExpiryJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BvcTopUpExpiryJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(2);

    public BvcTopUpExpiryJob(IServiceProvider serviceProvider, ILogger<BvcTopUpExpiryJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BvcTopUpExpiryJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredTopUpsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("BvcTopUpExpiryJob stopped (host shutdown).");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in BvcTopUpExpiryJob");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ProcessExpiredTopUpsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var walletService = scope.ServiceProvider.GetRequiredService<IWalletService>();

        var expiredCount = await walletService.ExpirePendingTopUpsAsync(stoppingToken);
        if (expiredCount > 0)
        {
            _logger.LogInformation(
                "BvcTopUpExpiryJob processed {Count} expired top-up requests in this tick.",
                expiredCount);
        }
    }
}