using Npgsql;

var connString = "Host=ep-morning-darkness-aof95ckg.c-2.ap-southeast-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_GgPKb1sMxn7S;SSL Mode=Require;Trust Server Certificate=true;";

await using var conn = new NpgsqlConnection(connString);
await conn.OpenAsync();

await using var cmd = new NpgsqlCommand(
    "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 20",
    conn);
await using var reader = await cmd.ExecuteReaderAsync();
Console.WriteLine("Applied migrations:");
while (await reader.ReadAsync())
    Console.WriteLine($"  - {reader.GetString(0)}");
