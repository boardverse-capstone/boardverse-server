using BoardVerse.Core.Data;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
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
    options.UseNpgsql(connectionString);
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
            IsActive = true
        };
        db.Cafes.Add(cafe);
        await db.SaveChangesAsync();
        Console.WriteLine("✓ Demo cafe created");
    }
    else
    {
        Console.WriteLine("↻ Demo cafe already exists");
    }

    Console.WriteLine();
    Console.WriteLine("--- Test credentials ---");
    Console.WriteLine($"Manager login : {DevSeedConstants.ManagerEmail} / {DevSeedConstants.ManagerPassword}");
    Console.WriteLine($"Cafe ID       : {DevSeedConstants.DemoCafeId}");
    Console.WriteLine();
    Console.WriteLine("--- API flow ---");
    Console.WriteLine("1. POST /api/auth/login");
    Console.WriteLine("2. GET  /api/v1/master-games?searchTerm=catan");
    Console.WriteLine("3. POST /api/cafes/{cafeId}/inventory");
    Console.WriteLine("=== Done ===");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
