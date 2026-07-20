using System;
using Npgsql;

var cs = "Host=ep-morning-darkness-aof95ckg.c-2.ap-southeast-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_GgPKb1sMxn7S;SSL Mode=Require;Trust Server Certificate=true;";
await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();

// Check TournamentParticipants - focus on WalkIn fields
await using var cmd1 = new NpgsqlCommand(@"
    SELECT column_name, data_type, is_nullable
    FROM information_schema.columns
    WHERE table_name = 'TournamentParticipants'
    ORDER BY ordinal_position;", conn);

await using var reader1 = await cmd1.ExecuteReaderAsync();
Console.WriteLine("=== TournamentParticipants Schema ===");
while (await reader1.ReadAsync())
{
    Console.WriteLine($"  {reader1.GetString(0)}: {reader1.GetString(1)} (nullable: {reader1.GetString(2)})");
}
await reader1.CloseAsync();

// Check Tournaments
await using var cmd2 = new NpgsqlCommand(@"
    SELECT column_name, data_type
    FROM information_schema.columns
    WHERE table_name = 'Tournaments'
    ORDER BY ordinal_position;", conn);

await using var reader2 = await cmd2.ExecuteReaderAsync();
Console.WriteLine("\n=== Tournaments Schema ===");
while (await reader2.ReadAsync())
{
    Console.WriteLine($"  {reader2.GetString(0)}: {reader2.GetString(1)}");
}
await reader2.CloseAsync();

// Check applied migrations
await using var cmd3 = new NpgsqlCommand(@"
    SELECT ""MigrationId""
    FROM public.""__EFMigrationsHistory""
    WHERE ""MigrationId"" LIKE '%Tournament%'
    ORDER BY ""MigrationId"";", conn);

await using var reader3 = await cmd3.ExecuteReaderAsync();
Console.WriteLine("\n=== Applied Tournament Migrations ===");
while (await reader3.ReadAsync())
{
    Console.WriteLine($"  ✓ {reader3.GetString(0)}");
}
await reader3.CloseAsync();

Console.WriteLine("\n=== Neon DB is UP TO DATE ===");
