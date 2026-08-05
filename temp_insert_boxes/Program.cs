// INSERT 2 CafeInventoryBoxes cho Boss cafe Gloomhaven inventory
// ⚠️ CHẠY TRÊN PRODUCTION (morning-feather)
using Npgsql;

var connStr = "Host=ep-morning-feather-ao1lnyg0.c-2.ap-southeast-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_GgPKb1sMxn7S;SSL Mode=Require;Trust Server Certificate=true;";

var cafeId = Guid.Parse("a1aae9db-4f1b-44af-ac86-6038d085df94");
var inventoryId = Guid.Parse("250e1277-7c62-4c32-bd4c-f0cb7f02999b");
var now = DateTime.UtcNow;

var barcodes = new[]
{
    $"BV-{cafeId:N}".Substring(0, 10) + $"-{inventoryId:N}".Substring(0, 10) + "-001",
    $"BV-{cafeId:N}".Substring(0, 10) + $"-{inventoryId:N}".Substring(0, 10) + "-002",
};

await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

Console.WriteLine("=== Step 1: Check barcode uniqueness ===");
foreach (var bc in barcodes)
{
    await using var checkCmd = new NpgsqlCommand(
        "SELECT \"Id\", \"IsActive\" FROM \"CafeInventoryBoxes\" WHERE \"Barcode\" = @bc", conn);
    checkCmd.Parameters.AddWithValue("bc", bc);
    await using var reader = await checkCmd.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        Console.WriteLine($"  ⚠️ Barcode '{bc}' đã tồn tại (Id={reader["Id"]}, IsActive={reader["IsActive"]})");
    }
    else
    {
        Console.WriteLine($"  ✅ Barcode '{bc}' available");
    }
    await reader.CloseAsync();
}

Console.WriteLine();
Console.WriteLine("=== Step 2: Check existing boxes for this inventory ===");
await using (var cmd = new NpgsqlCommand(
    "SELECT COUNT(*) FROM \"CafeInventoryBoxes\" WHERE \"CafeGameInventoryId\" = @invId AND \"IsActive\" = true", conn))
{
    cmd.Parameters.AddWithValue("invId", inventoryId);
    var existingCount = (long)(await cmd.ExecuteScalarAsync())!;
    Console.WriteLine($"  Active boxes hiện tại: {existingCount}");

    if (existingCount > 0)
    {
        Console.WriteLine($"  ⚠️ Inventory đã có {existingCount} box active. Cần check kỹ trước khi INSERT thêm.");
        Console.WriteLine($"  BoxQuantity của inventory = 2. Nếu existingCount = 2, sẽ KHÔNG INSERT thêm.");
        if (existingCount >= 2)
        {
            Console.WriteLine();
            Console.WriteLine("❌ DỪNG: Đã đủ box rồi, không INSERT.");
            return;
        }
    }
}

Console.WriteLine();
Console.WriteLine("=== Step 3: INSERT boxes (transaction) ===");

await using var tx = await conn.BeginTransactionAsync();
try
{
    foreach (var bc in barcodes)
    {
        var newBoxId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO ""CafeInventoryBoxes""
                (""Id"", ""CafeGameInventoryId"", ""Barcode"", ""Status"", ""CreatedAt"", ""UpdatedAt"", ""IsActive"")
            VALUES
                (@id, @invId, @bc, 'Available', @now, NULL, true)
            RETURNING ""Id"", ""Barcode"", ""Status"", ""IsActive"", ""CreatedAt""", conn, tx);

        cmd.Parameters.AddWithValue("id", newBoxId);
        cmd.Parameters.AddWithValue("invId", inventoryId);
        cmd.Parameters.AddWithValue("bc", bc);
        cmd.Parameters.AddWithValue("now", now);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            Console.WriteLine($"  ✅ Box #{reader["Barcode"]}:");
            Console.WriteLine($"     Id        = {reader["Id"]}");
            Console.WriteLine($"     Barcode   = {reader["Barcode"]}");
            Console.WriteLine($"     Status    = {reader["Status"]}");
            Console.WriteLine($"     IsActive  = {reader["IsActive"]}");
            Console.WriteLine($"     CreatedAt = {reader["CreatedAt"]}");
        }
        await reader.CloseAsync();
    }

    await tx.CommitAsync();
    Console.WriteLine();
    Console.WriteLine("✅ Transaction committed.");
}
catch (Exception ex)
{
    await tx.RollbackAsync();
    Console.WriteLine($"❌ FAILED — rolled back. Error: {ex.Message}");
    throw;
}

Console.WriteLine();
Console.WriteLine("=== Step 4: Final verify ===");
await using (var cmd = new NpgsqlCommand(@"
    SELECT ""Id"", ""Barcode"", ""Status"", ""IsActive"", ""CreatedAt""
    FROM ""CafeInventoryBoxes""
    WHERE ""CafeGameInventoryId"" = @invId
    ORDER BY ""Barcode""", conn))
{
    cmd.Parameters.AddWithValue("invId", inventoryId);
    await using var reader = await cmd.ExecuteReaderAsync();
    int rows = 0;
    while (await reader.ReadAsync())
    {
        rows++;
        Console.WriteLine($"  Box #{rows}: Id={reader["Id"]}, Barcode={reader["Barcode"]}, Status={reader["Status"]}, IsActive={reader["IsActive"]}");
    }
    Console.WriteLine($"  Tổng cộng: {rows} boxes cho inventory này.");
}