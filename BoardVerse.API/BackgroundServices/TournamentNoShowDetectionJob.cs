using BoardVerse.Services.IServices;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Background job tự động đánh dấu no-show cho participants đã đăng ký
/// nhưng không check-in khi giải đấu bắt đầu.
///
/// Flow:
/// 1. Tìm tournament vừa start (Status = OnGoing, CurrentRound = 1, StartRoundAt != null)
/// 2. Với mỗi participant đăng ký nhưng chưa check-in:
///    - Đánh dấu NoShow
///    - Áp dụng Karma penalty (nếu có cấu hình)
///    - Refund entry fee (nếu có)
///    - Log audit trail
///
/// Job chạy mỗi 1 phút để detect tournament mới start.
/// </summary>
public class TournamentNoShowDetectionJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TournamentNoShowDetectionJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    public TournamentNoShowDetectionJob(
        IServiceProvider serviceProvider,
        ILogger<TournamentNoShowDetectionJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TournamentNoShowDetectionJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DetectNoShowsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TournamentNoShowDetectionJob");
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

        _logger.LogInformation("TournamentNoShowDetectionJob stopped.");
    }

    private async Task DetectNoShowsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = _serviceProvider.CreateScope();
        var tournamentService = scope.ServiceProvider.GetRequiredService<ITournamentService>();

        var result = await tournamentService.AutoMarkNoShowsAsync(ct);
        if (result.TotalMarked > 0)
        {
            _logger.LogInformation(
                "TournamentNoShowDetectionJob marked {Marked} no-shows for tournament {TournamentId}.",
                result.TotalMarked, result.TournamentId);
        }
    }
}
