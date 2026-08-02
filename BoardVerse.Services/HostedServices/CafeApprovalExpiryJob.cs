using BoardVerse.Services.IServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.HostedServices;

/// <summary>
/// BR-NEW-11 (mục 21F.15): chạy mỗi 5 phút xử lý lobby pendingCafeApproval quá 24 giờ.
/// → status = ExpiredByCafe, refund 100% BVC cho host.
/// </summary>
public class CafeApprovalExpiryJob : PollingHostedService
{
    private const int BatchSize = 100;

    public CafeApprovalExpiryJob(
        IServiceScopeFactory scopeFactory,
        ILogger<CafeApprovalExpiryJob> logger)
        : base(scopeFactory, logger, TimeSpan.FromMinutes(5))
    {
    }

    protected override async Task ExecuteTickAsync(IServiceProvider sp, CancellationToken ct)
    {
        var reservationService = sp.GetRequiredService<IReservationService>();
        var now = DateTime.UtcNow;

        var processed = await reservationService.ProcessCafeApprovalExpiryAsync(now, BatchSize, ct);

        if (processed > 0)
        {
            var scopeLogger = sp.GetRequiredService<ILogger<CafeApprovalExpiryJob>>();
            scopeLogger.LogInformation(
                "CafeApprovalExpiryJob processed {Count} reservation(s) at {Now:o}",
                processed, now);
        }
    }
}
