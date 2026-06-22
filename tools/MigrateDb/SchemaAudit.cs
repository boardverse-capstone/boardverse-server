using BoardVerse.Data;
using Npgsql;

internal static class SchemaAudit
{
    /// <summary>PostGIS / extension-owned tables in public schema — not app tables.</summary>
    private static readonly HashSet<string> AllowedInfrastructureTables =
    [
        "spatial_ref_sys"
    ];

    private static readonly HashSet<string> ExpectedTables =
    [
        "Users", "UserProfiles", "RefreshTokens",
        "Cafes", "CafeStaffs", "CafePartnerApplications",
        "GameTemplates", "GameComponentTemplates", "Categories", "GameTemplateCategories",
        "CafeGameInventories", "CafeGameComponentPenalties", "CafeInventoryBoxes",
        "ActiveSessions", "CafeTables", "PlayerLocationHistories",
        "Lobbies", "LobbyMembers", "PlayerKarmaRatings",
        "MatchResults", "MatchHistories", "MatchHistoryParticipants",
        "KarmaLogs", "SystemConfigurations"
    ];

    private static readonly Dictionary<string, string[]> ExpectedColumns = new()
    {
        ["Users"] = ["AccountStatus", "LockoutEndDate"],
        ["GameTemplates"] = ["BggId", "BggSyncedAt", "IsActive"],
        ["GameComponentTemplates"] = ["ComponentKind"],
        ["Cafes"] = ["Latitude", "Longitude", "Location"],
        ["UserProfiles"] = ["LastKnownLatitude", "LastKnownLongitude", "KarmaPoints"],
        ["KarmaLogs"] =
        [
            "Id", "UserId", "ViolationCategory", "Source", "KarmaPointsChange",
            "KarmaBefore", "KarmaAfter", "Reason", "PerformedByUserId", "IsAdminAdjustment", "CreatedAt"
        ],
        ["SystemConfigurations"] = ["ConfigKey", "ConfigValue", "Description", "UpdatedAt"],
        ["Lobbies"] = ["Id", "GameTemplateId", "Status", "RatingOpenedAt"],
        ["PlayerKarmaRatings"] = ["LobbyId", "RaterUserId", "TargetUserId", "KarmaDeltaApplied"],
        ["MatchResults"] = ["LobbyId", "UserId", "Outcome"],
        ["MatchHistories"] = ["LobbyId", "WinnerUserId", "IsDraw"],
        ["CafeInventoryBoxes"] = ["Barcode", "Status"],
        ["ActiveSessions"] = ["CafeTableId", "CafeInventoryBoxId", "StartedAt"]
    };

    private static readonly string[] ExpectedConfigKeys =
    [
        "elo_k_factor", "karma_penalty_cancel", "karma_penalty_noshow",
        "matchmaking_radius_km", "matchmaking_elo_diff", "platform_commission_rate"
    ];

    public static async Task<int> RunAsync(BoardVerseDbContext db, string connectionString)
    {
        Console.WriteLine("=== Neon schema audit ===");
        Console.WriteLine();

        var issues = new List<string>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var tables = await QueryStringsAsync(conn,
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
            ORDER BY table_name
            """);

        var tableSet = tables.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expected in ExpectedTables.OrderBy(t => t))
        {
            if (!tableSet.Contains(expected))
            {
                issues.Add($"Missing table: {expected}");
            }
        }

        var extraTables = tableSet
            .Where(t => !ExpectedTables.Contains(t) && !AllowedInfrastructureTables.Contains(t))
            .OrderBy(t => t)
            .ToList();

        foreach (var (table, columns) in ExpectedColumns)
        {
            if (!tableSet.Contains(table))
            {
                continue;
            }

            var actualColumns = await QueryStringsAsync(conn,
                """
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = @table
                ORDER BY ordinal_position
                """,
                new NpgsqlParameter("table", table));

            var columnSet = actualColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var col in columns)
            {
                if (!columnSet.Contains(col))
                {
                    issues.Add($"Missing column: {table}.{col}");
                }
            }
        }

        if (tableSet.Contains("SystemConfigurations"))
        {
            var configKeys = await QueryStringsAsync(conn,
                """SELECT "ConfigKey" FROM "SystemConfigurations" ORDER BY "ConfigKey" """);

            foreach (var key in ExpectedConfigKeys)
            {
                if (!configKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    issues.Add($"Missing seed config: SystemConfigurations.{key}");
                }
            }
        }

        var postgis = await QueryStringsAsync(conn,
            """SELECT extname FROM pg_extension WHERE extname IN ('postgis') ORDER BY extname""");

        Console.WriteLine($"Tables in Neon: {tables.Count}");
        Console.WriteLine($"Expected core tables: {ExpectedTables.Count}");
        Console.WriteLine($"PostGIS installed: {(postgis.Contains("postgis") ? "yes" : "NO")}");
        Console.WriteLine();

        if (extraTables.Count > 0)
        {
            Console.WriteLine("Extra tables (not in expected core set):");
            foreach (var t in extraTables)
            {
                Console.WriteLine($"  + {t}");
            }

            Console.WriteLine();
        }

        if (issues.Count == 0)
        {
            Console.WriteLine("Result: schema matches expected bootstrap state.");
            return 0;
        }

        Console.WriteLine($"Result: {issues.Count} issue(s) found:");
        foreach (var issue in issues)
        {
            Console.WriteLine($"  ! {issue}");
        }

        return 1;
    }

    private static async Task<List<string>> QueryStringsAsync(
        NpgsqlConnection conn,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }

        var results = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }
}
