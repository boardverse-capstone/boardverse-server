using BoardVerse.Core.Data;
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
var apiSettingsPath = Path.Combine(repoRoot, "BoardVerse.API", "appsettings.json");
var apiDevSettingsPath = Path.Combine(repoRoot, "BoardVerse.API", "appsettings.Development.json");

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile(apiSettingsPath, optional: true)
    .AddJsonFile(apiDevSettingsPath, optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
services.AddSingleton<IConfiguration>(configuration);

services.AddDbContext<BoardVerseDbContext>(options =>
{
    var connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? "Host=localhost;Port=5432;Database=boardverse;Username=postgres;Password=postgres";
    BoardVerseDbContextOptions.UseBoardVersePostgreSql(options, connectionString);
});

services.AddScoped<IGameTemplateRepository, GameTemplateRepository>();
services.AddScoped<IGameSeedService, GameSeedService>();

var serviceProvider = services.BuildServiceProvider();

try
{
    using var scope = serviceProvider.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
    await GameSchemaBootstrapper.EnsureGameTablesAsync(dbContext);

    var seedService = scope.ServiceProvider.GetRequiredService<IGameSeedService>();

    Console.WriteLine("=== BoardVerse Master Game Seed Tool ===");
    Console.WriteLine();
    Console.WriteLine("Games to seed:");
    foreach (var slug in GameCatalog.PopularGameSlugs)
    {
        var entry = GameCatalog.GetBySlug(slug);
        Console.WriteLine($"  • {entry?.Name ?? "Unknown"} ({slug})");
    }

    Console.WriteLine();
    await seedService.SeedGamesFromCatalogAsync(GameCatalog.PopularGameSlugs.ToList());
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
