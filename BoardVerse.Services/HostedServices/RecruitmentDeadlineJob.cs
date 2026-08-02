using BoardVerse.Services.IServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.HostedServices;

/// <summary>
/// BR-LOBBY-02 (mục 21A.5): chạy mỗi 60 giây xử lý reservation đến recruitmentDeadline.
/// - currentPlayers ≥ minPlayers → lobby Viable/Full, reservation Confirmed.
/// - currentPlayers &lt; minPlayers  → lobby TimeoutFailed, refund 100% BVC (BR-REFUND-01).
/// </summary>
public class RecruitmentDeadlineJob : PollingHostedService
{
    private const int BatchSize = 100;

    public RecruitmentDeadlineJob(
        IServiceScopeFactory scopeFactory,
        ILogger<RecruitmentDeadlineJob> logger)
        : base(scopeFactory, logger, TimeSpan.FromSeconds(60))
    {
    }

    protected override async Task ExecuteTickAsync(IServiceProvider sp, CancellationToken ct)
    {
        var reservationService = sp.GetRequiredService<IReservationService>();
        var now = DateTime.UtcNow;

        var processed = await reservationService.ProcessDeadlineReservationsAsync(now, BatchSize, ct);

        if (processed > 0)
        {
            var scopeLogger = sp.GetRequiredService<ILogger<RecruitmentDeadlineJob>>();
            scopeLogger.LogInformation(
                "RecruitmentDeadlineJob processed {Count} reservation(s) at {Now:o}",
                processed, now);
        }
    }
}
