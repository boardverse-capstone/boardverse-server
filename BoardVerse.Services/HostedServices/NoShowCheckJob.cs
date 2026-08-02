using BoardVerse.Services.IServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.HostedServices;

/// <summary>
/// BR §21A.9: chạy mỗi 5 phút xử lý reservation Confirmed mà chưa check-in sau grace (30 phút).
/// → status = NoShow, forfeit 100% BVC về doanh thu quán, giảm Karma host.
/// </summary>
public class NoShowCheckJob : PollingHostedService
{
    private const int BatchSize = 100;

    public NoShowCheckJob(
        IServiceScopeFactory scopeFactory,
        ILogger<NoShowCheckJob> logger)
        : base(scopeFactory, logger, TimeSpan.FromMinutes(5))
    {
    }

    protected override async Task ExecuteTickAsync(IServiceProvider sp, CancellationToken ct)
    {
        var reservationService = sp.GetRequiredService<IReservationService>();
        var now = DateTime.UtcNow;

        var processed = await reservationService.ProcessNoShowAsync(now, BatchSize, ct);

        if (processed > 0)
        {
            var scopeLogger = sp.GetRequiredService<ILogger<NoShowCheckJob>>();
            scopeLogger.LogInformation(
                "NoShowCheckJob processed {Count} reservation(s) at {Now:o}",
                processed, now);
        }
    }
}
