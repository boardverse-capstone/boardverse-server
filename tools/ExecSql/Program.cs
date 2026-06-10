using System.Text.Json;
using Npgsql;

string repoRoot = AppContext.BaseDirectory;
// repoRoot will be bin path; compute project root by walking up to repository
var dir = new DirectoryInfo(repoRoot);
while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BoardVerse.API", "appsettings.json")))
{
    dir = dir.Parent;
}

if (dir == null)
{
    Console.Error.WriteLine("Cannot find BoardVerse.API/appsettings.json from current location.");
    return 1;
}

var appsettingsPath = Path.Combine(dir.FullName, "BoardVerse.API", "appsettings.json");
var json = await File.ReadAllTextAsync(appsettingsPath);
using var doc = JsonDocument.Parse(json);
var root = doc.RootElement;
string? conn = null;
if (root.TryGetProperty("ConnectionStrings", out var cs) && cs.TryGetProperty("DefaultConnection", out var defaultConn))
{
    conn = defaultConn.GetString();
}

// Allow override via env var
var env = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (!string.IsNullOrWhiteSpace(env)) conn = env;

if (string.IsNullOrWhiteSpace(conn))
{
    Console.Error.WriteLine("No connection string found in appsettings.json or ConnectionStrings__DefaultConnection env var.");
    return 1;
}

Console.WriteLine("Using connection: " + conn.Split(';')[0]);

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/ExecSql -- <path-to-sql-file>");
    Console.Error.WriteLine("Example: dotnet run --project tools/ExecSql -- BoardVerse.Data/update-all-entities.sql");
    return 1;
}

var sqlPath = Path.IsPathRooted(args[0])
    ? args[0]
    : Path.Combine(dir.FullName, args[0].Replace('/', Path.DirectorySeparatorChar));

if (!File.Exists(sqlPath))
{
    Console.Error.WriteLine("SQL script not found: " + sqlPath);
    return 1;
}

Console.WriteLine("Executing: " + sqlPath);

var sql = await File.ReadAllTextAsync(sqlPath);

await using var c = new NpgsqlConnection(conn);
try
{
    await c.OpenAsync();
    await using var cmd = new NpgsqlCommand(sql, c);
    var res = await cmd.ExecuteNonQueryAsync();
    Console.WriteLine("Executed SQL. Result: " + res);
}
catch (Exception ex)
{
    Console.Error.WriteLine("Failed to execute SQL: " + ex.Message);
    return 1;
}

return 0;
