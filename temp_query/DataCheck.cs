using var conn = new NpgsqlConnection("Host=ep-morning-darkness-aof95ckg.c-2.ap-southeast-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_GgPKb1sMxn7S;SSL Mode=Require");

await conn.OpenAsync();

// Check Users table
await using var cmd1 = new NpgsqlCommand(@"
    SELECT id, email, username, role, created_at 
    FROM public.""Users"" 
    ORDER BY created_at DESC 
    LIMIT 10;", conn);

await using var reader1 = await cmd1.ExecuteReaderAsync();
Console.WriteLine("=== Recent Users ===");
while (await reader1.ReadAsync())
{
    Console.WriteLine($"ID: {reader1.GetGuid(0)}, Email: {reader1.GetString(1)}, Username: {reader1.GetString(2)}, Role: {reader1.GetInt32(3)}, Created: {reader1.GetDateTime(4)}");
}

await reader1.CloseAsync();

// Check Cafes table
await using var cmd2 = new NpgsqlCommand(@"
    SELECT id, name, address, manager_id, is_active, created_at 
    FROM public.""Cafes"" 
    ORDER BY created_at DESC 
    LIMIT 10;", conn);

await using var reader2 = await cmd2.ExecuteReaderAsync();
Console.WriteLine("\n=== Recent Cafes ===");
while (await reader2.ReadAsync())
{
    Console.WriteLine($"ID: {reader2.GetGuid(0)}, Name: {reader2.GetString(1)}, Manager: {reader2.GetGuid(2)}, Active: {reader2.GetBoolean(3)}");
}

await reader2.CloseAsync();

// Check BookingDeposits table
await using var cmd3 = new NpgsqlCommand(@"
    SELECT id, order_id, amount, status, cafe_id, created_at 
    FROM public.""BookingDeposits"" 
    ORDER BY created_at DESC 
    LIMIT 10;", conn);

await using var reader3 = await cmd3.ExecuteReaderAsync();
Console.WriteLine("\n=== Recent BookingDeposits ===");
while (await reader3.ReadAsync())
{
    var orderId = reader3.IsDBNull(1) ? "NULL" : reader3.GetString(1);
    Console.WriteLine($"ID: {reader3.GetGuid(0)}, OrderID: {orderId}, Amount: {reader3.GetDecimal(2)}, Status: {reader3.GetInt32(3)}");
}
