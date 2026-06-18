using Microsoft.Extensions.Configuration;

namespace BoardVerse.Tests.Integration.Infrastructure;

public static class TestConfiguration
{
    private static readonly Lazy<IConfiguration> Config = new(Build);

    public static IConfiguration Instance => Config.Value;

    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? Environment.GetEnvironmentVariable("NEON_CONNECTION")
        ?? Instance.GetConnectionString("DefaultConnection");

    private static IConfiguration Build()
    {
        var basePath = AppContext.BaseDirectory;

        return new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Testing.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }
}
