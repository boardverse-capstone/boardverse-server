using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Tests.Integration.Helpers;

/// <summary>
/// Reset demo players' active reservation/lobby state to avoid BR-USER-LIMIT-01
/// (max 1 host + 1 member + 2 tổng) blocking subsequent test runs.
///
/// Called both by fixture initialization and per-test setup.
/// </summary>
public static class PlayerReservationResetHelper
{
    public static async Task ResetAsync(BoardVerseDbContext db, params Guid[] playerIds)
    {
        foreach (var playerId in playerIds)
        {
            // Release any held BVC for this player.
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == playerId);
            if (wallet != null && wallet.HeldBalance > 0)
            {
                wallet.HeldBalance = 0;
                wallet.UpdatedAt = DateTime.UtcNow;
            }

            // Mark all non-terminal reservations by this player as CancelledByPlayer (6).
            try
            {
                await db.Database.ExecuteSqlRawAsync(@"
                    UPDATE ""Reservations""
                    SET ""Status"" = 6,
                        ""UpdatedAt"" = NOW()
                    WHERE ""HostId"" = {0}
                      AND ""Status"" NOT IN (3, 4, 5, 6, 7, 8);
                ", playerId);
            }
            catch
            {
                // Schema drift — ignore.
            }

            // Mark all non-terminal lobbies created by this player as HostCancelled.
            // Lobbies.Status is stored as VARCHAR (string), not int — check the active
            // values to filter. Aggressive cleanup: mark everything not Closed as terminal.
            try
            {
                await db.Database.ExecuteSqlRawAsync(@"
                    UPDATE ""Lobbies""
                    SET ""Status"" = 'HostCancelled',
                        ""UpdatedAt"" = NOW(),
                        ""ClosedAt"" = COALESCE(""ClosedAt"", NOW())
                    WHERE ""HostUserId"" = {0}
                      AND ""Status"" <> 'Closed';
                ", playerId);
            }
            catch
            {
                // Schema drift — ignore.
            }

            // Remove player from active lobby memberships (BR-USER-LIMIT-04 clean state).
            try
            {
                await db.Database.ExecuteSqlRawAsync(@"
                    DELETE FROM ""LobbyMembers""
                    WHERE ""UserId"" = {0}
                      AND ""LeftAt"" IS NULL;
                ", playerId);
            }
            catch
            {
                // Schema drift — ignore.
            }
        }

        await db.SaveChangesAsync();
    }
}