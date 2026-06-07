using BoardVerse.Data;
using BoardVerse.Data.Repositories;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddDbContext<BoardVerseDbContext>(options =>
{
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrEmpty(connectionString))
    {
        connectionString = "Host=localhost;Port=5432;Database=boardverse;Username=postgres;Password=postgres";
    }
    options.UseNpgsql(connectionString);
});
services.AddScoped<IGameTemplateRepository, GameTemplateRepository>();
services.AddHttpClient<IBggApiService, BggApiService>();
services.AddScoped<IGameSeedService, GameSeedService>();

var serviceProvider = services.BuildServiceProvider();

try
{
    var seedService = serviceProvider.GetRequiredService<IGameSeedService>();

    Console.WriteLine("=== BoardGameGeek Seed Tool ===");
    Console.WriteLine();
    Console.WriteLine("Popular Board Game IDs from BGG:");
    Console.WriteLine("1. Catan - 13");
    Console.WriteLine("2. Ticket to Ride - 9209");
    Console.WriteLine("3. Carcassonne - 822");
    Console.WriteLine("4. Pandemic - 30549");
    Console.WriteLine("5. Wingspan - 266192");
    Console.WriteLine("6. Azul - 230802");
    Console.WriteLine("7. Splendor - 148228");
    Console.WriteLine("8. Terraforming Mars - 167791");
    Console.WriteLine("9. Gloomhaven - 174430");
    Console.WriteLine("10. Codenames - 178900");
    Console.WriteLine();

    // Popular board game IDs from BoardGameGeek
    var popularGameIds = new List<int>
    {
        13,        // Catan
        9209,      // Ticket to Ride
        822,       // Carcassonne
        30549,     // Pandemic
        266192,    // Wingspan
        230802,    // Azul
        148228,    // Splendor
        167791,    // Terraforming Mars
        174430,    // Gloomhaven
        178900     // Codenames
    };

    Console.WriteLine($"Seeding {popularGameIds.Count} popular board games...");
    Console.WriteLine();

    await seedService.SeedGamesFromBggAsync(popularGameIds);

    Console.WriteLine();
    Console.WriteLine("=== Seeding Complete ===");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
finally
{
    if (serviceProvider is IDisposable disposable)
    {
        disposable.Dispose();
    }
}
