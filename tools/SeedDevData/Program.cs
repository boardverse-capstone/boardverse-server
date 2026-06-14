using BoardVerse.Core.Data;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Data.Repositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile(Path.Combine(repoRoot, "BoardVerse.API", "appsettings.json"), optional: true)
    .AddJsonFile(Path.Combine(repoRoot, "BoardVerse.API", "appsettings.Development.json"), optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
services.AddSingleton<IConfiguration>(configuration);
services.AddDbContext<BoardVerseDbContext>(options =>
{
    var connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? throw new InvalidOperationException("No database connection string configured.");
    BoardVerseDbContextOptions.UseBoardVersePostgreSql(options, connectionString);
});
services.AddScoped<IGameTemplateRepository, GameTemplateRepository>();
services.AddScoped<IGameSeedService, GameSeedService>();

var provider = services.BuildServiceProvider();

try
{
    using var scope = provider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
    var seedService = scope.ServiceProvider.GetRequiredService<IGameSeedService>();

    Console.WriteLine("=== BoardVerse Dev Seed ===");
    Console.WriteLine();

    await GameSchemaBootstrapper.EnsureUserAndCafeTablesAsync(db);
    await GameSchemaBootstrapper.EnsureGameTablesAsync(db);
    await GameSchemaBootstrapper.EnsureInventoryTablesAsync(db);
    Console.WriteLine("✓ Schema ready (games + inventory)");

    await seedService.SeedGamesFromCatalogAsync(GameCatalog.PopularGameSlugs.ToList());
    Console.WriteLine("✓ Master games seeded");

    var manager = await db.Users.FirstOrDefaultAsync(u => u.Id == DevSeedConstants.ManagerUserId);
    if (manager == null)
    {
        manager = new User
        {
            Id = DevSeedConstants.ManagerUserId,
            Email = DevSeedConstants.ManagerEmail,
            Username = DevSeedConstants.ManagerUsername,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DevSeedConstants.ManagerPassword),
            Role = UserRole.Manager,
            Provider = "Local",
            IsEmailVerified = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(manager);
        await db.SaveChangesAsync();
        Console.WriteLine("✓ Dev Manager user created");
    }
    else
    {
        Console.WriteLine("↻ Dev Manager user already exists");
    }

    var cafe = await db.Cafes.FirstOrDefaultAsync(c => c.Id == DevSeedConstants.DemoCafeId);
    if (cafe == null)
    {
        cafe = new Cafe
        {
            Id = DevSeedConstants.DemoCafeId,
            Name = DevSeedConstants.DemoCafeName,
            Address = DevSeedConstants.DemoCafeAddress,
            PhoneNumber = "0901234567",
            Description = "Demo cafe for testing board game inventory APIs",
            ManagerId = DevSeedConstants.ManagerUserId,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            PartnerOperationalStatus = CafePartnerOperationalStatus.Active
        };
        GeoLocationHelper.ApplyCoordinates(cafe, DevSeedConstants.DemoCafeLatitude, DevSeedConstants.DemoCafeLongitude);
        db.Cafes.Add(cafe);
        await db.SaveChangesAsync();
        Console.WriteLine("✓ Demo cafe created");
    }
    else
    {
        Console.WriteLine("↻ Demo cafe already exists");
        if (!GeoLocationHelper.HasCoordinates(cafe))
        {
            GeoLocationHelper.ApplyCoordinates(
                cafe,
                DevSeedConstants.DemoCafeLatitude,
                DevSeedConstants.DemoCafeLongitude);
            cafe.PartnerOperationalStatus = CafePartnerOperationalStatus.Active;
            await db.SaveChangesAsync();
            Console.WriteLine("↻ Demo cafe GPS location backfilled");
        }
    }

    var demoTableCount = await db.CafeTables.CountAsync(t => t.CafeId == DevSeedConstants.DemoCafeId && t.IsActive);
    if (demoTableCount == 0)
    {
        const int totalTables = 12;
        const int availableTables = 5;
        var tableNames = CafePartnerTableLayoutHelper.GenerateDefaultNames(totalTables);
        for (var i = 0; i < tableNames.Count; i++)
        {
            db.CafeTables.Add(new CafeTable
            {
                Id = Guid.NewGuid(),
                CafeId = DevSeedConstants.DemoCafeId,
                Name = tableNames[i],
                SortOrder = i,
                Status = i < availableTables
                    ? CafeTableStatus.Available
                    : CafeTableStatus.InUse,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"✓ Demo cafe tables seeded ({availableTables}/{totalTables} available)");
    }
    else
    {
        Console.WriteLine($"↻ Demo cafe already has {demoTableCount} tables");
    }

    var inventoriesWithoutBoxes = await db.CafeGameInventories
        .Where(i => i.IsActive && !db.CafeInventoryBoxes.Any(b => b.CafeGameInventoryId == i.Id && b.IsActive))
        .ToListAsync();

    foreach (var inventory in inventoriesWithoutBoxes)
    {
        var boxes = await db.CafeInventoryBoxes
            .Where(b => b.CafeGameInventoryId == inventory.Id)
            .ToListAsync();
        CafeInventoryBoxSyncHelper.ApplySync(inventory, boxes);
    }

    if (inventoriesWithoutBoxes.Count > 0)
    {
        await db.SaveChangesAsync();
        Console.WriteLine($"✓ Backfilled boxes for {inventoriesWithoutBoxes.Count} inventory row(s)");
    }

    var demoGame = await db.GameTemplates
        .AsNoTracking()
        .Where(g => g.IsActive && EF.Functions.ILike(g.Name, "%catan%"))
        .OrderBy(g => g.Name)
        .FirstOrDefaultAsync();

    if (demoGame != null)
    {
        var demoInventory = await db.CafeGameInventories.FirstOrDefaultAsync(i =>
            i.CafeId == DevSeedConstants.DemoCafeId
            && i.GameTemplateId == demoGame.Id
            && i.IsActive);

        if (demoInventory == null)
        {
            var now = DateTime.UtcNow;
            demoInventory = new CafeGameInventory
            {
                Id = Guid.NewGuid(),
                CafeId = DevSeedConstants.DemoCafeId,
                GameTemplateId = demoGame.Id,
                BoxQuantity = 2,
                Status = CafeGameInventoryStatus.Available,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true
            };
            db.CafeGameInventories.Add(demoInventory);
            await db.SaveChangesAsync();

            var newBoxes = new List<CafeInventoryBox>();
            CafeInventoryBoxSyncHelper.ApplySync(demoInventory, newBoxes);
            Console.WriteLine("✓ Demo Catan inventory created (2 boxes)");
        }

        var demoBoxes = await db.CafeInventoryBoxes
            .Where(b => b.CafeGameInventoryId == demoInventory.Id && b.IsActive)
            .OrderBy(b => b.Barcode)
            .ToListAsync();

        if (demoBoxes.Count == 0)
        {
            CafeInventoryBoxSyncHelper.ApplySync(demoInventory, demoBoxes);
            await db.SaveChangesAsync();
            demoBoxes = await db.CafeInventoryBoxes
                .Where(b => b.CafeGameInventoryId == demoInventory.Id && b.IsActive)
                .OrderBy(b => b.Barcode)
                .ToListAsync();
        }

        if (demoBoxes.Count >= 2)
        {
            demoBoxes[0].Status = CafeGameInventoryStatus.Available;
            var inUseBox = demoBoxes[1];
            inUseBox.Status = CafeGameInventoryStatus.InUse;
            inUseBox.UpdatedAt = DateTime.UtcNow;

            var hasSession = await db.ActiveSessions.AnyAsync(s =>
                s.CafeInventoryBoxId == inUseBox.Id && s.IsActive);

            if (!hasSession)
            {
                var table = await db.CafeTables
                    .Where(t => t.CafeId == DevSeedConstants.DemoCafeId && t.IsActive)
                    .OrderBy(t => t.SortOrder)
                    .FirstAsync();

                db.ActiveSessions.Add(new ActiveSession
                {
                    Id = Guid.NewGuid(),
                    CafeId = DevSeedConstants.DemoCafeId,
                    CafeTableId = table.Id,
                    CafeInventoryBoxId = inUseBox.Id,
                    GameTemplateId = demoGame.Id,
                    StartedAt = DateTime.UtcNow.AddMinutes(-40),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
                Console.WriteLine("✓ Demo active session seeded (Catan box in use, ~40 min elapsed)");
            }

            await db.SaveChangesAsync();
        }
    }

    Console.WriteLine();
    Console.WriteLine("--- Test credentials ---");
    Console.WriteLine($"Manager login : {DevSeedConstants.ManagerEmail} / {DevSeedConstants.ManagerPassword}");
    Console.WriteLine($"Cafe ID       : {DevSeedConstants.DemoCafeId}");
    Console.WriteLine();
    Console.WriteLine("--- API flow ---");
    Console.WriteLine("1. POST /api/auth/login");
    Console.WriteLine("2. GET  /api/v1/master-games?searchTerm=catan");
    Console.WriteLine("3. GET  /api/cafes/nearby?latitude=10.776889&longitude=106.700806&gameTemplateId={catanId}");
    Console.WriteLine("4. POST /api/cafes/{cafeId}/inventory");
    Console.WriteLine("=== Done ===");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
