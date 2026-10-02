using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data;
using BoardVerse.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="CafeInventoryRepository"/> — kiểm tra tương tác với EF Core change tracker.
///
/// Mục đích chính: bắt regression của bug "Add Inventory không tạo QR boxes" — khi
/// <c>SyncInventoryBoxesAsync</c> được gọi ngay sau <c>AddAsync</c> (trước <c>SaveChangesAsync</c>),
/// inventory đang ở trạng thái Added, không có trong DB. Query <c>FirstOrDefaultAsync</c>
/// cũ trả về null → boxes không được sinh. Bug này chỉ bị trigger trong flow
/// <c>AddToInventoryAsync</c> (manager thêm game lần đầu), không trigger trong
/// <c>UpdateInventoryAsync</c> (đã save trước đó).
/// </summary>
public class CafeInventoryRepositoryTests
{
    private static readonly Guid CafeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid GameTemplateId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static BoardVerseDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase($"CafeInventoryRepositoryTests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new BoardVerseDbContext(options);
    }

    /// <summary>
    /// Regression test cho bug Add Inventory Step 2: khi gọi AddAsync → SyncInventoryBoxesAsync
    /// liên tiếp (chưa SaveChanges), boxes vẫn phải được sinh đầy đủ.
    ///
    /// Trước fix: <c>FirstOrDefaultAsync</c> trả null → boxes.count = 0 (BUG).
    /// Sau fix: change-tracker check trả về tracked entity → boxes.count = BoxQuantity.
    /// </summary>
    [Fact]
    public async Task SyncInventoryBoxesAsync_ImmediatelyAfterAdd_CreatesAllBoxes()
    {
        using var db = CreateInMemoryDb();
        var repo = new CafeInventoryRepository(db);

        // Arrange: tạo inventory mới, AddAsync (chưa SaveChanges → state = Added)
        var inventoryId = Guid.NewGuid();
        var inventory = new CafeGameInventory
        {
            Id = inventoryId,
            CafeId = CafeId,
            GameTemplateId = GameTemplateId,
            BoxQuantity = 12,
            Status = CafeGameInventoryStatus.Available,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };
        await repo.AddAsync(inventory);

        // Act: gọi SyncInventoryBoxesAsync ngay sau AddAsync (giả lập CafeInventoryService.AddToInventoryAsync)
        await repo.SyncInventoryBoxesAsync(inventoryId);
        await repo.SaveChangesAsync();

        // Assert: 12 boxes phải được tạo và persist vào DB
        var boxCount = await db.CafeInventoryBoxes
            .CountAsync(b => b.CafeGameInventoryId == inventoryId && b.IsActive);
        Assert.Equal(12, boxCount);

        // Barcode phải unique và theo format chuẩn
        var boxes = await db.CafeInventoryBoxes
            .Where(b => b.CafeGameInventoryId == inventoryId)
            .OrderBy(b => b.Barcode)
            .ToListAsync();
        Assert.Equal(12, boxes.Count);
        Assert.Equal(12, boxes.Select(b => b.Barcode).Distinct().Count());
        Assert.All(boxes, b => Assert.StartsWith("BV-", b.Barcode));
        Assert.All(boxes, b => Assert.Equal(CafeGameInventoryStatus.Available, b.Status));
    }

    /// <summary>
    /// Verify existing flow (inventory đã có trong DB) vẫn hoạt động — tăng BoxQuantity
    /// từ 5 lên 8 sẽ tạo thêm 3 boxes mới.
    /// </summary>
    [Fact]
    public async Task SyncInventoryBoxesAsync_ExistingInventory_AddsMissingBoxes()
    {
        using var db = CreateInMemoryDb();
        var repo = new CafeInventoryRepository(db);

        // Seed: tạo inventory với 5 boxes đã persist
        var inventoryId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        for (var i = 1; i <= 5; i++)
        {
            db.CafeInventoryBoxes.Add(new CafeInventoryBox
            {
                Id = Guid.NewGuid(),
                CafeGameInventoryId = inventoryId,
                Barcode = $"BV-test-{i:D3}",
                Status = CafeGameInventoryStatus.Available,
                CreatedAt = now,
                IsActive = true
            });
        }
        db.CafeGameInventories.Add(new CafeGameInventory
        {
            Id = inventoryId,
            CafeId = CafeId,
            GameTemplateId = GameTemplateId,
            BoxQuantity = 5,
            Status = CafeGameInventoryStatus.Available,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        });
        await db.SaveChangesAsync();

        // Act: bump BoxQuantity lên 8 và sync
        var inventory = await db.CafeGameInventories.FindAsync(inventoryId);
        inventory!.BoxQuantity = 8;
        await repo.SyncInventoryBoxesAsync(inventoryId);
        await repo.SaveChangesAsync();

        // Assert: 8 boxes tổng cộng
        var boxCount = await db.CafeInventoryBoxes
            .CountAsync(b => b.CafeGameInventoryId == inventoryId && b.IsActive);
        Assert.Equal(8, boxCount);
    }

    /// <summary>
    /// Backward compat: gọi SyncInventoryBoxesAsync với inventoryId không tồn tại
    /// (không trong change tracker, không trong DB) phải return silently — không throw.
    /// </summary>
    [Fact]
    public async Task SyncInventoryBoxesAsync_InventoryNotFound_DoesNotThrow()
    {
        using var db = CreateInMemoryDb();
        var repo = new CafeInventoryRepository(db);

        await repo.SyncInventoryBoxesAsync(Guid.NewGuid());
        await repo.SaveChangesAsync();

        Assert.Empty(await db.CafeInventoryBoxes.ToListAsync());
    }
}
