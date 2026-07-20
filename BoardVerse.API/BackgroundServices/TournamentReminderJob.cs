using BoardVerse.Services.IServices;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Background job gửi reminder notification cho participants trước giờ giải đấu.
///
/// Reminder schedule:
/// - T-30 phút: Push notification "Giải đấu bắt đầu sau 30 phút"
/// - T-15 phút: Push notification lần 2
/// - T-5 phút:  Push notification cuối cùng
///
/// Job chạy mỗi 5 phút, check tournament nào sắp start trong vòng 30 phút tới.
/// </summary>
public class TournamentReminderJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TournamentReminderJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public TournamentReminderJob(
        IServiceProvider serviceProvider,
        ILogger<TournamentReminderJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TournamentReminderJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendUpcomingRemindersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TournamentReminderJob");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("TournamentReminderJob stopped.");
    }

    private async Task SendUpcomingRemindersAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = _serviceProvider.CreateScope();
        var tournamentService = scope.ServiceProvider.GetRequiredService<ITournamentService>();

        var now = DateTime.UtcNow;

        // Tìm tournament sắp start trong 30 phút, ở trạng thái RegistrationOpen/RegistrationClosed
        // và chưa được reminder trong khoảng thời gian tương ứng
        var sent = await tournamentService.SendTournamentRemindersAsync(now, ct);
        if (sent > 0)
        {
            _logger.LogInformation(
                "TournamentReminderJob sent {Count} reminders.",
                sent);
        }
    }
}
