using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var configuration = new ConfigurationBuilder()
    .SetBasePath(repoRoot)
    .AddJsonFile(Path.Combine(repoRoot, "BoardVerse.API", "appsettings.json"), optional: true)
    .AddJsonFile(Path.Combine(repoRoot, "BoardVerse.API", "appsettings.Development.json"), optional: true)
    .AddEnvironmentVariables()
    .Build();

var connectionString = configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("No database connection string configured.");

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
services.AddDbContext<BoardVerseDbContext>(options =>
    BoardVerseDbContextOptions.UseBoardVersePostgreSql(options, connectionString));

await using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();

if (args.Length > 0 && args[0].Equals("audit", StringComparison.OrdinalIgnoreCase))
{
    return await SchemaAudit.RunAsync(db, connectionString);
}

Console.WriteLine("=== BoardVerse schema bootstrap ===");
Console.WriteLine();

await GameSchemaBootstrapper.EnsureObsoleteTablesDroppedAsync(db);
Console.WriteLine("✓ Obsolete tables removed");

await GameSchemaBootstrapper.EnsureUserAndCafeTablesAsync(db);
Console.WriteLine("✓ Users, cafes, location, PostGIS");

await GameSchemaBootstrapper.EnsureGameTablesAsync(db);
Console.WriteLine("✓ Game templates, components (incl. BggId, ComponentKind)");

await GameSchemaBootstrapper.EnsureInventoryTablesAsync(db);
Console.WriteLine("✓ Inventory, POS tables");

await GameSchemaBootstrapper.EnsureLobbyAndKarmaRatingTablesAsync(db);
Console.WriteLine("✓ Lobbies, karma ratings");

await GameSchemaBootstrapper.EnsureMatchResultTablesAsync(db);
Console.WriteLine("✓ Match results, Elo history");

await GameSchemaBootstrapper.EnsureAuthTokenTablesAsync(db);
var staleRefreshTokensRemoved = await GameSchemaBootstrapper.CleanupStaleRefreshTokensAsync(db);
Console.WriteLine($"✓ Auth tokens (removed {staleRefreshTokensRemoved} stale refresh token(s))");

await GameSchemaBootstrapper.EnsureKarmaLogAndSystemConfigTablesAsync(db);
Console.WriteLine("✓ Karma logs, system configurations");

Console.WriteLine();
Console.WriteLine("=== Done ===");
return 0;
