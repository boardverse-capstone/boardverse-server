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
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();

        await EnsureDevUsersAsync(db);
        await EnsureRequiredGamesAsync(db);
        await EnsureDemoCafeAsync(db);
        await EnsureDemoTablesAsync(db);
        await EnsureDemoCatanInventoryAsync(db);
        await EnsureDemoStaffAsync(db);
        await EnsureDemoLobbiesAsync(db);
        await ResetPosSessionStateAsync(db);
        await ResetMatchLobbyAsync(db);
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
        await EnsureUserAsync(db, DevSeedConstants.ManagerUserId, DevSeedConstants.ManagerEmail,
            DevSeedConstants.ManagerUsername, DevSeedConstants.ManagerPassword, UserRole.Manager);
        await EnsureUserAsync(db, DevSeedConstants.AdminUserId, DevSeedConstants.AdminEmail,
            DevSeedConstants.AdminUsername, DevSeedConstants.AdminPassword, UserRole.Admin);
        await EnsureUserAsync(db, DevSeedConstants.DemoPlayer1UserId, DevSeedConstants.Player1Email,
            DevSeedConstants.Player1Username, DevSeedConstants.DemoPlayerPassword, UserRole.Player, 1250, 100);
        await EnsureUserAsync(db, DevSeedConstants.DemoPlayer2UserId, DevSeedConstants.Player2Email,
            DevSeedConstants.Player2Username, DevSeedConstants.DemoPlayerPassword, UserRole.Player, 1180, 100);
        await EnsureUserAsync(db, DevSeedConstants.DemoPlayer3UserId, DevSeedConstants.Player3Email,
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
        var cafe = await db.Cafes.FirstOrDefaultAsync(c => c.Id == DevSeedConstants.DemoCafeId);
        if (cafe == null)
        {
            cafe = new Cafe
            {
                Id = DevSeedConstants.DemoCafeId,
                Name = DevSeedConstants.DemoCafeName,
                Address = DevSeedConstants.DemoCafeAddress,
                PhoneNumber = "0901234567",
                Description = "Integration test demo cafe",
                ManagerId = DevSeedConstants.ManagerUserId,
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
            cafe.ManagerId = DevSeedConstants.ManagerUserId;
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
        var posTable = await db.CafeTables.FirstOrDefaultAsync(t => t.Id == DevSeedConstants.DemoPosTableId);
        if (posTable == null)
        {
            posTable = new CafeTable
            {
                Id = DevSeedConstants.DemoPosTableId,
                CafeId = DevSeedConstants.DemoCafeId,
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
            posTable.CafeId = DevSeedConstants.DemoCafeId;
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
            i.Id == DevSeedConstants.DemoCatanInventoryId
            || (i.CafeId == DevSeedConstants.DemoCafeId && i.GameTemplateId == catan.Id && i.IsActive));

        if (inventory == null)
        {
            var now = DateTime.UtcNow;
            inventory = new CafeGameInventory
            {
                Id = DevSeedConstants.DemoCatanInventoryId,
                CafeId = DevSeedConstants.DemoCafeId,
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
            inventory.CafeId = DevSeedConstants.DemoCafeId;
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

        CafeInventoryBoxSyncHelper.ApplySync(inventory, boxes);
        await db.SaveChangesAsync();

        boxes = await db.CafeInventoryBoxes
            .Where(b => b.CafeGameInventoryId == inventory.Id && b.IsActive)
            .OrderBy(b => b.Barcode)
            .ToListAsync();

        foreach (var box in boxes)
        {
            box.Status = CafeGameInventoryStatus.Available;
            box.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        IntegrationTestFixtures.CatanInventoryId = inventory.Id;
        IntegrationTestFixtures.PosBoxBarcode = boxes.First().Barcode;
    }

    private static async Task EnsureDemoStaffAsync(BoardVerseDbContext db)
    {
        var staff = await db.CafeStaffs.FirstOrDefaultAsync(s =>
            s.CafeId == DevSeedConstants.DemoCafeId
            && s.UserId == DevSeedConstants.DemoPlayer2UserId);

        if (staff == null)
        {
            db.CafeStaffs.Add(new CafeStaff
            {
                CafeId = DevSeedConstants.DemoCafeId,
                UserId = DevSeedConstants.DemoPlayer2UserId,
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
            DevSeedConstants.DemoMatchLobbyId,
            catan.Id,
            LobbyStatus.InProgress,
            [DevSeedConstants.DemoPlayer1UserId, DevSeedConstants.DemoPlayer2UserId]);

        await EnsureLobbyAsync(
            db,
            DevSeedConstants.DemoKarmaLobbyId,
            catan.Id,
            LobbyStatus.Closed,
            [
                DevSeedConstants.DemoPlayer1UserId,
                DevSeedConstants.DemoPlayer2UserId,
                DevSeedConstants.DemoPlayer3UserId
            ]);
    }

    private static async Task EnsureLobbyAsync(
        BoardVerseDbContext db,
        Guid lobbyId,
        Guid gameTemplateId,
        LobbyStatus status,
        IReadOnlyList<Guid> memberUserIds)
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
                Members = []
            };
            db.Lobbies.Add(lobby);
        }
        else
        {
            lobby.GameTemplateId = gameTemplateId;
            lobby.Status = status;
            lobby.UpdatedAt = now;
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
        var posTable = await db.CafeTables.FirstAsync(t => t.Id == DevSeedConstants.DemoPosTableId);
        posTable.Status = CafeTableStatus.Available;
        posTable.UpdatedAt = DateTime.UtcNow;

        var activeSessions = await db.ActiveSessions
            .Where(s => s.IsActive && s.CafeId == DevSeedConstants.DemoCafeId)
            .ToListAsync();

        foreach (var session in activeSessions)
        {
            session.IsActive = false;
            session.EndedAt = DateTime.UtcNow;
        }

        var boxes = await db.CafeInventoryBoxes
            .Where(b => b.IsActive && b.CafeGameInventoryId == IntegrationTestFixtures.CatanInventoryId)
            .ToListAsync();

        foreach (var box in boxes)
        {
            box.Status = CafeGameInventoryStatus.Available;
            box.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    private static async Task ResetMatchLobbyAsync(BoardVerseDbContext db)
    {
        var submissions = await db.MatchResults
            .Where(m => m.LobbyId == DevSeedConstants.DemoMatchLobbyId)
            .ToListAsync();

        if (submissions.Count > 0)
        {
            db.MatchResults.RemoveRange(submissions);
            await db.SaveChangesAsync();
        }

        var lobby = await db.Lobbies.FirstOrDefaultAsync(l => l.Id == DevSeedConstants.DemoMatchLobbyId);
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
