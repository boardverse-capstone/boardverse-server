using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;

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
    private static readonly bool IsTestingDb = RealConnectionString?.Contains("morning-darkness") == true;
    private static readonly object CleanupLock = new();

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

    /// <summary>
    /// Cleanup test rows từ testing DB (morning-darkness) trước mỗi test run.
    /// Chỉ chạy khi connected tới testing DB — an toàn tuyệt đối với production.
    /// </summary>
    public static async Task ResetTestDataAsync(CancellationToken ct = default)
    {
        if (!IsTestingDb || string.IsNullOrWhiteSpace(RealConnectionString)) return;

        await using var conn = new NpgsqlConnection(RealConnectionString);
        await conn.OpenAsync(ct);

        // Truncate tables theo thứ tự đúng (FK constraints).
        var tables = new[]
        {
            "LobbyMembers",
            "Lobbies",
            "Reservations",
            "PosCheckInTokens",
        };

        foreach (var table in tables)
        {
            try
            {
                await using var cmd = new NpgsqlCommand($"TRUNCATE TABLE \"{table}\" CASCADE;", conn);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") // table not found
            {
                // Table chưa tồn tại trong schema — skip.
            }
        }
    }
}