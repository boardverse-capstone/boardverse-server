using Npgsql;

// ══════════════════════════════════════════════════════════════
// TEST SCRIPT: Weight Filter — Check Real Data Distribution
// Nhánh: TESTING (morning-darkness)
// ══════════════════════════════════════════════════════════════

var connStr = "Host=ep-morning-darkness-aof95ckg.c-2.ap-southeast-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_GgPKb1sMxn7S;SSL Mode=Require;Trust Server Certificate=true";

await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

Console.WriteLine("═══════════════════════════════════════════════════════");
Console.WriteLine("   🎲 WEIGHT FILTER TEST — Real Data Distribution");
Console.WriteLine("   Branch: TESTING (morning-darkness)");
Console.WriteLine("═══════════════════════════════════════════════════════\n");

// ── Step 1: Total active games ─────────────────────────────────
Console.WriteLine("📊 STEP 1: Total Active Games");
Console.WriteLine("─────────────────────────────────────────────────────");
int totalGames = 0;
await using (var cmd = new NpgsqlCommand(@"
    SELECT COUNT(*) FROM ""GameTemplates"" WHERE ""IsActive"" = true", conn))
{
    totalGames = Convert.ToInt32(await cmd.ExecuteScalarAsync());
    Console.WriteLine($"   Total active games: {totalGames}");
}
Console.WriteLine();

// ── Step 2: Weight distribution ────────────────────────────────
Console.WriteLine("📊 STEP 2: Weight Distribution by Range");
Console.WriteLine("─────────────────────────────────────────────────────");
await using (var cmd = new NpgsqlCommand(@"
    SELECT 
        weight_range,
        game_count,
        ROUND((game_count * 100.0 / @totalGames)::numeric, 2) as percentage
    FROM (
        SELECT 
            CASE 
                WHEN ""Weight"" IS NULL THEN 'NULL'
                WHEN ""Weight"" <= 2.0 THEN 'Light (≤2.0)'
                WHEN ""Weight"" > 2.0 AND ""Weight"" <= 3.0 THEN 'Medium-Light (2.0-3.0)'
                WHEN ""Weight"" > 3.0 AND ""Weight"" <= 4.0 THEN 'Medium (3.0-4.0)'
                WHEN ""Weight"" > 4.0 THEN 'Heavy (>4.0)'
            END as weight_range,
            COUNT(*) as game_count,
            CASE 
                WHEN ""Weight"" IS NULL THEN 0
                WHEN ""Weight"" <= 2.0 THEN 1
                WHEN ""Weight"" > 2.0 AND ""Weight"" <= 3.0 THEN 2
                WHEN ""Weight"" > 3.0 AND ""Weight"" <= 4.0 THEN 3
                WHEN ""Weight"" > 4.0 THEN 4
            END as sort_order
        FROM ""GameTemplates""
        WHERE ""IsActive"" = true
        GROUP BY weight_range, sort_order
    ) subquery
    ORDER BY sort_order", conn))
{
    cmd.Parameters.AddWithValue("totalGames", totalGames);
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var range = reader.GetString(0);
        var count = reader.GetInt64(1);
        var percentage = reader.GetDecimal(2);
        Console.WriteLine($"   {range,-25} {count,5} games ({percentage,6}%)");
    }
}
Console.WriteLine();

// ── Step 3: Sample games per range ─────────────────────────────
Console.WriteLine("📊 STEP 3: Sample Games per Weight Range (5 per range)");
Console.WriteLine("─────────────────────────────────────────────────────");

var ranges = new[]
{
    ("Light", "\"Weight\" <= 2.0"),
    ("Medium-Light", "\"Weight\" > 2.0 AND \"Weight\" <= 3.0"),
    ("Medium", "\"Weight\" > 3.0 AND \"Weight\" <= 4.0"),
    ("Heavy", "\"Weight\" > 4.0")
};

foreach (var (rangeName, whereClause) in ranges)
{
    Console.WriteLine($"\n🎯 {rangeName}:");
    await using var cmd = new NpgsqlCommand($@"
        SELECT ""Name"", ""Weight"", ""MinPlayers"", ""MaxPlayers"", ""PlayTime""
        FROM ""GameTemplates""
        WHERE ""IsActive"" = true AND {whereClause}
        ORDER BY ""Weight"", ""Name""
        LIMIT 5", conn);
    
    await using var reader = await cmd.ExecuteReaderAsync();
    var hasGames = false;
    while (await reader.ReadAsync())
    {
        hasGames = true;
        var name = reader.GetString(0);
        var weight = reader.GetDouble(1);
        var minPlayers = reader.GetInt32(2);
        var maxPlayers = reader.GetInt32(3);
        var playTime = reader.GetInt32(4);
        Console.WriteLine($"   • {name}");
        Console.WriteLine($"     Weight: {weight:F2} | Players: {minPlayers}-{maxPlayers} | Time: {playTime}min");
    }
    
    if (!hasGames)
    {
        Console.WriteLine($"   ⚠️  No games in this range");
    }
}

Console.WriteLine("\n─────────────────────────────────────────────────────");

// ── Step 4: Test WeightRange enum mapping ───────────────────────
Console.WriteLine("\n📊 STEP 4: Testing WeightRange Enum Mapping");
Console.WriteLine("─────────────────────────────────────────────────────");
Console.WriteLine("WeightRange enum values:");
Console.WriteLine("   1 = Light (≤2.0)");
Console.WriteLine("   2 = MediumLight (2.0-3.0)");
Console.WriteLine("   3 = Medium (3.0-4.0)");
Console.WriteLine("   4 = Heavy (>4.0)");
Console.WriteLine();

// Test filter for each enum value
var testCases = new[]
{
    (1, "Light", "\"Weight\" <= 2.0"),
    (2, "MediumLight", "\"Weight\" > 2.0 AND \"Weight\" <= 3.0"),
    (3, "Medium", "\"Weight\" > 3.0 AND \"Weight\" <= 4.0"),
    (4, "Heavy", "\"Weight\" > 4.0")
};

foreach (var (enumValue, enumName, whereClause) in testCases)
{
    await using var cmd = new NpgsqlCommand($@"
        SELECT COUNT(*)
        FROM ""GameTemplates""
        WHERE ""IsActive"" = true AND {whereClause}", conn);
    
    var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
    Console.WriteLine($"   WeightRange.{enumName} ({enumValue}): {count} games");
}

Console.WriteLine("\n═══════════════════════════════════════════════════════");
Console.WriteLine("✅ Weight data analysis complete!");
Console.WriteLine("═══════════════════════════════════════════════════════");
Console.WriteLine("\n🔥 Next Steps:");
Console.WriteLine("   1. Run API: dotnet run --project BoardVerse.API");
Console.WriteLine("   2. Test endpoint: POST /api/board-games/discovery/solo");
Console.WriteLine("   3. Test body examples:");
Console.WriteLine("      { \"weightRanges\": [1] }         → Light games only");
Console.WriteLine("      { \"weightRanges\": [1,2] }       → Light + Medium-Light");
Console.WriteLine("      { \"weightRanges\": [3,4] }       → Medium + Heavy");
Console.WriteLine("      { \"weightRanges\": [1,2,3,4] }   → All ranges");
