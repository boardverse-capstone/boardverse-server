using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class CafeTableSyncHelperTests
{
    [Fact]
    public void ApplySync_AddsNewTablesForNewNames()
    {
        var cafeId = Guid.NewGuid();
        var tables = new List<CafeTable>();

        CafeTableSyncHelper.ApplySync(cafeId, ["Table A", "Table B"], tables);

        Assert.Equal(2, tables.Count);
        Assert.Equal(0, tables[0].SortOrder);
        Assert.Equal(1, tables[1].SortOrder);
        Assert.All(tables, t => Assert.Equal(CafeTableStatus.Available, t.Status));
    }

    [Fact]
    public void ApplySync_ReactivatesInactiveTableWithSameName()
    {
        var cafeId = Guid.NewGuid();
        var existingId = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new()
            {
                Id = existingId,
                CafeId = cafeId,
                Name = "Table A",
                SortOrder = 5,
                IsActive = false
            }
        };

        CafeTableSyncHelper.ApplySync(cafeId, ["Table A"], tables);

        Assert.Single(tables);
        Assert.True(tables[0].IsActive);
        Assert.Equal(0, tables[0].SortOrder);
        Assert.Equal(existingId, tables[0].Id);
    }

    [Fact]
    public void ApplySync_DeactivatesAvailableTableRemovedFromLayout()
    {
        var cafeId = Guid.NewGuid();
        // 3 bàn active, payload chỉ giữ 1 → 2 bàn thừa sẽ bị soft-delete.
        var tables = new List<CafeTable>
        {
            new()
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                Name = "Old Table 1",
                SortOrder = 0,
                Status = CafeTableStatus.Available,
                IsActive = true
            },
            new()
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                Name = "Old Table 2",
                SortOrder = 1,
                Status = CafeTableStatus.Available,
                IsActive = true
            },
            new()
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                Name = "Old Table 3",
                SortOrder = 2,
                Status = CafeTableStatus.Available,
                IsActive = true
            }
        };

        CafeTableSyncHelper.ApplySync(cafeId, ["New Table"], tables);

        // 1 bàn được rename thành "New Table" (giữ Id), 2 bàn còn lại bị soft-delete.
        Assert.Equal(3, tables.Count);
        Assert.Equal(2, tables.Count(t => !t.IsActive));
        Assert.Single(tables.Where(t => t.IsActive && t.Name == "New Table"));
    }

    // ============================================================
    // Overload mới — IReadOnlyList<CafeTableSyncItem> (Name + SeatCount + SortOrder)
    // ============================================================

    [Fact]
    public void ApplySync_WithItems_NewTable_UsesProvidedSeatCountAndSortOrder()
    {
        var cafeId = Guid.NewGuid();
        var tables = new List<CafeTable>();

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Bàn VIP", SeatCount = 8, SortOrder = 0 },
            new() { Name = "Bàn 2", SeatCount = 6, SortOrder = 1 }
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Equal(2, tables.Count);
        Assert.Equal(8, tables[0].SeatCount);
        Assert.Equal(0, tables[0].SortOrder);
        Assert.Equal(6, tables[1].SeatCount);
        Assert.Equal(1, tables[1].SortOrder);
    }

    [Fact]
    public void ApplySync_WithItems_NullSeatCount_FallsBackToDefault4()
    {
        var cafeId = Guid.NewGuid();
        var tables = new List<CafeTable>();

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Bàn A", SeatCount = null, SortOrder = 0 }
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Single(tables);
        Assert.Equal(CafeTableSyncHelper.DefaultSeatCount, tables[0].SeatCount);
    }

    [Fact]
    public void ApplySync_WithItems_NullSortOrder_FallsBackToArrayIndex()
    {
        var cafeId = Guid.NewGuid();
        var tables = new List<CafeTable>();

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "A", SeatCount = 4, SortOrder = null },
            new() { Name = "B", SeatCount = 4, SortOrder = null }
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Equal(0, tables[0].SortOrder);
        Assert.Equal(1, tables[1].SortOrder);
    }

    [Fact]
    public void ApplySync_WithItems_ExistingTable_NoSeatCount_KeepsCurrentSeatCount()
    {
        var cafeId = Guid.NewGuid();
        var existingId = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new()
            {
                Id = existingId,
                CafeId = cafeId,
                Name = "Bàn 1",
                SeatCount = 12,
                IsActive = true
            }
        };

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Bàn 1", SeatCount = null, SortOrder = 0 }
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Single(tables);
        Assert.Equal(existingId, tables[0].Id);
        Assert.Equal(12, tables[0].SeatCount);
    }

    [Fact]
    public void ApplySync_WithItems_ExistingTable_WithSeatCount_OverwritesSeatCount()
    {
        var cafeId = Guid.NewGuid();
        var existingId = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new()
            {
                Id = existingId,
                CafeId = cafeId,
                Name = "Bàn 1",
                SeatCount = 4,
                IsActive = true
            }
        };

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Bàn 1", SeatCount = 10, SortOrder = 0 }
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Single(tables);
        Assert.Equal(10, tables[0].SeatCount);
    }

    [Fact]
    public void ApplySync_WithItems_TrimsWhitespaceInName()
    {
        var cafeId = Guid.NewGuid();
        var tables = new List<CafeTable>();

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "  Bàn A  ", SeatCount = 4, SortOrder = 0 }
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Single(tables);
        Assert.Equal("Bàn A", tables[0].Name);
    }

    // ============================================================
    // Rename scenarios (Phase 1 + Phase 2 matching)
    // ============================================================

    [Fact]
    public void ApplySync_RenameByName_KeepsSameId()
    {
        // Khi payload có tên trùng với bàn active (case-insensitive),
        // Phase 1 match → giữ nguyên Id, chỉ đổi Name + cập nhật field khác.
        var cafeId = Guid.NewGuid();
        var existingId = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new()
            {
                Id = existingId,
                CafeId = cafeId,
                Name = "Bàn 1",
                SortOrder = 0,
                SeatCount = 4,
                Status = CafeTableStatus.Available,
                IsActive = true
            }
        };

        var items = new List<CafeTableSyncItem>
        {
            // User rename "Bàn 1" → "Bàn VIP" tại cùng vị trí.
            new() { Name = "Bàn VIP", SeatCount = 8, SortOrder = 0 }
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Single(tables);
        var renamed = tables[0];
        Assert.Equal(existingId, renamed.Id); // ⚠️ Id phải giữ nguyên để không đứt FK
        Assert.Equal("Bàn VIP", renamed.Name);
        Assert.Equal(8, renamed.SeatCount);
        Assert.True(renamed.IsActive);
    }

    [Fact]
    public void ApplySync_RenameByPosition_KeepsSameId()
    {
        // Khi user rename bằng cách đổi tên nhưng giữ SortOrder,
        // Phase 1 không match (tên mới), Phase 2 match theo SortOrder → rename ngầm, giữ Id.
        var cafeId = Guid.NewGuid();
        var oldId = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new()
            {
                Id = oldId,
                CafeId = cafeId,
                Name = "Bàn 1",
                SortOrder = 0,
                SeatCount = 4,
                Status = CafeTableStatus.Available,
                IsActive = true
            }
        };

        var items = new List<CafeTableSyncItem>
        {
            // Tên mới khác hoàn toàn, SortOrder giữ nguyên 0.
            new() { Name = "Bàn A", SeatCount = 6, SortOrder = 0 }
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Single(tables);
        var renamed = tables[0];
        Assert.Equal(oldId, renamed.Id); // ⚠️ Vẫn giữ Id — quan trọng cho FK history
        Assert.Equal("Bàn A", renamed.Name);
        Assert.Equal(6, renamed.SeatCount);
        Assert.Equal(0, renamed.SortOrder);
    }

    [Fact]
    public void ApplySync_MultipleRenames_KeepAllOriginalIds()
    {
        // Đổi tên toàn bộ 3 bàn, kiểm tra tất cả Id được giữ nguyên.
        var cafeId = Guid.NewGuid();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new() { Id = id1, CafeId = cafeId, Name = "Bàn 1", SortOrder = 0, SeatCount = 4, IsActive = true, Status = CafeTableStatus.Available },
            new() { Id = id2, CafeId = cafeId, Name = "Bàn 2", SortOrder = 1, SeatCount = 4, IsActive = true, Status = CafeTableStatus.Available },
            new() { Id = id3, CafeId = cafeId, Name = "Bàn 3", SortOrder = 2, SeatCount = 4, IsActive = true, Status = CafeTableStatus.Available }
        };

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "VIP", SeatCount = 8, SortOrder = 0 },
            new() { Name = "Standard", SeatCount = 4, SortOrder = 1 },
            new() { Name = "Economy", SeatCount = 6, SortOrder = 2 }
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Equal(3, tables.Count);
        Assert.Equal(id1, tables.First(t => t.Name == "VIP").Id);
        Assert.Equal(id2, tables.First(t => t.Name == "Standard").Id);
        Assert.Equal(id3, tables.First(t => t.Name == "Economy").Id);
    }

    [Fact]
    public void ApplySync_AddAndRename_KeepsOriginalIdsForRenames()
    {
        // Payload có 3 items: 1 rename (giữ Id) + 1 fallback rename (giữ Id) + 1 tạo mới.
        var cafeId = Guid.NewGuid();
        var oldIdA = Guid.NewGuid();
        var oldIdB = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new() { Id = oldIdA, CafeId = cafeId, Name = "Bàn A", SortOrder = 0, SeatCount = 4, IsActive = true, Status = CafeTableStatus.Available },
            new() { Id = oldIdB, CafeId = cafeId, Name = "Bàn B", SortOrder = 1, SeatCount = 4, IsActive = true, Status = CafeTableStatus.Available }
        };

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Bàn B", SeatCount = 4, SortOrder = 0 },   // giữ nguyên, sortOrder đổi 1→0 (Phase 1 match theo Name)
            new() { Name = "Bàn C", SeatCount = 6, SortOrder = 1 },   // fallback rename Id=oldIdA (Phase 2)
            new() { Name = "Bàn Z", SeatCount = 10, SortOrder = 2 }   // mới
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Equal(3, tables.Count);
        // Bàn B giữ nguyên Id (match by Name ở Phase 1).
        Assert.Equal(oldIdB, tables.First(t => t.Name == "Bàn B").Id);
        Assert.Equal(0, tables.First(t => t.Name == "Bàn B").SortOrder);
        // Bàn A được fallback rename thành "Bàn C" (Phase 2) → Id được giữ.
        Assert.Equal(oldIdA, tables.First(t => t.Name == "Bàn C").Id);
        Assert.Equal(1, tables.First(t => t.Name == "Bàn C").SortOrder);
        Assert.True(tables.First(t => t.Name == "Bàn C").IsActive);
        // Bàn Z là bàn mới, Id khác với 2 Id cũ.
        Assert.DoesNotContain(tables.First(t => t.Name == "Bàn Z").Id, new[] { oldIdA, oldIdB });
    }

    [Fact]
    public void ApplySync_RenameButTableInUse_StillRenamesTable()
    {
        // Bàn active có session đang chạy (Status = InUse) nhưng user gửi tên mới.
        // → Vẫn rename (giữ Id) vì Phase 2 match theo SortOrder.
        // → Đây là case đặc biệt: user đổi tên bàn đang chơi.
        // → Kết quả: 1 bàn duy nhất với Id cũ, tên mới.
        var cafeId = Guid.NewGuid();
        var inUseId = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new()
            {
                Id = inUseId,
                CafeId = cafeId,
                Name = "Bàn 1",
                SortOrder = 0,
                SeatCount = 4,
                Status = CafeTableStatus.InUse, // đang có session
                IsActive = true
            }
        };

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Bàn VIP", SeatCount = 8, SortOrder = 0 }
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        // Bàn được rename thành "Bàn VIP" nhưng vẫn giữ Id và Status = InUse.
        Assert.Single(tables);
        var renamed = tables[0];
        Assert.Equal(inUseId, renamed.Id);
        Assert.Equal("Bàn VIP", renamed.Name);
        Assert.Equal(CafeTableStatus.InUse, renamed.Status);
        Assert.True(renamed.IsActive);
    }

    [Fact]
    public void ApplySync_AddWhenAllTablesInUse_CreatesNewTableSeparately()
    {
        // Bàn active đang có session (Status = InUse) — Phase 2 match theo SortOrder
        // nhưng ta cần verify: nếu user cố tình thêm bàn mới khi bàn cũ đang chơi,
        // logic sẽ rename bàn cũ (giữ Id). Đây là behavior đúng — không tạo bàn mới.
        var cafeId = Guid.NewGuid();
        var inUseId = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new()
            {
                Id = inUseId,
                CafeId = cafeId,
                Name = "Bàn 1",
                SortOrder = 0,
                SeatCount = 4,
                Status = CafeTableStatus.InUse,
                IsActive = true
            }
        };

        // Payload thêm 2 bàn mới (không có "Bàn 1" trong payload):
        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Bàn 2", SeatCount = 4, SortOrder = 0 },  // SortOrder=0 → rename "Bàn 1" thành "Bàn 2"
            new() { Name = "Bàn 3", SeatCount = 4, SortOrder = 1 }   // SortOrder=1 → tạo mới
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Equal(2, tables.Count);
        // Bàn cũ được rename thành "Bàn 2" (giữ Id, vẫn InUse).
        Assert.Equal(inUseId, tables.First(t => t.Name == "Bàn 2").Id);
        Assert.Equal(CafeTableStatus.InUse, tables.First(t => t.Name == "Bàn 2").Status);
        // "Bàn 3" là bàn mới.
        Assert.NotEqual(inUseId, tables.First(t => t.Name == "Bàn 3").Id);
    }
}

