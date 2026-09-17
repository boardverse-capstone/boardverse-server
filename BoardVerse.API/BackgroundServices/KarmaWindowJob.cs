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

        // GAP-R6-BJ-KARMA Fix: dùng ExecuteUpdateAsync atomic thay vì load → mutate → save.
        // Trước đây: 2 instance cluster pick cùng lobby → cả 2 set RatingOpenedAt = now,
        //   Status = RatingOpen, reactivate members → duplicate audit trail và double reactivate.
        //   Member reactivation là idempotent (IsActive=true) nên không gây bug nghiêm trọng,
        //   nhưng vẫn là duplicate work.
        // Sau: ExecuteUpdateAsync WHERE Status=Closed AND RatingOpenedAt IS NULL
        //   → Postgres MVCC đảm bảo chỉ 1 instance flip được mỗi row. Re-query lấy IDs flipped,
        //   chỉ reactivate members cho những IDs đó.
        var flippedCount = await db.Lobbies
            .Where(l => l.Status == LobbyStatus.Closed && l.RatingOpenedAt == null)
            .ExecuteUpdateAsync(l => l
                .SetProperty(x => x.RatingOpenedAt, (DateTime?)now)
                .SetProperty(x => x.Status, LobbyStatus.RatingOpen)
                .SetProperty(x => x.UpdatedAt, now),
                stoppingToken);

        if (flippedCount == 0)
            return;

        // Re-query các lobby đã flip để reactivate members (idempotent — IsActive=true → true).
        var flippedLobbies = await db.Lobbies
            .Include(l => l.Members)
            .Where(l => l.Status == LobbyStatus.RatingOpen
                && l.RatingOpenedAt != null
                && l.UpdatedAt == now)
            .ToListAsync(stoppingToken);

        _logger.LogInformation("Found {Count} lobbies to open karma window.", flippedLobbies.Count);

        foreach (var lobby in flippedLobbies)
        {
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
        _logger.LogInformation("Processed {Count} karma windows.", flippedLobbies.Count);
    }
}
