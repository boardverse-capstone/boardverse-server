using BoardVerse.Services.IServices;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// BR-NEW-10 §XI — Cooling-off background job.
///
/// 2 scheduler tick mỗi 30 phút (lighter cadence so với ReservationDeadlineJob mỗi phút):
/// <list type="number">
///   <item><description><b>DetectAndActivate</b>: quét wallet active, detect signals
///         (3 TimeoutFailed / HostCancelled trong 7d, hoặc forfeit &gt; 500k BVC trong 30d),
///         activate cooling-off 30 ngày + riskMultiplier ×2.</description></item>
///   <item><description><b>ExpireOverdue</b>: deactivate wallet đã quá hạn cooling-off.</description></item>
/// </list>
///
/// Tần suất 30 phút là đủ vì:
/// <list type="bullet">
///   <item><description>Signals dựa trên window 7-30 ngày (resolution thấp).</description></item>
///   <item><description>User perception về cooling-off có tolerance giờ chứ không phải phút.</description></item>
/// </list>
/// </summary>
public class CoolingOffJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CoolingOffJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(30);
    private const int BatchSize = 100;

    public CoolingOffJob(IServiceProvider serviceProvider, ILogger<CoolingOffJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CoolingOffJob started (interval={Interval}m, batchSize={BatchSize}).",
            _interval.TotalMinutes, BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAllSchedulersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("CoolingOffJob stopped (host shutdown).");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CoolingOffJob tick");
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

        _logger.LogInformation("CoolingOffJob stopped.");
    }

    private async Task RunAllSchedulersAsync(CancellationToken ct)
    {
        // 1. Detect signals → activate cooling-off.
        await RunSchedulerAsync(
            tickName: "DetectAndActivate",
            invocation: sp => sp.GetRequiredService<ICoolingOffService>()
                .DetectAndActivateAsync(DateTime.UtcNow, BatchSize, ct),
            ct: ct);

        // 2. Expire cooling-off quá hạn.
        await RunSchedulerAsync(
            tickName: "ExpireOverdue",
            invocation: sp => sp.GetRequiredService<ICoolingOffService>()
                .ExpireOverdueAsync(DateTime.UtcNow, BatchSize, ct),
            ct: ct);
    }

    private async Task RunSchedulerAsync(string tickName, Func<IServiceProvider, Task<int>> invocation, CancellationToken ct)
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
                    "[{Tick}] CoolingOffJob processed {Count} wallets in {ElapsedMs}ms",
                    tickName, processed, sw.ElapsedMilliseconds);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogInformation(
                "[{Tick}] CoolingOffJob cancelled after {ElapsedMs}ms (likely host shutdown).",
                tickName, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "[{Tick}] CoolingOffJob failed after {ElapsedMs}ms",
                tickName, sw.ElapsedMilliseconds);
        }
    }
}
