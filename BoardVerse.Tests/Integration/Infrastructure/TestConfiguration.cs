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

    /// <summary>
    /// Normalize connection string: đảm bảo Include Error Detail=true để debug FK violation.
    /// PostgreSQL/Npgsql mặc định redact chi tiết FK error → khó debug.
    /// </summary>
    public static string NormalizeConnectionString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        const string Flag = "Include Error Detail=true";
        if (raw.Contains(Flag, StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        return raw.EndsWith(";", StringComparison.Ordinal)
            ? raw + Flag
            : raw + ";" + Flag;
    }

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
