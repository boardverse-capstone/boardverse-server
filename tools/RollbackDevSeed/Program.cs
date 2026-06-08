using System.Text.Json;
using Npgsql;

var dir = new DirectoryInfo(AppContext.BaseDirectory);
while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BoardVerse.API", "appsettings.json")))
    dir = dir.Parent;

if (dir == null)
{
    Console.Error.WriteLine("Cannot find BoardVerse.API/appsettings.json");
    return 1;
}

var json = await File.ReadAllTextAsync(Path.Combine(dir.FullName, "BoardVerse.API", "appsettings.json"));
using var doc = JsonDocument.Parse(json);
var conn = doc.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString();
if (string.IsNullOrWhiteSpace(conn))
{
    Console.Error.WriteLine("No connection string found.");
    return 1;
}

var statements = new[]
{
    "DELETE FROM \"CafeGameComponentPenalties\"",
    "DELETE FROM \"CafeGameInventories\"",
    "DELETE FROM \"GameComponentTemplates\"",
    "DELETE FROM \"GameTemplates\"",
    "DELETE FROM \"CafeStaffs\" WHERE \"CafeId\" = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'",
    "DELETE FROM \"Cafes\" WHERE \"Id\" = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'",
    "DELETE FROM \"Users\" WHERE \"Id\" = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'",
    "DROP TABLE IF EXISTS \"CafeGameComponentPenalties\" CASCADE",
    "DROP TABLE IF EXISTS \"CafeGameInventories\" CASCADE",
    "DROP TABLE IF EXISTS \"GameComponentTemplates\" CASCADE",
    "DROP TABLE IF EXISTS \"GameTemplates\" CASCADE"
};

Console.WriteLine("Rolling back dev seed on: " + conn.Split(';')[0]);
Console.WriteLine();

await using var connection = new NpgsqlConnection(conn);
await connection.OpenAsync();

foreach (var sql in statements)
{
    try
    {
        await using var cmd = new NpgsqlCommand(sql, connection);
        var affected = await cmd.ExecuteNonQueryAsync();
        Console.WriteLine($"OK ({affected} rows): {sql}");
    }
    catch (PostgresException ex) when (ex.SqlState == "42P01")
    {
        Console.WriteLine($"Skip (table missing): {sql}");
    }
}

Console.WriteLine();
Console.WriteLine("Rollback complete.");

return 0;
