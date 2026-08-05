using BoardVerse.Services.IServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.HostedServices;

/// <summary>
/// GAP-9 Fix: Retry BVC capture for sessions that failed during PaySessionAsync.
/// Chạy mỗi 5 phút để capture BVC cho các phiên đã PAID nhưng chưa capture thành công.
/// BR §21A.8 + BR-REVENUE-01: BVC deposit capture về doanh thu quán khi check-in hoặc quá hạn.
/// </summary>
public class BvcCaptureRetryJob : PollingHostedService
{
    private const int BatchSize = 100;

    public BvcCaptureRetryJob(
        IServiceScopeFactory scopeFactory,
        ILogger<BvcCaptureRetryJob> logger)
        : base(scopeFactory, logger, TimeSpan.FromMinutes(5))
    {
    }

    protected override async Task ExecuteTickAsync(IServiceProvider sp, CancellationToken ct)
    {
        var reservationService = sp.GetRequiredService<IReservationService>();
        var now = DateTime.UtcNow;

        var processed = await reservationService.ProcessBvcCaptureRetryAsync(now, BatchSize, ct);

        if (processed > 0)
        {
            var scopeLogger = sp.GetRequiredService<ILogger<BvcCaptureRetryJob>>();
            scopeLogger.LogInformation(
                "BvcCaptureRetryJob processed {Count} session(s) at {Now:o}",
                processed, now);
        }
    }
}
