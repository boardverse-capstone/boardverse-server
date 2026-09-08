using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Background job tự động mở cửa sổ đánh giá Karma sau khi phiên chơi kết thúc.
/// State Machine: CLOSED → mở RatingOpenedAt → trigger Karma rating window.
/// </summary>
public class KarmaWindowJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KarmaWindowJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    public KarmaWindowJob(IServiceProvider serviceProvider, ILogger<KarmaWindowJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("KarmaWindowJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessKarmaWindowsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("KarmaWindowJob stopped (host shutdown).");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in KarmaWindowJob");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ProcessKarmaWindowsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();

        var now = DateTime.UtcNow;

        // GAP-2 fix: Include(l => l.Members) để job có thể reactivate members.
        // Trước đây job chỉ set RatingOpenedAt mà KHÔNG flip status Closed → RatingOpen
        // và KHÔNG reactivate members (ReservationService.MarkLobbyMembersInactive đã set
        // IsActive=false + Status=LobbyTerminated). Hậu quả:
        //   1. Member rate trước khi host gọi /open-karma-window → 403 vì status ≠ RatingOpen.
        //   2. Member rate sau khi host mở window → 403 vì members có IsActive=false
        //      → KarmaRatingRepository.GetLobbyForRatingAsync (filter IsActive) trả collection rỗng.
        // Fix: job tự động flip status + reactivate members (mirror KarmaRatingService.OpenLobbyKarmaRatingWindowAsync).
        var lobbiesToOpen = await db.Lobbies
            .Include(l => l.Members)
            .Where(l => l.Status == LobbyStatus.Closed && l.RatingOpenedAt == null)
            .ToListAsync(stoppingToken);

        if (lobbiesToOpen.Count == 0)
            return;

        _logger.LogInformation("Found {Count} lobbies to open karma window.", lobbiesToOpen.Count);

        foreach (var lobby in lobbiesToOpen)
        {
            lobby.RatingOpenedAt = now;
            lobby.Status = LobbyStatus.RatingOpen;
            lobby.UpdatedAt = now;

            // Reactivate members để KarmaRatingRepository.GetLobbyForRatingAsync
            // (filter Members.Where(IsActive)) trả collection có data.
            // Chỉ reactivate những member chưa bị Kicked/Left (giống KarmaRatingService).
            foreach (var member in lobby.Members)
            {
                if (member.Status is LobbyMemberStatus.Kicked or LobbyMemberStatus.Left)
                {
                    continue;
                }

                member.IsActive = true;
            }

            _logger.LogInformation(
                "Opened karma window for lobby {LobbyId} (status: Closed → RatingOpen, reactivated members: {MemberCount}).",
                lobby.Id, lobby.Members.Count(m => m.IsActive));
        }

        await db.SaveChangesAsync(stoppingToken);
        _logger.LogInformation("Processed {Count} karma windows.", lobbiesToOpen.Count);
    }
}
