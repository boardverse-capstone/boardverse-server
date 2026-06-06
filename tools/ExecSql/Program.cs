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

var sql = "ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"Role\" character varying(50) NOT NULL DEFAULT 'User';";

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
