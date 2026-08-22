using BoardVerse.Services.Services;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Background job chạy <see cref="LegacyBookingCleanupService"/> theo chu kỳ
/// <c>LegacyBooking.CleanupIntervalMinutes</c> (mặc định 5 phút).
///
/// Đăng ký trong <c>Program.cs</c> cùng các hosted services khác. Skip khi
/// <c>builder.Environment.IsEnvironment("Testing")</c> để không can thiệp integration tests.
///
/// Lưu ý: Service này dùng scope để resolve <see cref="LegacyBookingCleanupService"/>
/// (vì service phụ thuộc <c>BoardVerseDbContext</c> scoped).
/// </summary>
public class LegacyBookingCleanupJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LegacyBookingCleanupJob> _logger;
    private readonly TimeSpan _interval;

    public LegacyBookingCleanupJob(
        IServiceScopeFactory scopeFactory,
        ILogger<LegacyBookingCleanupJob> logger,
        Microsoft.Extensions.Options.IOptions<BoardVerse.Core.Settings.LegacyBookingSettings> settings)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var minutes = Math.Max(1, settings.Value.CleanupIntervalMinutes);
        _interval = TimeSpan.FromMinutes(minutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "LegacyBookingCleanupJob started. Running every {IntervalMinutes} minutes",
            _interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<LegacyBookingCleanupService>();
                await service.RunOnceAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("LegacyBookingCleanupJob stopped (host shutdown).");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LegacyBookingCleanupJob: error during cleanup tick");
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

        _logger.LogInformation("LegacyBookingCleanupJob stopped.");
    }
}