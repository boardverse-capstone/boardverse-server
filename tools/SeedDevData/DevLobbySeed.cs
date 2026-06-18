using BoardVerse.Core.Data;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data;
using BoardVerse.Data.Configurations;
using Microsoft.EntityFrameworkCore;

internal static class DevLobbySeed
{
    private static readonly DateTime CategorySeedDate = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static async Task RunAsync(BoardVerseDbContext db)
    {
        await EnsureCategoriesAsync(db);
        await EnsureUserWithProfileAsync(
            db,
            DevSeedConstants.AdminUserId,
            DevSeedConstants.AdminEmail,
            DevSeedConstants.AdminUsername,
            DevSeedConstants.AdminPassword,
            UserRole.Admin);
        await EnsureUserWithProfileAsync(
            db,
            DevSeedConstants.ManagerUserId,
            DevSeedConstants.ManagerEmail,
            DevSeedConstants.ManagerUsername,
            DevSeedConstants.ManagerPassword,
            UserRole.Manager);
        await EnsureUserWithProfileAsync(
            db,
            DevSeedConstants.DemoPlayer1UserId,
            DevSeedConstants.Player1Email,
            DevSeedConstants.Player1Username,
            DevSeedConstants.DemoPlayerPassword,
            UserRole.Player,
            globalElo: 1250,
            karmaPoints: 100);
        await EnsureUserWithProfileAsync(
            db,
            DevSeedConstants.DemoPlayer2UserId,
            DevSeedConstants.Player2Email,
            DevSeedConstants.Player2Username,
            DevSeedConstants.DemoPlayerPassword,
            UserRole.Player,
            globalElo: 1180,
            karmaPoints: 100);
        await EnsureUserWithProfileAsync(
            db,
            DevSeedConstants.DemoPlayer3UserId,
            DevSeedConstants.Player3Email,
            DevSeedConstants.Player3Username,
            DevSeedConstants.DemoPlayerPassword,
            UserRole.Player,
            globalElo: 1200,
            karmaPoints: 45);

        var catan = await db.GameTemplates
            .AsNoTracking()
            .Where(g => g.IsActive && EF.Functions.ILike(g.Name, "%catan%"))
            .OrderBy(g => g.Name)
            .FirstOrDefaultAsync();

        if (catan == null)
        {
            Console.WriteLine("✗ Skipped lobby seed — no active Catan game template found");
            return;
        }

        await EnsureCompetitiveCategoriesAsync(db, catan.Id);

        var activeSession = await db.ActiveSessions
            .AsNoTracking()
            .Where(s =>
                s.IsActive
                && s.CafeId == DevSeedConstants.DemoCafeId
                && s.GameTemplateId == catan.Id)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();

        await EnsureLobbyAsync(
            db,
            DevSeedConstants.DemoMatchLobbyId,
            catan.Id,
            LobbyStatus.InProgress,
            activeSession?.Id,
            [
                DevSeedConstants.DemoPlayer1UserId,
                DevSeedConstants.DemoPlayer2UserId
            ],
            "match");

        await EnsureLobbyAsync(
            db,
            DevSeedConstants.DemoKarmaLobbyId,
            catan.Id,
            LobbyStatus.Closed,
            activeSessionId: null,
            [
                DevSeedConstants.DemoPlayer1UserId,
                DevSeedConstants.DemoPlayer2UserId,
                DevSeedConstants.DemoPlayer3UserId
            ],
            "karma");
    }

    private static async Task EnsureCategoriesAsync(BoardVerseDbContext db)
    {
        var definitions = new (Guid Id, string Name, string Slug, string Description, int SortOrder)[]
        {
            (CategoryConfiguration.HiddenRoleId, "Ẩn vai", "an-vai", "Trò chơi suy luận vai trò bí mật", 1),
            (CategoryConfiguration.StrategyId, "Chiến thuật", "chien-thuat", "Tư duy chiến lược, tối ưu nguồn lực và điểm số", 2),
            (CategoryConfiguration.PartyId, "Giải trí", "giai-tri", "Nhẹ nhàng, vui vẻ, phù hợp tụ tập đông người", 3),
            (CategoryConfiguration.CooperativeId, "Hợp tác", "hop-tac", "Người chơi cùng phối hợp để đạt mục tiêu chung", 4),
            (CategoryConfiguration.CompetitiveId, "Đối kháng", "doi-khang", "Cạnh tranh trực tiếp giữa các người chơi", 5),
            (CategoryConfiguration.AdventureId, "Phiêu lưu", "phieu-luu", "Khám phá cốt truyện và thế giới trong game", 6)
        };

        var inserted = 0;
        foreach (var (id, name, slug, description, sortOrder) in definitions)
        {
            if (await db.Categories.AnyAsync(c => c.Id == id))
            {
                continue;
            }

            db.Categories.Add(new Category
            {
                Id = id,
                Name = name,
                Slug = slug,
                Description = description,
                SortOrder = sortOrder,
                IsActive = true,
                CreatedAt = CategorySeedDate,
                UpdatedAt = CategorySeedDate
            });
            inserted++;
        }

        if (inserted > 0)
        {
            await db.SaveChangesAsync();
            Console.WriteLine($"✓ Categories seeded ({inserted} new)");
        }
        else
        {
            Console.WriteLine("↻ Categories already present");
        }
    }

