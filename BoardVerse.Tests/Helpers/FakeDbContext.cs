using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace BoardVerse.Tests.Helpers;

/// <summary>
/// Stub DbContext dùng cho unit test không cần provider thật.
/// - Nếu chạy với DATABASE_URL / appsettings.json có DefaultConnection → dùng Postgres thật
///   (cùng connection string với integration tests trên nhánh testing).
/// - Nếu không có connection string → fallback dùng InMemory provider.
///   Lưu ý: InMemory không bind được JsonDocument (vd. PlayerActionHistory.Metadata),
///   service nào đụng tới entity đó nên dùng repo thay vì truy cập _db trực tiếp.
/// </summary>
public class FakeDbContext : BoardVerseDbContext
{
    private static readonly string? RealConnectionString = LoadConnectionString();

    public FakeDbContext() : base(BuildOptions())
    {
    }

    private static DbContextOptions<BoardVerseDbContext> BuildOptions()
    {
        var builder = new DbContextOptionsBuilder<BoardVerseDbContext>();

        if (!string.IsNullOrWhiteSpace(RealConnectionString))
        {
            builder.UseNpgsql(RealConnectionString, npgsql => npgsql.UseNetTopologySuite());
        }
        else
        {
            builder.UseInMemoryDatabase($"FakeDbContext-{Guid.NewGuid()}")
                   .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        }

        return builder.Options;
    }

    private static string? LoadConnectionString()
    {
        var envValue = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("NEON_CONNECTION");
        if (!string.IsNullOrWhiteSpace(envValue)) return envValue;

        var basePath = AppContext.BaseDirectory;
        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Testing.json", optional: true)
            .AddJsonFile("appsettings.local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        return config.GetConnectionString("DefaultConnection");
    }
}