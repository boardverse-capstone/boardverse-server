using Npgsql;

var prodConn = "Host=ep-morning-feather-ao1lnyg0.c-2.ap-southeast-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_GgPKb1sMxn7S;SSL Mode=Require;Trust Server Certificate=true";
var testConn = "Host=ep-morning-darkness-aof95ckg.c-2.ap-southeast-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_GgPKb1sMxn7S;SSL Mode=Require;Trust Server Certificate=true";

var sqlFile = @"..\MakeSenderIdNullable.sql";

if (!File.Exists(sqlFile))
{
    Console.WriteLine($"SQL file not found: {sqlFile}");
    return;
}

var sql = await File.ReadAllTextAsync(sqlFile);

async Task ApplySql(string name, string connStr)
{
    Console.WriteLine($"\n{"=",60}");
    Console.WriteLine($"| APPLYING TO: {name}");
    Console.WriteLine($"{"=",60}");
    
    try
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        Console.WriteLine("Connected OK");
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.CommandTimeout = 60;
        await cmd.ExecuteNonQueryAsync();
        
        Console.WriteLine("✅ SQL applied successfully!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n❌ ERROR: {ex.Message}");
    }
}

Console.WriteLine($"Applying SQL from: {sqlFile}");
await ApplySql("PRODUCTION (ep-morning-feather)", prodConn);
await ApplySql("TESTING (ep-morning-darkness)", testConn);

Console.WriteLine($"\n{"=",60}");
Console.WriteLine("DONE");
