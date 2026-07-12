using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Background job xử lý các phòng chờ bị hủy tự động khi chưa đủ người.
/// BR-08: Tự động chuyển OPEN → TIMEOUT_FAILED nếu trước giờ hẹn X phút 
/// mà số lượng thành viên vẫn chưa đạt quy mô tối thiểu của tựa game.
/// </summary>
public class LobbyTimeoutJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LobbyTimeoutJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    public LobbyTimeoutJob(IServiceProvider serviceProvider, ILogger<LobbyTimeoutJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LobbyTimeoutJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessTimedOutLobbiesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in LobbyTimeoutJob");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ProcessTimedOutLobbiesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();

        var now = DateTime.UtcNow;
        var timedOutLobbies = await db.Lobbies
            .Include(l => l.Members)
            .Include(l => l.GameTemplate)
            .Where(l => l.Status == LobbyStatus.Open &&
                        l.ScheduledStartTime != null &&
                        l.ScheduledStartTime.Value.AddMinutes(-l.CancellationLeadTimeMinutes) <= now)
            .ToListAsync(stoppingToken);

        if (timedOutLobbies.Count == 0)
            return;

        _logger.LogInformation("Found {Count} lobbies to check for timeout.", timedOutLobbies.Count);

        foreach (var lobby in timedOutLobbies)
        {
            var minPlayers = lobby.GameTemplate?.MinPlayers ?? 2;
            if (lobby.Members.Count < minPlayers)
            {
                lobby.Status = LobbyStatus.TimeoutFailed;
                _logger.LogInformation("Lobby {LobbyId} timed out with {MemberCount} members (min: {MinPlayers})",
                    lobby.Id, lobby.Members.Count, minPlayers);
            }
        }

        await db.SaveChangesAsync(stoppingToken);
        _logger.LogInformation("Processed {Count} timed out lobbies.", timedOutLobbies.Count);
    }
}