    private static async Task EnsureUserWithProfileAsync(
        BoardVerseDbContext db,
        Guid userId,
        string email,
        string username,
        string password,
        UserRole role,
        int globalElo = 1200,
        int karmaPoints = 100)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            user = new User
            {
                Id = userId,
                Email = email,
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Role = role,
                Provider = "Local",
                IsEmailVerified = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
            Console.WriteLine($"✓ User created: {username} ({role})");
        }
        else
        {
            await ClearConflictingDevIdentityAsync(db, userId, email, username);

            user.Email = email;
            user.Username = username;
            user.Role = role;
            user.Provider = "Local";
            user.IsEmailVerified = true;
            user.IsActive = true;
            user.AccountStatus = UserAccountStatus.Active;
            user.IsBlocked = false;
            user.BlockReason = null;
            user.BlockedAt = null;
            user.LockoutEndDate = null;
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            user.UpdatedAt = DateTime.UtcNow;
            Console.WriteLine($"↻ Dev user credentials refreshed: {username} ({role})");
        }

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
            Console.WriteLine($"  ✓ Profile created for {username}");
        }

        await db.SaveChangesAsync();
    }

    private static async Task ClearConflictingDevIdentityAsync(
        BoardVerseDbContext db,
        Guid userId,
        string email,
        string username)
    {
        var conflicts = await db.Users
            .Where(u => u.Id != userId && (u.Email == email || u.Username == username))
            .ToListAsync();

        foreach (var conflict in conflicts)
        {
            conflict.Email = $"orphan.{conflict.Id:N}@boardverse.dev.invalid";
            conflict.Username = $"orphan_{conflict.Id:N}"[..20];
            conflict.UpdatedAt = DateTime.UtcNow;
            Console.WriteLine($"↻ Cleared dev identity conflict for user {conflict.Id}");
        }

        if (conflicts.Count > 0)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureCompetitiveCategoriesAsync(BoardVerseDbContext db, Guid gameTemplateId)
    {
        var requiredCategoryIds = new[]
        {
            CategoryConfiguration.StrategyId,
            CategoryConfiguration.CompetitiveId
        };

        var linked = await db.GameTemplateCategories
            .Where(gtc => gtc.GameTemplateId == gameTemplateId)
            .Select(gtc => gtc.CategoryId)
            .ToListAsync();

        var added = 0;
        foreach (var categoryId in requiredCategoryIds)
        {
            if (linked.Contains(categoryId))
            {
                continue;
            }

            db.GameTemplateCategories.Add(new GameTemplateCategory
            {
                GameTemplateId = gameTemplateId,
                CategoryId = categoryId,
                CreatedAt = CategorySeedDate
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync();
            Console.WriteLine($"✓ Linked Catan to competitive categories ({added} new)");
        }
    }

    private static async Task EnsureLobbyAsync(
        BoardVerseDbContext db,
        Guid lobbyId,
        Guid gameTemplateId,
        LobbyStatus status,
        Guid? activeSessionId,
        IReadOnlyList<Guid> memberUserIds,
        string label)
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
                ActiveSessionId = activeSessionId,
                Status = status,
                CreatedAt = now,
                UpdatedAt = now,
                Members = []
            };
            db.Lobbies.Add(lobby);
            Console.WriteLine($"✓ Demo {label} lobby created ({lobbyId})");
        }
        else
        {
            lobby.GameTemplateId = gameTemplateId;
            lobby.ActiveSessionId = activeSessionId;
            lobby.Status = status;
            lobby.UpdatedAt = now;
            Console.WriteLine($"↻ Demo {label} lobby refreshed ({lobbyId})");
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
        Console.WriteLine($"  → {label} lobby: {memberUserIds.Count} members, status={status}");
    }
}
