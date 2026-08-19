using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// GAP #17 fix: Background job xử lý reservation deadline.
/// Gộp 3 scheduler (BR §21A.5 + BR-NEW-11 + BR §21A.9):
///   1. <b>Deadline</b>: Recruitment deadline → Confirmed (nếu đạt minPlayers) hoặc TimeoutFailed + refund.
///   2. <b>Cafe approval expiry</b>: 24h không duyệt → ExpiredByCafe + refund.
///   3. <b>No-show</b>: Sau scheduledTime + grace, không check-in → NoShow + forfeit.
///
/// Tần suất: mỗi 1 phút (giống LobbyTimeoutJob / BookingDepositExpiryJob).
/// Mỗi scheduler process theo batch 50 reservation/lần → tránh lock DB quá lâu.
/// </summary>
public class ReservationDeadlineJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReservationDeadlineJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 50;

    public ReservationDeadlineJob(IServiceProvider serviceProvider, ILogger<ReservationDeadlineJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReservationDeadlineJob started (interval={Interval}s, batchSize={BatchSize}).",
            _interval.TotalSeconds, BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAllSchedulersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ReservationDeadlineJob tick");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("ReservationDeadlineJob stopped.");
    }

    private async Task RunAllSchedulersAsync(CancellationToken ct)
    {
        // 1. RecruitmentDeadline → confirm/timeout.
        await RunSchedulerAsync(
            tickName: "Deadline",
            invocation: sp => sp.GetRequiredService<IReservationService>()
                .ProcessDeadlineReservationsAsync(DateTime.UtcNow, BatchSize, ct));

        // 2. Cafe approval expiry (24h timeout).
        await RunSchedulerAsync(
            tickName: "CafeApprovalExpiry",
            invocation: sp => sp.GetRequiredService<IReservationService>()
                .ProcessCafeApprovalExpiryAsync(DateTime.UtcNow, BatchSize, ct));

        // 3. No-show detection (đã đến scheduledTime + grace mà chưa check-in).
        await RunSchedulerAsync(
            tickName: "NoShow",
            invocation: sp => sp.GetRequiredService<IReservationService>()
                .ProcessNoShowAsync(DateTime.UtcNow, BatchSize, ct));
    }

    private async Task RunSchedulerAsync(string tickName, Func<IServiceProvider, Task<int>> invocation)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var processed = await invocation(scope.ServiceProvider);
            sw.Stop();

            if (processed > 0)
            {
                _logger.LogInformation(
                    "[{Tick}] ReservationDeadlineJob processed {Count} reservations in {ElapsedMs}ms",
                    tickName, processed, sw.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "[{Tick}] ReservationDeadlineJob failed after {ElapsedMs}ms",
                tickName, sw.ElapsedMilliseconds);
        }
    }
}
