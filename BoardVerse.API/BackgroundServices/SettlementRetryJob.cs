using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Retry SePay transfers cho các <see cref="CafeSettlement"/> bị Fail.
/// Mỗi 5 phút quét các settlement Status=Failed có RetryCount &lt; maxAttempts và đủ backoff
/// rồi gọi lại <see cref="ISettlementService.ReleaseSessionDepositAsync"/> để thử SePay transfer.
/// Success → status = Succeeded, deposit.Status = Released.
/// Fail → tăng RetryCount + set NextRetryAt = now + 2^(retryCount) phút (exponential backoff).
/// </summary>
public class SettlementRetryJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const int MaxAttempts = 5;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SettlementRetryJob> _logger;

    public SettlementRetryJob(IServiceProvider serviceProvider, ILogger<SettlementRetryJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SettlementRetryJob started. Interval={Interval}, MaxAttempts={Max}", Interval, MaxAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRetriesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SettlementRetryJob iteration failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessRetriesAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var settlementRepo = scope.ServiceProvider.GetRequiredService<ICafeSettlementRepository>();
        var settlementService = scope.ServiceProvider.GetRequiredService<ISettlementService>();

        // Bỏ qua retry vừa fail (đợi ít nhất 60s để tránh tight loop)
        var retryable = await settlementRepo.GetRetryableAsync(MaxAttempts, TimeSpan.FromSeconds(60));
        if (retryable.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Retrying {Count} failed settlements", retryable.Count);

        foreach (var settlement in retryable)
        {
            if (ct.IsCancellationRequested) break;
            await RetryOneAsync(settlement, settlementService, settlementRepo);
        }
    }

    private async Task RetryOneAsync(
        CafeSettlement settlement,
        ISettlementService settlementService,
        ICafeSettlementRepository settlementRepo)
    {
        if (settlement.ActiveSessionId == null)
        {
            _logger.LogWarning("Settlement {SettlementId} missing ActiveSessionId, skipping", settlement.Id);
            return;
        }

        try
        {
            var activeSessionId = settlement.ActiveSessionId.Value;
            _logger.LogInformation(
                "Retrying settlement {SettlementId} (attempt {Attempt})",
                settlement.Id, settlement.RetryCount + 1);

            var updated = await settlementService.ReleaseSessionDepositAsync(
                settlement.CafeId,
                activeSessionId,
                activeSessionId);

            if (updated.Status == CafeSettlementStatus.Succeeded)
            {
                _logger.LogInformation(
                    "Settlement {SettlementId} succeeded on retry (attempt {Attempt})",
                    settlement.Id, settlement.RetryCount + 1);
            }
            else
            {
                BumpRetry(settlement, settlementRepo);
            }
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Settlement {SettlementId} skipped - entity missing", settlement.Id);
        }
        catch (ConflictException)
        {
            // Deposit đã Released ở nơi khác → settlement được coi là succeeded
            settlement.Status = CafeSettlementStatus.Succeeded;
            settlement.UpdatedAt = DateTime.UtcNow;
            await settlementRepo.UpdateAsync(settlement);
            await settlementRepo.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retry threw unexpected error for settlement {SettlementId}", settlement.Id);
            BumpRetry(settlement, settlementRepo);
        }
    }

    private static async void BumpRetry(CafeSettlement settlement, ICafeSettlementRepository settlementRepo)
    {
        settlement.RetryCount += 1;
        var backoffMinutes = (int)Math.Pow(2, settlement.RetryCount); // 2, 4, 8, 16, 32
        settlement.NextRetryAt = DateTime.UtcNow.AddMinutes(backoffMinutes);
        settlement.UpdatedAt = DateTime.UtcNow;
        await settlementRepo.UpdateAsync(settlement);
        await settlementRepo.SaveChangesAsync();
    }
}