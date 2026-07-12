using System;
using Npgsql;

var cs = "Host= ep-morning-darkness-aof95ckg.c-2.ap-southeast-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_GgPKb1sMxn7S;SSL Mode=Require;Trust Server Certificate=true;";
await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();

await using var cmd1 = new NpgsqlCommand("SELECT id, order_id, amount, status, refund_policy, created_at FROM public.booking_deposits WHERE order_id = 'WEB001' ORDER BY created_at DESC LIMIT 5;", conn);
await using var r1 = await cmd1.ExecuteReaderAsync();
Console.WriteLine("BOOKING_DEPOSITS");
while (await r1.ReadAsync())
{
    Console.WriteLine($"{r1.GetGuid(0)} | {r1.GetString(1)} | {r1.GetDecimal(2)} | {r1.GetInt32(3)} | {r1.GetInt32(4)} | {r1.GetDateTime(5)}");
}
await r1.CloseAsync();

await using var cmd2 = new NpgsqlCommand("SELECT id, is_active, provider, masked_account_number FROM public.payment_master_accounts LIMIT 5;", conn);
await using var r2 = await cmd2.ExecuteReaderAsync();
Console.WriteLine("PAYMENT_MASTER_ACCOUNTS");
while (await r2.ReadAsync())
{
    string masked = r2.IsDBNull(3) ? "NULL" : r2.GetString(3);
    Console.WriteLine($"{r2.GetGuid(0)} | {r2.GetBoolean(1)} | {r2.GetString(2)} | {masked}");
}
await r2.CloseAsync();
