using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Settings;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Options;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// BR-AUDIT-WEIGHT-01 — Background job tự động retry fetch BGG cho game đang missing Weight.
///
/// Sau khi game được import từ BGG (BggGameService.ImportGameAsync), có thể xảy ra:
/// <list type="number">
///   <item><description>BGG chưa tính xong <c>averageweight</c> cho game mới / ít vote → Weight = null.</description></item>
///   <item><description>BGG trả 200 OK nhưng XML thiếu <c>&lt;statistics&gt;</c> (Strategy 3 retry ngắn đã fail).</description></item>
/// </list>
///
/// Job này chạy định kỳ (mặc định 6 giờ), pick game <c>Weight = null</c> + <c>BggId != null</c> +
/// <c>BggRetryCount &lt; MissingWeightMaxRetryRounds</c>, gọi <see cref="IBggGameService.ReimportWeightAsync"/>
/// để fetch lại từ BGG. Khi vượt quá <c>MissingWeightMaxRetryRounds</c> (mặc định 5), game bị loại
/// khỏi retry queue → admin xử lý thủ công qua <c>PUT /api/v1/admin/master-games/{id}</c>.
///
/// Khác với Strategy 3 (retry ngay lúc import, 30s delay):
/// <list type="bullet">
///   <item><description>Strategy 3: retry trong cùng request → fail nhanh nếu BGG chưa sẵn sàng.</description></item>
///   <item><description>Job này: retry mỗi 6 giờ trong tối đa 5 vòng = 30 giờ để BGG có thời gian
///         community vote xong hoặc hàng đợi async của BGG xử lý xong.</description></item>
/// </list>
/// </summary>
public class MissingWeightBggRetryJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly BggSettings _settings;
    private readonly ILogger<MissingWeightBggRetryJob> _logger;

    public MissingWeightBggRetryJob(
        IServiceProvider serviceProvider,
        IOptions<BggSettings> settings,
        ILogger<MissingWeightBggRetryJob> logger)
    {
        _serviceProvider = serviceProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, _settings.MissingWeightRetryIntervalHours));
        _logger.LogInformation(
            "MissingWeightBggRetryJob started (interval={Hours}h, maxRounds={MaxRounds}, batchSize={BatchSize}).",
            interval.TotalHours, _settings.MissingWeightMaxRetryRounds, _settings.MissingWeightRetryBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("MissingWeightBggRetryJob stopped (host shutdown).");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in MissingWeightBggRetryJob tick");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("MissingWeightBggRetryJob stopped.");
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var scope = _serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        var repo = sp.GetRequiredService<IGameTemplateRepository>();
        var bgg = sp.GetRequiredService<IBggGameService>();

        var candidates = await repo.GetMissingWeightRetryCandidatesAsync(
            maxRetryRounds: _settings.MissingWeightMaxRetryRounds,
            batchSize: _settings.MissingWeightRetryBatchSize,
            ct: ct);

        if (candidates.Count == 0)
        {
            sw.Stop();
            _logger.LogDebug(
                "MissingWeightBggRetryJob tick: no candidates (elapsed={Ms}ms).",
                sw.ElapsedMilliseconds);
            return;
        }

        _logger.LogInformation(
            "MissingWeightBggRetryJob tick: processing {Count} candidates.",
            candidates.Count);

        var updated = 0;
        var stillMissing = 0;
        var transientFailed = 0;
        var skipped = 0;

        foreach (var candidate in candidates)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                var result = await bgg.ReimportWeightAsync(candidate.Id, ct);
                switch (result)
                {
                    case BggReimportResult.WeightUpdated:
                        updated++;
                        break;
                    case BggReimportResult.WeightStillMissing:
                        stillMissing++;
                        break;
                    case BggReimportResult.TransientFailure:
                        transientFailed++;
                        break;
                    default:
                        skipped++;
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "MissingWeightBggRetryJob: unexpected error for GameTemplate {GameId}.",
                    candidate.Id);
            }
        }

        sw.Stop();
        _logger.LogInformation(
            "MissingWeightBggRetryJob tick done in {ElapsedMs}ms — updated={Updated}, stillMissing={StillMissing}, transient={Transient}, skipped={Skipped}.",
            sw.ElapsedMilliseconds, updated, stillMissing, transientFailed, skipped);
    }
}
