using BoardVerse.Core.Data;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoardVerse.Tests.Integration.Infrastructure;

internal static class IntegrationTestDataBootstrapper
{
    private static readonly string[] RequiredGameSlugs = ["catan", "azul", "ticket-to-ride"];

    public static async Task EnsureAllFixturesAsync(IServiceProvider services)
    {
        // Generate unique IDs for this test run to avoid conflicts
        IntegrationTestFixtures.GenerateUniqueIds();

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();

        await EnsureDevUsersAsync(db);
        await EnsureRequiredGamesAsync(db);
        await EnsureDemoCafeAsync(db);
        await EnsureDemoTablesAsync(db);
        await EnsureDemoCatanInventoryAsync(db);
        await EnsureDemoStaffAsync(db);
        await EnsureDemoLobbiesAsync(db);
        await EnsureDemoBookingDepositAsync(db);
        await ResetPosSessionStateAsync(db);
        await ResetMatchLobbyAsync(db);
        await ResetLobbyStateAsync(db);
    }

    private static async Task EnsureDemoBookingDepositAsync(BoardVerseDbContext db)
    {
        var depositId = IntegrationTestFixtures.DemoBookingDepositId;
        var orderId = $"TEST-DEPOSIT-{Guid.NewGuid():N}";  // Unique OrderId to avoid constraint violations

        // Clean up any existing deposit with this ID first
        var existing = await db.BookingDeposits.FindAsync(depositId);
        if (existing != null)
        {
            db.BookingDeposits.Remove(existing);
            await db.SaveChangesAsync();
        }

        var deposit = new BookingDeposit
        {
            Id = depositId,
            CafeId = IntegrationTestFixtures.DemoCafeId,
            CafeManagerId = IntegrationTestFixtures.ManagerUserId,
            Amount = 50000,
            Status = BookingDepositStatus.Pending,  // Will be marked Paid by test webhook
            RefundPolicy = DepositRefundPolicy.Full,
            ActiveSessionId = Guid.Empty,  // Will be linked when session starts
            OrderId = orderId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.BookingDeposits.Add(deposit);
        await db.SaveChangesAsync();
    }

    private static async Task ResetLobbyStateAsync(BoardVerseDbContext db)
    {
        // Reset all open lobbies to HostCancelled to ensure clean test environment
        var openLobbies = await db.Lobbies
            .Where(l => l.Status == LobbyStatus.Open)
            .ToListAsync();

        foreach (var lobby in openLobbies)
        {
            lobby.Status = LobbyStatus.HostCancelled;
            lobby.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureRequiredGamesAsync(BoardVerseDbContext db)
    {
        var changed = false;
        foreach (var slug in RequiredGameSlugs)
        {
            var entry = GameCatalog.GetBySlug(slug);
            if (entry == null)
            {
                continue;
            }

            var exists = await db.GameTemplates.AnyAsync(g => g.IsActive && g.Name == entry.Name);
            if (exists)
            {
                continue;
            }

            var minPlayers = entry.MinPlayers > 0 ? entry.MinPlayers : 1;
            var maxPlayers = entry.MaxPlayers > 0 ? entry.MaxPlayers : 4;
            db.GameTemplates.Add(new GameTemplate
            {
                Id = Guid.NewGuid(),
                Name = entry.Name,
                Description = entry.Description,
                MinPlayers = minPlayers,
                MaxPlayers = maxPlayers,
                PlayTime = entry.PlayTime > 0 ? entry.PlayTime : 60,
                BggId = entry.BggId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureDevUsersAsync(BoardVerseDbContext db)
    {
        await EnsureUserAsync(db, IntegrationTestFixtures.ManagerUserId, DevSeedConstants.ManagerEmail,
            DevSeedConstants.ManagerUsername, DevSeedConstants.ManagerPassword, UserRole.Manager);
        await EnsureUserAsync(db, IntegrationTestFixtures.AdminUserId, DevSeedConstants.AdminEmail,
            DevSeedConstants.AdminUsername, DevSeedConstants.AdminPassword, UserRole.Admin);
        await EnsureUserAsync(db, IntegrationTestFixtures.DemoPlayer1UserId, DevSeedConstants.Player1Email,
            DevSeedConstants.Player1Username, DevSeedConstants.DemoPlayerPassword, UserRole.Player, 1250, 100);
        await EnsureUserAsync(db, IntegrationTestFixtures.DemoPlayer2UserId, DevSeedConstants.Player2Email,
            DevSeedConstants.Player2Username, DevSeedConstants.DemoPlayerPassword, UserRole.Player, 1180, 100);
        await EnsureUserAsync(db, IntegrationTestFixtures.DemoPlayer3UserId, DevSeedConstants.Player3Email,
            DevSeedConstants.Player3Username, DevSeedConstants.DemoPlayerPassword, UserRole.Player, 1200, 45);
    }

    private static async Task EnsureUserAsync(
        BoardVerseDbContext db,
        Guid userId,
        string email,
        string username,
        string password,
        UserRole role,
        int globalElo = 1200,
        int karmaPoints = 100)
    {
        await ClearIdentityConflictsAsync(db, userId, email, username);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            user = new User
            {
                Id = userId,
                Email = email,
                Username = username,
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
        }

        user.Email = email;
        user.Username = username;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        user.Role = role;
        user.Provider = "Local";
        user.IsEmailVerified = true;
        user.IsActive = true;
        user.AccountStatus = UserAccountStatus.Active;
        user.BlockReason = null;
        user.BlockedAt = null;
        user.LockoutEndDate = null;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile == null)
        {
            db.UserProfiles.Add(new UserProfile
            {
                UserId = userId,
                GlobalElo = globalElo,
                KarmaPoints = karmaPoints,
                GamerTier = karmaPoints >= 150 ? GamerTier.Silver : GamerTier.Bronze,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureDemoCafeAsync(BoardVerseDbContext db)
    {
        var cafe = await db.Cafes.FirstOrDefaultAsync(c => c.Id == IntegrationTestFixtures.DemoCafeId);
        if (cafe == null)
        {
            cafe = new Cafe
            {
                Id = IntegrationTestFixtures.DemoCafeId,
                Name = DevSeedConstants.DemoCafeName,
                Address = DevSeedConstants.DemoCafeAddress,
                PhoneNumber = "0901234567",
                Description = "Integration test demo cafe",
                ManagerId = IntegrationTestFixtures.ManagerUserId,
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                PartnerOperationalStatus = CafePartnerOperationalStatus.Active
            };
            GeoLocationHelper.ApplyCoordinates(
                cafe,
                DevSeedConstants.DemoCafeLatitude,
                DevSeedConstants.DemoCafeLongitude);
            db.Cafes.Add(cafe);
        }
        else
        {
            cafe.ManagerId = IntegrationTestFixtures.ManagerUserId;
            cafe.IsActive = true;
            cafe.PartnerOperationalStatus = CafePartnerOperationalStatus.Active;
            GeoLocationHelper.ApplyCoordinates(
                cafe,
                DevSeedConstants.DemoCafeLatitude,
                DevSeedConstants.DemoCafeLongitude);
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureDemoTablesAsync(BoardVerseDbContext db)
    {
        var posTable = await db.CafeTables.FirstOrDefaultAsync(t => t.Id == IntegrationTestFixtures.DemoPosTableId);
        if (posTable == null)
        {
            posTable = new CafeTable
            {
                Id = IntegrationTestFixtures.DemoPosTableId,
                CafeId = IntegrationTestFixtures.DemoCafeId,
                Name = "Integration POS Table",
                SortOrder = 0,
                Status = CafeTableStatus.Available,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };
            db.CafeTables.Add(posTable);
        }
        else
        {
            posTable.CafeId = IntegrationTestFixtures.DemoCafeId;
            posTable.IsActive = true;
            posTable.Status = CafeTableStatus.Available;
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureDemoCatanInventoryAsync(BoardVerseDbContext db)
    {
        var catan = await db.GameTemplates
            .AsNoTracking()
            .Where(g => g.IsActive && EF.Functions.ILike(g.Name, "%catan%"))
            .OrderBy(g => g.Name)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Integration bootstrap requires a Catan game template.");

        var inventory = await db.CafeGameInventories.FirstOrDefaultAsync(i =>
            i.Id == IntegrationTestFixtures.DemoCatanInventoryId
            || (i.CafeId == IntegrationTestFixtures.DemoCafeId && i.GameTemplateId == catan.Id && i.IsActive));

        if (inventory == null)
        {
            var now = DateTime.UtcNow;
            inventory = new CafeGameInventory
            {
                Id = IntegrationTestFixtures.DemoCatanInventoryId,
                CafeId = IntegrationTestFixtures.DemoCafeId,
                GameTemplateId = catan.Id,
                BoxQuantity = 2,
                Status = CafeGameInventoryStatus.Available,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true
            };
            db.CafeGameInventories.Add(inventory);
        }
        else
        {
            inventory.CafeId = IntegrationTestFixtures.DemoCafeId;
            inventory.GameTemplateId = catan.Id;
            inventory.BoxQuantity = Math.Max(inventory.BoxQuantity, 2);
            inventory.IsActive = true;
            inventory.Status = CafeGameInventoryStatus.Available;
            inventory.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        var boxes = await db.CafeInventoryBoxes
            .Where(b => b.CafeGameInventoryId == inventory.Id)
            .ToListAsync();

        var knownBoxIds = boxes.Select(b => b.Id).ToHashSet();
        CafeInventoryBoxSyncHelper.ApplySync(inventory, boxes);
        foreach (var box in boxes.Where(b => !knownBoxIds.Contains(b.Id)))
        {
            db.CafeInventoryBoxes.Add(box);
        }

        await db.SaveChangesAsync();

        boxes = await db.CafeInventoryBoxes
            .Where(b => b.CafeGameInventoryId == inventory.Id && b.IsActive)
            .OrderBy(b => b.Barcode)
            .ToListAsync();

        if (boxes.Count == 0)
        {
            throw new InvalidOperationException(
                "Integration bootstrap requires at least one active inventory box for demo Catan.");
        }

        foreach (var box in boxes)
        {
            box.Status = CafeGameInventoryStatus.Available;
            box.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        IntegrationTestFixtures.DemoCatanInventoryId = inventory.Id;
        IntegrationTestFixtures.CatanBarcode = boxes[0].Barcode;
        IntegrationTestFixtures.PosBoxBarcode = boxes[0].Barcode;
    }

    private static async Task EnsureDemoStaffAsync(BoardVerseDbContext db)
    {
        // Add Manager as staff (required for POS operations)
        var managerStaff = await db.CafeStaffs.FirstOrDefaultAsync(s =>
            s.CafeId == IntegrationTestFixtures.DemoCafeId
            && s.UserId == IntegrationTestFixtures.ManagerUserId);

        if (managerStaff == null)
        {
            db.CafeStaffs.Add(new CafeStaff
            {
                CafeId = IntegrationTestFixtures.DemoCafeId,
                UserId = IntegrationTestFixtures.ManagerUserId,
                JoinedAt = DateTime.UtcNow
            });
        }

        // Also add Player2 as staff
        var player2Staff = await db.CafeStaffs.FirstOrDefaultAsync(s =>
            s.CafeId == IntegrationTestFixtures.DemoCafeId
            && s.UserId == IntegrationTestFixtures.DemoPlayer2UserId);

        if (player2Staff == null)
        {
            db.CafeStaffs.Add(new CafeStaff
            {
                CafeId = IntegrationTestFixtures.DemoCafeId,
                UserId = IntegrationTestFixtures.DemoPlayer2UserId,
                JoinedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureDemoLobbiesAsync(BoardVerseDbContext db)
    {
        var catan = await db.GameTemplates
            .AsNoTracking()
            .Where(g => g.IsActive && EF.Functions.ILike(g.Name, "%catan%"))
            .OrderBy(g => g.Name)
            .FirstAsync();

        await EnsureLobbyAsync(
            db,
            IntegrationTestFixtures.DemoMatchLobbyId,
            catan.Id,
            LobbyStatus.InProgress,
            [IntegrationTestFixtures.DemoPlayer1UserId, IntegrationTestFixtures.DemoPlayer2UserId],
            hostUserId: IntegrationTestFixtures.DemoPlayer1UserId);

        await EnsureLobbyAsync(
            db,
            IntegrationTestFixtures.DemoKarmaLobbyId,
            catan.Id,
            LobbyStatus.Closed,
            [
                IntegrationTestFixtures.DemoPlayer1UserId,
                IntegrationTestFixtures.DemoPlayer2UserId,
                IntegrationTestFixtures.DemoPlayer3UserId
            ],
            hostUserId: IntegrationTestFixtures.DemoPlayer1UserId);
    }

    private static async Task EnsureLobbyAsync(
        BoardVerseDbContext db,
        Guid lobbyId,
        Guid gameTemplateId,
        LobbyStatus status,
        IReadOnlyList<Guid> memberUserIds,
        Guid? hostUserId = null)
    {
        var now = DateTime.UtcNow;
        var lobby = await db.Lobbies
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == lobbyId);

        if (lobby == null)
        {
            lobby = new Lobby
            {
                Id = lobbyId,
                GameTemplateId = gameTemplateId,
                Status = status,
                CreatedAt = now,
                UpdatedAt = now,
                Members = [],
                HostUserId = hostUserId ?? memberUserIds.FirstOrDefault()
            };
            db.Lobbies.Add(lobby);
        }
        else
        {
            lobby.GameTemplateId = gameTemplateId;
            lobby.Status = status;
            lobby.UpdatedAt = now;
            if (hostUserId.HasValue)
            {
                lobby.HostUserId = hostUserId.Value;
            }
        }

        foreach (var userId in memberUserIds)
        {
            if (lobby.Members.Any(m => m.UserId == userId && m.IsActive))
            {
                continue;
            }

            db.LobbyMembers.Add(new LobbyMember
            {
                Id = Guid.NewGuid(),
                LobbyId = lobbyId,
                UserId = userId,
                JoinedAt = now,
                IsActive = true
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task ResetPosSessionStateAsync(BoardVerseDbContext db)
    {
        var posTable = await db.CafeTables.FirstOrDefaultAsync(t => t.Id == IntegrationTestFixtures.DemoPosTableId);
        if (posTable != null)
        {
            posTable.Status = CafeTableStatus.Available;
            posTable.UpdatedAt = DateTime.UtcNow;
        }

        // Clean up old sessions - handle both old and new schema gracefully
        try
        {
            // Delete members first (table may not exist in old schema)
            await db.Database.ExecuteSqlRawAsync(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_tables WHERE tablename = 'ActiveSessionMembers') THEN
                        DELETE FROM ""ActiveSessionMembers"" WHERE ""ActiveSessionId"" IN (SELECT ""Id"" FROM ""ActiveSessions"" WHERE ""CafeId"" = {0});
                    END IF;
                END $$;
                DELETE FROM ""ActiveSessions"" WHERE ""CafeId"" = {0};
            ", IntegrationTestFixtures.DemoCafeId);
        }
        catch
        {
            // Schema might be incomplete, ignore
        }

        // Reset ALL boxes in demo cafe to Available - not just Catan
        var allBoxes = await db.CafeInventoryBoxes
            .Include(b => b.CafeGameInventory)
            .Where(b => b.IsActive && b.CafeGameInventory.CafeId == IntegrationTestFixtures.DemoCafeId)
            .ToListAsync();

        foreach (var box in allBoxes)
        {
            box.Status = CafeGameInventoryStatus.Available;
            box.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        // Refresh the barcode fixture to first available box
        if (allBoxes.Count > 0)
        {
            IntegrationTestFixtures.PosBoxBarcode = allBoxes[0].Barcode;
        }
    }

    private static async Task ResetMatchLobbyAsync(BoardVerseDbContext db)
    {
        var submissions = await db.MatchResults
            .Where(m => m.LobbyId == IntegrationTestFixtures.DemoMatchLobbyId)
            .ToListAsync();

        if (submissions.Count > 0)
        {
            db.MatchResults.RemoveRange(submissions);
            await db.SaveChangesAsync();
        }

        var lobby = await db.Lobbies.FirstOrDefaultAsync(l => l.Id == IntegrationTestFixtures.DemoMatchLobbyId);
        if (lobby != null)
        {
            lobby.Status = LobbyStatus.InProgress;
            lobby.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    private static async Task ClearIdentityConflictsAsync(
        BoardVerseDbContext db,
        Guid userId,
        string email,
        string username)
    {
        var conflicts = await db.Users
            .Where(u => u.Id != userId && (u.Email == email || u.Username == username))
            .ToListAsync();

        if (conflicts.Count == 0)
        {
            return;
        }

        foreach (var conflict in conflicts)
        {
            conflict.Email = $"orphan.{conflict.Id:N}@boardverse.dev.invalid";
            conflict.Username = $"orphan_{conflict.Id:N}"[..20];
            conflict.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }
}
