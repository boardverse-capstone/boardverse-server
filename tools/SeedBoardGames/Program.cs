using BoardVerse.Core.Data;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Settings;
using BoardVerse.Data;
using BoardVerse.Data.Repositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var apiSettingsPath = Path.Combine(repoRoot, "BoardVerse.API", "appsettings.json");
var apiDevSettingsPath = Path.Combine(repoRoot, "BoardVerse.API", "appsettings.Development.json");

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile(apiSettingsPath, optional: true)
    .AddJsonFile(apiDevSettingsPath, optional: true)
    .AddEnvironmentVariables()
    .Build();

var bggToken = Environment.GetEnvironmentVariable("BGG_API_TOKEN");
if (!string.IsNullOrWhiteSpace(bggToken))
    configuration["Bgg:ApiToken"] = bggToken;

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
services.AddSingleton<IConfiguration>(configuration);
services.Configure<BggSettings>(configuration.GetSection(BggSettings.SectionName));

services.AddDbContext<BoardVerseDbContext>(options =>
{
    var connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? "Host=localhost;Port=5432;Database=boardverse;Username=postgres;Password=postgres";
    options.UseNpgsql(connectionString);
});

services.AddScoped<IGameTemplateRepository, GameTemplateRepository>();
services.AddHttpClient<IBggApiService, BggApiService>();
services.AddScoped<IGameSeedService, GameSeedService>();

var serviceProvider = services.BuildServiceProvider();

try
{
    using var scope = serviceProvider.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
    await GameSchemaBootstrapper.EnsureGameTablesAsync(dbContext);

    var seedService = scope.ServiceProvider.GetRequiredService<IGameSeedService>();
    var bggSettings = configuration.GetSection(BggSettings.SectionName).Get<BggSettings>() ?? new BggSettings();

    Console.WriteLine("=== BoardVerse Master Game Seed Tool ===");
    Console.WriteLine();

    if (string.IsNullOrWhiteSpace(bggSettings.ApiToken))
    {
        Console.WriteLine("BGG API token: NOT configured");
        Console.WriteLine("  → Seeding from curated catalog (names, components, metadata).");
        Console.WriteLine("  → To fetch live images from BGG, set Bgg:ApiToken in appsettings");
        Console.WriteLine("    or BGG_API_TOKEN environment variable after approval at:");
        Console.WriteLine("    https://boardgamegeek.com/applications");
    }
    else
    {
        Console.WriteLine("BGG API token: configured — will fetch live metadata + images from BGG.");
    }

    Console.WriteLine();
    Console.WriteLine("Games to seed:");
    foreach (var id in BggKnownGameCatalog.PopularGameIds)
    {
        var entry = BggKnownGameCatalog.GetById(id);
        Console.WriteLine($"  • {entry?.Name ?? "Unknown"} (BGG #{id})");
    }

    Console.WriteLine();
    await seedService.SeedGamesFromBggAsync(BggKnownGameCatalog.PopularGameIds.ToList());
    Console.WriteLine();
    Console.WriteLine("=== Done ===");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
finally
{
    if (serviceProvider is IDisposable disposable)
        disposable.Dispose();
}
