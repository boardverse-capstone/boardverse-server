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
    private static readonly string[] RequiredGameSlugs = ["catan", "azul", "ticket-to-ride", "splendor"];

    private static readonly SemaphoreSlim FixtureBootstrapLock = new(1, 1);

    public static async Task EnsureAllFixturesAsync(IServiceProvider services)
    {
        await FixtureBootstrapLock.WaitAsync();
        try
        {
            await EnsureAllFixturesCoreAsync(services);
        }
        finally
        {
            FixtureBootstrapLock.Release();
        }
    }

    private static async Task EnsureAllFixturesCoreAsync(IServiceProvider services)
    {
        // Generate unique IDs for this test run to avoid conflicts
        IntegrationTestFixtures.GenerateUniqueIds();

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();

        // Clean up orphan data from previous DB resets/runs
        // SKIPPED: CleanupOrphanDataAsync was causing "column t.Value does not exist" errors
        // on certain EF queries and is not strictly required for test correctness.
        // await CleanupOrphanDataAsync(db);

        await EnsureDevUsersAsync(db);
        await EnsureRequiredGamesAsync(db);
        await EnsureTournamentSupportForSplendorAsync(db);
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

    /// <summary>
    /// Cleans up orphan records from DB that may exist after DB reset/restore.
    /// </summary>
    private static async Task CleanupOrphanDataAsync(BoardVerseDbContext db)
    {
        try
        {
            // Clean orphan UserProfiles (where UserId doesn't exist)
            await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""UserProfiles"" 
                WHERE ""UserId"" NOT IN (SELECT ""Id"" FROM ""Users"")");

            // Clean orphan TournamentParticipants (where UserId or TournamentId doesn't exist)
            await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""TournamentParticipants"" 
                WHERE ""UserId"" NOT IN (SELECT ""Id"" FROM ""Users"") 
                   OR ""TournamentId"" NOT IN (SELECT ""Id"" FROM ""Tournaments"")");

            // Clean orphan TournamentMatchBrackets
            await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""TournamentMatchBrackets"" 
                WHERE ""TournamentId"" NOT IN (SELECT ""Id"" FROM ""Tournaments"")");

            // Clean orphan TournamentMatchEloContributions
            await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""TournamentMatchEloContributions"" 
                WHERE ""MatchId"" NOT IN (SELECT ""Id"" FROM ""TournamentMatchBrackets"")");

            // Clean other orphan FK records
            await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""LobbyMembers"" 
                WHERE ""LobbyId"" NOT IN (SELECT ""Id"" FROM ""Lobbies"") 
                   OR ""UserId"" NOT IN (SELECT ""Id"" FROM ""Users"")");

            await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""Lobbies"" 
                WHERE ""HostUserId"" NOT IN (SELECT ""Id"" FROM ""Users"")");

            await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""BookingDeposits"" 
                WHERE ""UserId"" NOT IN (SELECT ""Id"" FROM ""Users"") 
                   OR ""CafeId"" NOT IN (SELECT ""Id"" FROM ""Cafes"")");

            await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""ActiveSessions"" 
                WHERE ""HostUserId"" NOT IN (SELECT ""Id"" FROM ""Users"")");

            await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""IndividualSessions"" 
                WHERE ""UserId"" NOT IN (SELECT ""Id"" FROM ""Users"") 
                   OR ""ActiveSessionId"" NOT IN (SELECT ""Id"" FROM ""ActiveSessions"")");

            await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""SessionGames"" 
                WHERE ""SessionId"" NOT IN (SELECT ""Id"" FROM ""ActiveSessions"")");

            await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""KarmaRatingHistory"" 
                WHERE ""UserId"" NOT IN (SELECT ""Id"" FROM ""Users"")");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Cleanup orphan data failed: {ex.Message}");
        }
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
            UserId = IntegrationTestFixtures.DemoUserId,
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
        // Check schema compatibility upfront to avoid errors with old database.
        // Use ExecuteScalarAsync via raw ADO to avoid EF wrapping SELECT in a subquery alias
        // (which would emit `SELECT t."Value" FROM (...) AS t` and break against PostgreSQL).
        bool hasTournamentColumns;
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync();
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = 'GameTemplates'
                      AND column_name = 'TournamentMaxScorePerPlayer'
                );";
            var result = await cmd.ExecuteScalarAsync();
            hasTournamentColumns = result is bool b && b;
        }
        catch
        {
            hasTournamentColumns = false;
        }

        // Skip seeding if schema is old (missing tournament columns)
        if (!hasTournamentColumns)
        {
            return;
        }

        var changed = false;
        foreach (var slug in RequiredGameSlugs)
        {
            var entry = GameCatalog.GetBySlug(slug);
            if (entry == null)
            {
                continue;
            }

            // Check if game exists using raw SQL to avoid schema issues
            bool exists;
            try
            {
                var conn = db.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync();
                }
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT EXISTS (SELECT 1 FROM \"GameTemplates\" WHERE \"Name\" = '{entry.Name.Replace("'", "''")}' LIMIT 1);";
                var result = await cmd.ExecuteScalarAsync();
                exists = result is bool b && b;
            }
            catch
            {
                // If query fails, assume game doesn't exist
                exists = false;
            }

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

    /// <summary>
    /// Ensures Splendor is tournament-enabled for integration tests.
    /// The shared DB may have IsTournamentSupported = false for Splendor.
    /// Also sets the SplendorGameTemplateId fixture.
    /// </summary>
    private static async Task EnsureTournamentSupportForSplendorAsync(BoardVerseDbContext db)
    {
        // First, ensure Splendor game exists - seed it if not present
        Guid splendorId = Guid.Empty;
        
        // Try to find existing Splendor
        try
        {
            var existing = await db.Database
                .SqlQueryRaw<Guid>("SELECT \"Id\" AS \"Value\" FROM \"GameTemplates\" WHERE LOWER(\"Name\") = 'splendor' LIMIT 1")
                .FirstOrDefaultAsync();
            if (existing != Guid.Empty)
            {
                splendorId = existing;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Note: Could not query Splendor: {ex.Message}");
        }
        
        if (splendorId == Guid.Empty)
        {
            // Splendor doesn't exist - seed it directly
            Console.WriteLine("Seeding Splendor game template...");
            splendorId = Guid.NewGuid();
            var catalogEntry = GameCatalog.GetBySlug("splendor");
            var gameName = catalogEntry?.Name ?? "Splendor";
            var gameDesc = catalogEntry?.Description ?? "Classic gem-collecting game";
            
            db.GameTemplates.Add(new GameTemplate
            {
                Id = splendorId,
                Name = gameName,
                Description = gameDesc,
                MinPlayers = catalogEntry?.MinPlayers > 0 ? catalogEntry.MinPlayers : 2,
                MaxPlayers = catalogEntry?.MaxPlayers > 0 ? catalogEntry.MaxPlayers : 4,
                PlayTime = catalogEntry?.PlayTime > 0 ? catalogEntry.PlayTime : 60,
                BggId = catalogEntry?.BggId ?? 0,
                IsActive = true,
                IsTournamentSupported = true,
                TournamentMaxScorePerPlayer = 15,
                TournamentMinPlayersPerTable = 2,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            Console.WriteLine($"Splendor seeded with ID: {splendorId}");
        }
        else
        {
            // Update existing Splendor to be tournament-enabled
            await db.Database.ExecuteSqlRawAsync(@"
                UPDATE ""GameTemplates""
                SET ""IsTournamentSupported"" = true,
                    ""TournamentMaxScorePerPlayer"" = 15,
                    ""TournamentMinPlayersPerTable"" = 2
                WHERE LOWER(""Name"") = 'splendor'
                  AND (""IsTournamentSupported"" = false OR ""TournamentMaxScorePerPlayer"" IS NULL)");
        }
        
        // Set the fixture
        IntegrationTestFixtures.SplendorGameTemplateId = splendorId;
        Console.WriteLine($"SplendorGameTemplateId set to: {splendorId}");

        // Cleanup old tournament data from previous test runs
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""TournamentParticipants"" WHERE ""TournamentId"" IN (
                    SELECT ""Id"" FROM ""Tournaments"" WHERE ""CafeId"" IS NOT NULL
                )");
            await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""TournamentMatchBrackets"" WHERE ""TournamentId"" IN (
                    SELECT ""Id"" FROM ""Tournaments"" WHERE ""CafeId"" IS NOT NULL
                )");
            await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""Tournaments"" WHERE ""CafeId"" IS NOT NULL");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Cleanup failed: {ex.Message}");
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
        await EnsureUserAsync(db, IntegrationTestFixtures.DemoPlayer4UserId, DevSeedConstants.Player4Email,
            DevSeedConstants.Player4Username, DevSeedConstants.DemoPlayerPassword, UserRole.Player, 1220, 80);
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
                GamerTier = KarmaRatingHelper.ResolveTier(karmaPoints),
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
                PartnerOperationalStatus = CafePartnerOperationalStatus.Active,
                // Gap 4: Cafe SePay config for settlement destination
                SePayAccountNumber = "0855199924",
                SePayBankCode = "MBBank"
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
            cafe.SePayAccountNumber = "0855199924";
            cafe.SePayBankCode = "MBBank";
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
        // Query only for the Id to avoid EF Core mapping issues with missing columns
        // SqlQueryRaw<Guid> requires column named "Value"
        Guid catanId;
        try
        {
            // Try with IsActive filter first
            catanId = await db.Database
                .SqlQueryRaw<Guid>("SELECT \"Id\" AS \"Value\" FROM \"GameTemplates\" WHERE \"IsActive\" = true AND LOWER(\"Name\") LIKE '%catan%' ORDER BY \"Name\" LIMIT 1")
                .FirstOrDefaultAsync();
        }
        catch
        {
            try
            {
                // Fallback without IsActive
                catanId = await db.Database
                    .SqlQueryRaw<Guid>("SELECT \"Id\" AS \"Value\" FROM \"GameTemplates\" WHERE LOWER(\"Name\") LIKE '%catan%' ORDER BY \"Name\" LIMIT 1")
                    .FirstOrDefaultAsync();
            }
            catch
            {
                // Last resort - just get any game ID
                catanId = await db.Database
                    .SqlQueryRaw<Guid>("SELECT \"Id\" AS \"Value\" FROM \"GameTemplates\" LIMIT 1")
                    .FirstOrDefaultAsync();
            }
        }

        // Validate we got a valid ID
        if (catanId == Guid.Empty)
        {
            throw new InvalidOperationException("Integration bootstrap requires a Catan game template.");
        }

        // Now we can use catanId to work with CafeGameInventories
        var inventory = await db.CafeGameInventories.FirstOrDefaultAsync(i =>
            i.Id == IntegrationTestFixtures.DemoCatanInventoryId
            || (i.CafeId == IntegrationTestFixtures.DemoCafeId && i.GameTemplateId == catanId && i.IsActive));

        if (inventory == null)
        {
            var now = DateTime.UtcNow;
            inventory = new CafeGameInventory
            {
                Id = IntegrationTestFixtures.DemoCatanInventoryId,
                CafeId = IntegrationTestFixtures.DemoCafeId,
                GameTemplateId = catanId,
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
            inventory.GameTemplateId = catanId;
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
        // Get only the Id column to avoid schema mismatch when EF tries to materialize
        // the entity with all properties including new ones like IsTournamentSupported.
        // SqlQueryRaw<Guid> requires the result column to be named "Value".
        Guid catanId;
        try
        {
            var result = await db.Database
                .SqlQueryRaw<Guid>("SELECT \"Id\" AS \"Value\" FROM \"GameTemplates\" WHERE \"IsActive\" = true AND LOWER(\"Name\") LIKE '%catan%' ORDER BY \"Name\" LIMIT 1")
                .FirstOrDefaultAsync();
            catanId = result;
        }
        catch
        {
            try
            {
                var result = await db.Database
                    .SqlQueryRaw<Guid>("SELECT \"Id\" AS \"Value\" FROM \"GameTemplates\" WHERE LOWER(\"Name\") LIKE '%catan%' ORDER BY \"Name\" LIMIT 1")
                    .FirstOrDefaultAsync();
                catanId = result;
            }
            catch
            {
                // Last resort - use first game
                var firstGame = await db.GameTemplates.AsNoTracking().FirstOrDefaultAsync();
                catanId = firstGame?.Id ?? Guid.NewGuid();
            }
        }

        await EnsureLobbyAsync(
            db,
            IntegrationTestFixtures.DemoMatchLobbyId,
            catanId,
            LobbyStatus.InProgress,
            [IntegrationTestFixtures.DemoPlayer1UserId, IntegrationTestFixtures.DemoPlayer2UserId],
            hostUserId: IntegrationTestFixtures.DemoPlayer1UserId);

        await EnsureLobbyAsync(
            db,
            IntegrationTestFixtures.DemoKarmaLobbyId,
            catanId,
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
                HostUserId = hostUserId ?? memberUserIds.FirstOrDefault(),
                ShareCode = lobbyId.ToString("N")[..8].ToUpperInvariant()
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
            // BR-12: Always reset RatingOpenedAt so OpenKarmaWindowAsync can be called repeatedly
            lobby.RatingOpenedAt = null;
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

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // Another parallel test collection already inserted the same lobby/shareCode.
            // Detach the conflicting entity so the local scope can complete without re-throwing.
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private static bool IsDuplicateKey(DbUpdateException ex)
    {
        if (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505")
        {
            return true;
        }
        return ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true;
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
