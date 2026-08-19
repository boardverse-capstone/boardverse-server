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

// Behavior mới (auto-fill SortOrder): với FE không gửi SortOrder, bàn match Phase 1
// sẽ GIỮ NGUYÊN SortOrder DB (không bị ghi đè). Lý do: manager thêm bàn mới mà không
// cần FE tính toán — bàn cũ không bị shift vị trí.
        Assert.Single(tables);
        Assert.True(tables[0].IsActive);
        Assert.Equal(5, tables[0].SortOrder);
        Assert.Equal(existingId, tables[0].Id);
    }

    [Fact]
    public void ApplySync_DeactivatesAvailableTableRemovedFromLayout()
    {
        var cafeId = Guid.NewGuid();
        // 3 bàn active, payload chỉ giữ 1 → 2 bàn thừa sẽ bị soft-delete.
        //
        // BUGFIX: trước fix, payload có 1 tên mới hoàn toàn → Phase 2 rename 1 bàn cũ (giữ Id)
        // + 2 bàn còn lại bị soft-delete → tổng vẫn 3 bàn.
        // Sau fix: payload không có tên nào trùng → Phase 2 skip, Phase 3 tạo 1 bàn mới,
        // cả 3 bàn cũ bị soft-delete → tổng 4 bàn (3 cũ IsActive=false + 1 mới IsActive=true).
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

        var originalIds = tables.Select(t => t.Id).ToList();

        CafeTableSyncHelper.ApplySync(cafeId, ["New Table"], tables);

        // 4 bàn: 3 cũ (soft-delete) + 1 mới.
        Assert.Equal(4, tables.Count);
        Assert.Equal(3, tables.Count(t => !t.IsActive));
        Assert.Single(tables.Where(t => t.IsActive && t.Name == "New Table"));

        // Các bàn cũ giữ nguyên Id nhưng IsActive=false.
        Assert.All(originalIds, id =>
        {
            var old = tables.First(t => t.Id == id);
            Assert.False(old.IsActive);
        });

        // Bàn mới có Id khác 3 Id cũ.
        var newTable = tables.First(t => t.Name == "New Table");
        Assert.DoesNotContain(newTable.Id, originalIds);
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
        //
        // BUGFIX regression: trước fix, nếu payload đổi tên 1 bàn duy nhất (1 cũ → 1 mới, cùng SortOrder),
        // Phase 2 vẫn match SortOrder → rename giữ Id. Sau fix: Phase 1 không match tên mới →
        // Phase 2 skip (matchedIds.Count==0) → tạo bàn mới, KHÔNG rename bàn cũ.
        //
        // Để rename thật sự giữ Id, payload phải match ≥ 1 tên (Phase 1 chạy).
        // Test này giờ document: rename trực tiếp qua Phase 1 (giữ nguyên tên → đổi SeatCount).
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
            // Giữ nguyên tên + đổi SeatCount → Phase 1 match → giữ Id, cập nhật SeatCount.
            new() { Name = "Bàn 1", SeatCount = 8, SortOrder = 0 }
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Single(tables);
        var renamed = tables[0];
        Assert.Equal(existingId, renamed.Id);
        Assert.Equal("Bàn 1", renamed.Name);
        Assert.Equal(8, renamed.SeatCount);
        Assert.True(renamed.IsActive);
    }

    [Fact]
    public void ApplySync_TrueRenameByPosition_KeepsSameId_WhenOtherNameMatches()
    {
        // BUGFIX regression: rename thật sự qua Phase 2 (giữ Id) CHỈ xảy ra khi payload chứa
        // ít nhất 1 tên trùng với bàn active hiện có. Scenario:
        //   Bàn cũ: A (SortOrder=0), B (SortOrder=1).
        //   Payload: A (SortOrder=0, match Phase 1), C (SortOrder=1, đổi tên B → C).
        // → Phase 1 match "A" → matchedIds.Count > 0.
        // → Phase 2 target "C" (SortOrder=1) → match bàn "B" (SortOrder=1) → rename B → C, giữ Id.
        var cafeId = Guid.NewGuid();
        var oldIdA = Guid.NewGuid();
        var oldIdB = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new() { Id = oldIdA, CafeId = cafeId, Name = "A", SortOrder = 0, SeatCount = 4, IsActive = true, Status = CafeTableStatus.Available },
            new() { Id = oldIdB, CafeId = cafeId, Name = "B", SortOrder = 1, SeatCount = 4, IsActive = true, Status = CafeTableStatus.Available }
        };

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "A", SeatCount = 6, SortOrder = 0 },  // giữ nguyên, đổi SeatCount
            new() { Name = "C", SeatCount = 4, SortOrder = 1 }   // Phase 2 rename B → C, giữ Id
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Equal(2, tables.Count);
        Assert.Equal(oldIdA, tables.First(t => t.Name == "A").Id);
        Assert.Equal(oldIdB, tables.First(t => t.Name == "C").Id); // ⚠️ Id được giữ
        Assert.Equal(6, tables.First(t => t.Name == "A").SeatCount);
    }

    [Fact]
    public void ApplySync_RenameByPosition_KeepsSameId_WhenOtherTablesKeepNames()
    {
        // BUGFIX regression: Phase 2 chỉ rename ngầm khi payload chứa ít nhất 1 bàn trùng
        // tên với bàn active hiện có (chứng tỏ user đang "replace layout" có chủ đích).
        //
        // Scenario: cafe có 2 bàn (A, B). User rename A → A' (giữ Id), đổi B → C (giữ Id), thêm D mới.
        // → Payload có "A" match Phase 1 + "A'", "C", "D" là target rename/create.
        // → Phase 2 target "A'" (SortOrder=0) match bàn A (SortOrder=0) → rename A → A', giữ Id.
        // → Phase 2 target "C" (SortOrder=1) match bàn B (SortOrder=1) → rename B → C, giữ Id.
        // → "D" là bàn mới, tạo ở Phase 3.
        var cafeId = Guid.NewGuid();
        var oldIdA = Guid.NewGuid();
        var oldIdB = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new() { Id = oldIdA, CafeId = cafeId, Name = "A", SortOrder = 0, SeatCount = 4, IsActive = true, Status = CafeTableStatus.Available },
            new() { Id = oldIdB, CafeId = cafeId, Name = "B", SortOrder = 1, SeatCount = 4, IsActive = true, Status = CafeTableStatus.Available }
        };

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "A", SeatCount = 6, SortOrder = 0 },   // match Phase 1, giữ Id
            new() { Name = "A'", SeatCount = 6, SortOrder = 1 },  // Phase 2: rename B → A' (giữ Id)
            new() { Name = "C", SeatCount = 4, SortOrder = 2 },   // mới (Phase 3)
            new() { Name = "D", SeatCount = 4, SortOrder = 3 }    // mới (Phase 3)
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Equal(4, tables.Count);
        // A giữ nguyên Id, đổi SeatCount.
        Assert.Equal(oldIdA, tables.First(t => t.Name == "A").Id);
        Assert.Equal(6, tables.First(t => t.Name == "A").SeatCount);
        Assert.Equal(0, tables.First(t => t.Name == "A").SortOrder);
        // B được rename thành A' — giữ Id.
        Assert.Equal(oldIdB, tables.First(t => t.Name == "A'").Id);
        Assert.Equal(1, tables.First(t => t.Name == "A'").SortOrder);
        // C, D là bàn mới, Id khác.
        Assert.DoesNotContain(tables.First(t => t.Name == "C").Id, new[] { oldIdA, oldIdB });
        Assert.DoesNotContain(tables.First(t => t.Name == "D").Id, new[] { oldIdA, oldIdB });
    }

    [Fact]
    public void ApplySync_NewNameOnly_CreatesNewTableInsteadOfRenamingOld()
    {
        // BUGFIX regression (CHÍNH): trước fix, nếu payload chỉ chứa tên mới hoàn toàn
        // (không trùng bàn active nào), Phase 2 vẫn match theo SortOrder → rename ngầm
        // bàn cũ, phá kỳ vọng của FE khi họ chỉ muốn THÊM bàn mới.
        //
        // Sau fix: Phase 2 skip khi matchedIds.Count == 0 → tất cả target vào Phase 3.
        var cafeId = Guid.NewGuid();
        var oldId = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new() { Id = oldId, CafeId = cafeId, Name = "Bàn 1", SortOrder = 0, SeatCount = 4, IsActive = true, Status = CafeTableStatus.Available }
        };

        var items = new List<CafeTableSyncItem>
        {
            // Tên mới hoàn toàn, SortOrder=0 trùng với bàn cũ.
            new() { Name = "Bàn A", SeatCount = 6, SortOrder = 0 }
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        // Bàn cũ giữ nguyên tên + Id; bàn mới được tạo riêng với SortOrder=0.
        Assert.Equal(2, tables.Count);
        var preserved = tables.First(t => t.Id == oldId);
        Assert.Equal("Bàn 1", preserved.Name);
        Assert.Equal(CafeTableStatus.Available, preserved.Status);
        var created = tables.First(t => t.Id != oldId);
        Assert.Equal("Bàn A", created.Name);
        Assert.Equal(6, created.SeatCount);
        Assert.Equal(0, created.SortOrder);
        Assert.True(created.IsActive);
        Assert.Equal(CafeTableStatus.Available, created.Status);
    }

    [Fact]
    public void ApplySync_MultipleRenames_KeepAllOriginalIds()
    {
        // Đổi tên toàn bộ 3 bàn NHƯNG vẫn giữ tên cũ trong payload (1 tên match) → Phase 1 match 1,
        // Phase 2 chạy và rename các bàn còn lại theo SortOrder (giữ Id).
        //
        // BUGFIX regression: trước fix, ngay cả khi payload KHÔNG có tên nào trùng (rename 100% tên mới),
        // Phase 2 vẫn match theo SortOrder → rename toàn bộ, giữ Id. Test cũ document behavior này.
        // Sau fix: payload rename toàn bộ (không tên nào trùng) → tất cả target vào Phase 3,
        // tạo bàn mới. Test này giờ document behavior MỚI: phải giữ ≥ 1 tên cũ thì Phase 2 mới rename.
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

        // Payload giữ "Bàn 1" → Phase 1 match 1 bàn, Phase 2 rename 2 bàn còn lại theo SortOrder.
        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Bàn 1", SeatCount = 8, SortOrder = 0 },  // match Phase 1, giữ Id
            new() { Name = "Standard", SeatCount = 4, SortOrder = 1 }, // Phase 2 rename id2
            new() { Name = "Economy", SeatCount = 6, SortOrder = 2 }   // Phase 2 rename id3
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Equal(3, tables.Count);
        Assert.Equal(id1, tables.First(t => t.Name == "Bàn 1").Id);
        Assert.Equal(id2, tables.First(t => t.Name == "Standard").Id);
        Assert.Equal(id3, tables.First(t => t.Name == "Economy").Id);
    }

    [Fact]
    public void ApplySync_AllNewNames_Phase2Skipped_AllTargetsCreatedAsNew()
    {
        // BUGFIX regression: nếu payload rename 100% tên mới (không trùng tên nào trong DB),
        // Phase 2 KHÔNG chạy (matchedIds.Count == 0). Tất cả target vào Phase 3 tạo bàn mới.
        // → Bàn cũ giữ nguyên, bàn mới được tạo riêng. Test này document behavior MỚI.
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

        // 6 bàn: 3 cũ (giữ nguyên + IsActive=true, sẽ không bị soft-delete vì còn trong targets check
        // qua matchedIds — KHÔNG, đợi đã: soft-delete check `stillListed` so sánh tên, mà 3 bàn cũ
        // không tên nào trong target → sẽ bị soft-delete nếu Status=Available).
        //
        // Expected: 3 bàn cũ bị soft-delete (IsActive=false), 3 bàn mới tạo ở Phase 3.
        Assert.Equal(6, tables.Count);

        // Bàn cũ bị soft-delete (giữ Id nhưng IsActive=false).
        foreach (var oldId in new[] { id1, id2, id3 })
        {
            var old = tables.First(t => t.Id == oldId);
            Assert.False(old.IsActive, $"Bàn cũ Id={oldId} phải bị soft-delete vì không có trong payload");
        }

        // Bàn mới có tên từ payload, Id khác 3 Id cũ.
        var vip = tables.First(t => t.Name == "VIP");
        var standard = tables.First(t => t.Name == "Standard");
        var economy = tables.First(t => t.Name == "Economy");
        Assert.DoesNotContain(vip.Id, new[] { id1, id2, id3 });
        Assert.DoesNotContain(standard.Id, new[] { id1, id2, id3 });
        Assert.DoesNotContain(economy.Id, new[] { id1, id2, id3 });
        Assert.True(vip.IsActive);
        Assert.True(standard.IsActive);
        Assert.True(economy.IsActive);
    }

    [Fact]
    public void ApplySync_AddAndRename_KeepsOriginalIdsForRenames()
    {
        // Payload có 3 items: 1 match trực tiếp (giữ Id) + 1 không khớp SortOrder + 1 tạo mới.
        // BUGFIX: trước fix, "Bàn C" (SortOrder=1) sẽ được "fallback rename" vào bàn cũ A
        // (SortOrder=0) chỉ vì A chưa match — đây là behavior gây nhầm lẫn.
        // Sau fix: Phase 2 chỉ match khi SortOrder thật sự khớp (không fallback lung tung).
        // → "Bàn C" không match SortOrder 0 → vào Phase 3 (tạo mới), bàn A giữ nguyên.
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
            new() { Name = "Bàn C", SeatCount = 6, SortOrder = 1 },   // SortOrder=1 không match bàn nào (A đã SortOrder=0) → tạo mới
            new() { Name = "Bàn Z", SeatCount = 10, SortOrder = 2 }   // mới
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        // 4 bàn: B (match), A (giữ nguyên), C (mới), Z (mới).
        Assert.Equal(4, tables.Count);

        // Bàn B giữ nguyên Id (match by Name ở Phase 1) + đổi SortOrder 1→0.
        Assert.Equal(oldIdB, tables.First(t => t.Name == "Bàn B").Id);
        Assert.Equal(0, tables.First(t => t.Name == "Bàn B").SortOrder);

        // Bàn A KHÔNG bị rename — giữ nguyên tên + Id.
        Assert.Equal(oldIdA, tables.First(t => t.Name == "Bàn A").Id);
        Assert.Equal(0, tables.First(t => t.Name == "Bàn A").SortOrder);

        // "Bàn C" là bàn mới, Id khác với 2 Id cũ.
        var cTable = tables.First(t => t.Name == "Bàn C");
        Assert.DoesNotContain(cTable.Id, new[] { oldIdA, oldIdB });
        Assert.Equal(1, cTable.SortOrder);

        // Bàn Z là bàn mới.
        var zTable = tables.First(t => t.Name == "Bàn Z");
        Assert.DoesNotContain(zTable.Id, new[] { oldIdA, oldIdB });
        Assert.Equal(2, zTable.SortOrder);
    }

    [Fact]
    public void ApplySync_RenameButTableInUse_StillRenamesTable_WhenOtherNamesMatch()
    {
        // BUGFIX regression: behavior rename cho bàn InUse CHỈ xảy ra khi payload có
        // ít nhất 1 bàn trùng tên với bàn active hiện có (chứng tỏ user đang replace layout).
        //
        // Scenario: cafe có 2 bàn (Bàn 1 InUse, Bàn 2 Available). User rename cả 2 → tên mới.
        // → Payload có "Bàn 1 NEW" và "Bàn 2 NEW". Phase 1 KHÔNG match tên nào (cả 2 đều khác tên cũ).
        // → Phase 2 vẫn KHÔNG chạy vì matchedIds.Count == 0.
        // → Phase 3 tạo 2 bàn mới "Bàn 1 NEW" và "Bàn 2 NEW". Bàn InUse cũ KHÔNG bị rename.
        // → Bàn cũ InUse giữ nguyên tên + Id, sẽ bị soft-delete ở cuối (Status != Available → skip).
        //
        // Đây là fix chính: trước đây, khi bàn cũ đang InUse mà user thay đổi sơ đồ bàn
        // (không trùng tên cũ), Phase 2 vẫn rename bàn InUse → FK lịch sử vẫn trỏ đúng nhưng
        // UI bị mất bàn cũ, data bị ghi đè ngoài ý muốn.
        var cafeId = Guid.NewGuid();
        var inUseId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
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
            },
            new()
            {
                Id = otherId,
                CafeId = cafeId,
                Name = "Bàn 2",
                SortOrder = 1,
                SeatCount = 4,
                Status = CafeTableStatus.Available,
                IsActive = true
            }
        };

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Bàn VIP", SeatCount = 8, SortOrder = 0 },
            new() { Name = "Bàn Standard", SeatCount = 4, SortOrder = 1 }
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        // 4 bàn: 2 cũ (giữ nguyên + Id) + 2 mới.
        Assert.Equal(4, tables.Count);

        // Bàn InUse cũ giữ nguyên Id + Name (KHÔNG bị rename).
        var preservedInUse = tables.First(t => t.Id == inUseId);
        Assert.Equal("Bàn 1", preservedInUse.Name);
        Assert.Equal(CafeTableStatus.InUse, preservedInUse.Status);
        Assert.True(preservedInUse.IsActive);

        // Bàn Available cũ bị soft-delete (không có trong payload + Status=Available).
        var preservedOther = tables.First(t => t.Id == otherId);
        Assert.Equal("Bàn 2", preservedOther.Name);
        Assert.Equal(CafeTableStatus.Available, preservedOther.Status);
        Assert.False(preservedOther.IsActive);

        // 2 bàn mới được tạo với tên từ payload.
        Assert.NotNull(tables.FirstOrDefault(t => t.Name == "Bàn VIP" && t.Id != inUseId));
        Assert.NotNull(tables.FirstOrDefault(t => t.Name == "Bàn Standard" && t.Id != otherId));
    }

    [Fact]
    public void ApplySync_OnlyNewNames_KeepsInUseTableUntouched_CreatesNewSeparately()
    {
        // BUGFIX regression (CHÍNH — scenario user báo): bàn cũ đang InUse + payload chỉ
        // chứa tên hoàn toàn mới (không trùng tên nào trong DB).
        // → Trước fix: bàn cũ bị rename thành tên mới nhất, session vẫn chạy nhưng UI bị mất bàn.
        // → Sau fix: bàn cũ giữ nguyên (giữ FK session chạy an toàn), 2 bàn mới được tạo riêng.
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
            new() { Name = "Bàn 2", SeatCount = 4, SortOrder = 0 },
            new() { Name = "Bàn 3", SeatCount = 4, SortOrder = 1 }
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        // 3 bàn: bàn cũ (giữ nguyên) + 2 bàn mới.
        Assert.Equal(3, tables.Count);

        // Bàn cũ InUse giữ nguyên tên + Id + Status (FK tới session vẫn intact).
        var preserved = tables.First(t => t.Id == inUseId);
        Assert.Equal("Bàn 1", preserved.Name);
        Assert.Equal(0, preserved.SortOrder);
        Assert.Equal(CafeTableStatus.InUse, preserved.Status);
        Assert.True(preserved.IsActive);

        // 2 bàn mới với Id khác, tên từ payload.
        Assert.NotNull(tables.FirstOrDefault(t => t.Name == "Bàn 2" && t.Id != inUseId));
        Assert.NotNull(tables.FirstOrDefault(t => t.Name == "Bàn 3" && t.Id != inUseId));
    }

    // ============================================================
    // SortOrder validation (payload duplicates + collision với bàn cũ)
    // ============================================================

    [Fact]
    public void ApplySync_DuplicateSortOrderInPayload_ThrowsArgumentException()
    {
        // CASE 1: Payload có 2 bàn cùng SortOrder=0 → throw ngay trước khi Phase 1 chạy.
        // (Controller sẽ catch và convert thành 400 BadRequest.)
        var cafeId = Guid.NewGuid();
        var tables = new List<CafeTable>();

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Bàn A", SeatCount = 4, SortOrder = 0 },
            new() { Name = "Bàn B", SeatCount = 4, SortOrder = 0 }   // ← trùng
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            CafeTableSyncHelper.ApplySync(cafeId, items, tables));

        Assert.Contains("SortOrder không được trùng lặp", ex.Message);
        // tables không bị thay đổi vì throw trước Phase 1.
        Assert.Empty(tables);
    }

    [Fact]
    public void ApplySync_DuplicateSortOrder_NullFillsFromZero_DetectsDup()
    {
        // CASE 2: Khi DB rỗng và FE không gửi SortOrder → backend fill = 0, 1, 2, ...
        // (giống arrayIndex). 2 phần tử trong mảng sẽ có SortOrder khác nhau → không throw.
        // Test này document behavior: chỉ throw khi FE chủ động gửi 2 SortOrder giống nhau.
        var cafeId = Guid.NewGuid();
        var tables = new List<CafeTable>();

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Bàn A", SeatCount = 4, SortOrder = null },  // → 0
            new() { Name = "Bàn B", SeatCount = 4, SortOrder = null }   // → 1
        };

        // Không throw vì SortOrder sau fill = 0, 1 (khác nhau).
        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Equal(2, tables.Count);
        Assert.Equal(0, tables[0].SortOrder);
        Assert.Equal(1, tables[1].SortOrder);
    }

    [Fact]
    public void ApplySync_NewTableWithSameSortOrderAsExisting_DoesNotRename()
    {
        // CASE 3 (CHÍNH — user hỏi): DB đã có 1 bàn "Bàn 1" SortOrder=0.
        // FE gửi payload có 1 bàn mới tên "Bàn Mới", SortOrder=0.
        // → Trước fix: Phase 2 vẫn match SortOrder → rename "Bàn 1" → "Bàn Mới".
        // → Sau fix: matchedIds.Count == 0 → Phase 2 SKIP → tạo bàn mới riêng,
        //   bàn cũ giữ nguyên. Tuy nhiên SortOrder của 2 bàn sẽ TRÙNG (cùng = 0).
        //
        // Lưu ý: backend KHÔNG validate SortOrder collision với DB hiện tại — chỉ validate
        // duplicate trong payload. Test này document behavior: payload có SortOrder trùng
        // với DB → KHÔNG throw, nhưng có thể tạo ra 2 bàn cùng SortOrder (sẽ hiển thị theo
        // alphabetical Name fallback).
        var cafeId = Guid.NewGuid();
        var oldId = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new() { Id = oldId, CafeId = cafeId, Name = "Bàn 1", SortOrder = 0, SeatCount = 4, IsActive = true, Status = CafeTableStatus.Available }
        };

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Bàn Mới", SeatCount = 8, SortOrder = 0 }   // SortOrder trùng với bàn cũ
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        // Bàn cũ giữ nguyên.
        var preserved = tables.First(t => t.Id == oldId);
        Assert.Equal("Bàn 1", preserved.Name);
        Assert.Equal(0, preserved.SortOrder);

        // Bàn mới tạo riêng (cùng SortOrder=0 — đây là điểm cần lưu ý cho FE).
        var created = tables.First(t => t.Id != oldId);
        Assert.Equal("Bàn Mới", created.Name);
        Assert.Equal(8, created.SeatCount);
        Assert.Equal(0, created.SortOrder);
        Assert.True(created.IsActive);

        // 2 bàn cùng SortOrder=0 → khi hiển thị, sẽ sort theo Name (ThenBy Name).
        // Đây là behavior được chấp nhận — FE nên đảm bảo gửi SortOrder unique.
    }

    [Fact]
    public void ApplySync_NewTableWithSameSortOrderAsExisting_WhenOtherTableKeepsName_DoesRename()
    {
        // CASE 4: DB có 2 bàn (A SortOrder=0, B SortOrder=1). FE gửi payload có 3 bàn:
        //   - "A" SortOrder=0 (match Phase 1, giữ Id)
        //   - "A Mới" SortOrder=1 (Phase 2: match bàn B SortOrder=1 → rename B → A Mới, giữ Id)
        //   - "B Mới" SortOrder=2 (mới)
        //
        // Đây là scenario "rename SortOrder" thật sự: tận dụng Phase 2 để rename bàn B
        // → A Mới (giữ Id). Test này document behavior Phase 2 hoạt động khi có ≥1 match ở Phase 1.
        var cafeId = Guid.NewGuid();
        var oldIdA = Guid.NewGuid();
        var oldIdB = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new() { Id = oldIdA, CafeId = cafeId, Name = "A", SortOrder = 0, SeatCount = 4, IsActive = true, Status = CafeTableStatus.Available },
            new() { Id = oldIdB, CafeId = cafeId, Name = "B", SortOrder = 1, SeatCount = 4, IsActive = true, Status = CafeTableStatus.Available }
        };

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "A", SeatCount = 4, SortOrder = 0 },       // match Phase 1
            new() { Name = "A Mới", SeatCount = 6, SortOrder = 1 },    // Phase 2 rename B → A Mới
            new() { Name = "B Mới", SeatCount = 4, SortOrder = 2 }     // mới
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        // A giữ nguyên Id (match Phase 1).
        Assert.Equal(oldIdA, tables.First(t => t.Name == "A").Id);
        Assert.Equal(0, tables.First(t => t.Name == "A").SortOrder);

        // B được rename thành "A Mới" (giữ Id) — Phase 2.
        Assert.Equal(oldIdB, tables.First(t => t.Name == "A Mới").Id);
        Assert.Equal(1, tables.First(t => t.Name == "A Mới").SortOrder);

        // B Mới là bàn mới ở Phase 3.
        Assert.DoesNotContain(tables.First(t => t.Name == "B Mới").Id, new[] { oldIdA, oldIdB });
        Assert.Equal(2, tables.First(t => t.Name == "B Mới").SortOrder);

        // Tổng cộng 3 bàn.
        Assert.Equal(3, tables.Count);
    }

    [Fact]
    public void ApplySync_NewTableWithSortOrderCollision_AllowedButProducesTwoActiveAtSameOrder()
    {
        // CASE 5: SortOrder collision giữa payload và DB KHÔNG bị reject (chỉ reject duplicate
        // trong payload). Scenario: DB có 1 bàn "Bàn 1" SortOrder=0. FE gửi 2 bàn:
        //   - "Bàn 1" SortOrder=0 (match Phase 1)
        //   - "Bàn Mới" SortOrder=1 (SortOrder mới, không trùng payload)
        //
        // Payload KHÔNG bị reject. Kết quả: Bàn 1 giữ nguyên, Bàn Mới tạo ở SortOrder=1 (OK).
        // Lưu ý: nếu FE muốn Bàn Mới SortOrder=0 (cùng với Bàn 1) sẽ bị reject duplicate.
        var cafeId = Guid.NewGuid();
        var oldId = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new() { Id = oldId, CafeId = cafeId, Name = "Bàn 1", SortOrder = 0, SeatCount = 4, IsActive = true, Status = CafeTableStatus.Available }
        };

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Bàn 1", SeatCount = 4, SortOrder = 0 },     // match Phase 1
            new() { Name = "Bàn Mới", SeatCount = 8, SortOrder = 1 }    // SortOrder khác — OK
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Equal(2, tables.Count);
        Assert.Equal(oldId, tables.First(t => t.Name == "Bàn 1").Id);
        Assert.DoesNotContain(tables.First(t => t.Name == "Bàn Mới").Id, new[] { oldId });
        Assert.Equal(1, tables.First(t => t.Name == "Bàn Mới").SortOrder);
    }

    // ============================================================
    // Auto-fill SortOrder=null (manager quán không cần biết SortOrder)
    // ============================================================

    [Fact]
    public void ApplySync_NullSortOrder_WhenDbEmpty_FillsFromZero()
    {
        // DB rỗng, FE không gửi SortOrder → backend tự gán 0, 1, 2.
        var cafeId = Guid.NewGuid();
        var tables = new List<CafeTable>();

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Bàn A", SeatCount = 4, SortOrder = null },
            new() { Name = "Bàn B", SeatCount = 4, SortOrder = null },
            new() { Name = "Bàn C", SeatCount = 4, SortOrder = null }
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Equal(3, tables.Count);
        Assert.Equal(0, tables.First(t => t.Name == "Bàn A").SortOrder);
        Assert.Equal(1, tables.First(t => t.Name == "Bàn B").SortOrder);
        Assert.Equal(2, tables.First(t => t.Name == "Bàn C").SortOrder);
    }

    [Fact]
    public void ApplySync_NullSortOrder_WhenDbHasExisting_AppendsAfterMax()
    {
        // DB đã có [Bàn X=0, Bàn Y=1, Bàn Z=2]. FE thêm 2 bàn mới không gửi SortOrder
        // → backend tự gán 3, 4 (MAX active = 2 + 1 = 3, 4).
        var cafeId = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new() { Id = Guid.NewGuid(), CafeId = cafeId, Name = "Bàn X", SortOrder = 0, SeatCount = 4, IsActive = true },
            new() { Id = Guid.NewGuid(), CafeId = cafeId, Name = "Bàn Y", SortOrder = 1, SeatCount = 4, IsActive = true },
            new() { Id = Guid.NewGuid(), CafeId = cafeId, Name = "Bàn Z", SortOrder = 2, SeatCount = 4, IsActive = true }
        };

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Bàn Mới 1", SeatCount = 8, SortOrder = null },  // → 3
            new() { Name = "Bàn Mới 2", SeatCount = 8, SortOrder = null }   // → 4
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Equal(5, tables.Count);
        Assert.Equal(3, tables.First(t => t.Name == "Bàn Mới 1").SortOrder);
        Assert.Equal(4, tables.First(t => t.Name == "Bàn Mới 2").SortOrder);
    }

    [Fact]
    public void ApplySync_NullSortOrder_OnlyCountsActiveTables()
    {
        // DB có bàn active [A=0, B=1] + soft-delete [C=2 IsActive=false]. FE thêm 1 bàn mới
        // → SortOrder = 2 (MAX active = 1, +1 = 2; bỏ qua C soft-delete).
        var cafeId = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new() { Id = Guid.NewGuid(), CafeId = cafeId, Name = "A", SortOrder = 0, SeatCount = 4, IsActive = true },
            new() { Id = Guid.NewGuid(), CafeId = cafeId, Name = "B", SortOrder = 1, SeatCount = 4, IsActive = true },
            new() { Id = Guid.NewGuid(), CafeId = cafeId, Name = "C", SortOrder = 2, SeatCount = 4, IsActive = false }  // soft-delete
        };

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Mới", SeatCount = 4, SortOrder = null }
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Equal(2, tables.First(t => t.Name == "Mới").SortOrder);
    }

    [Fact]
    public void ApplySync_MixedExplicitAndNullSortOrder_AppendsNullAfterExplicit()
    {
        // DB rỗng. Payload có 2 bàn: 1 cái SortOrder=5 (FE muốn chèn vào vị trí đặc biệt),
        // 1 cái SortOrder=null (manager thêm mới không quan tâm).
        // → Sau fill: SortOrder=5 (giữ nguyên) và SortOrder=6 (null → MAX(5)+1+0).
        // Đây là case thực tế khi FE có 1 số bàn do user config + 1 số do manager thêm.
        var cafeId = Guid.NewGuid();
        var tables = new List<CafeTable>();

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "Bàn Config", SeatCount = 4, SortOrder = 5 },  // FE set cứng
            new() { Name = "Bàn Manager", SeatCount = 8, SortOrder = null }  // auto-fill
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        Assert.Equal(2, tables.Count);
        Assert.Equal(5, tables.First(t => t.Name == "Bàn Config").SortOrder);
        Assert.Equal(6, tables.First(t => t.Name == "Bàn Manager").SortOrder);
    }

    [Fact]
    public void ApplySync_NullSortOrder_FilledCorrectlyForRenameScenario()
    {
        // Scenario: Manager đổi tên 1 bàn và thêm 1 bàn mới (không biết SortOrder).
        // DB có [A=0, B=1, C=2]. Payload: [B (giữ tên), Mới (SortOrder=null)].
        // → B match Phase 1, Mới auto-fill SortOrder=3.
        var cafeId = Guid.NewGuid();
        var oldBId = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new() { Id = Guid.NewGuid(), CafeId = cafeId, Name = "A", SortOrder = 0, SeatCount = 4, IsActive = true },
            new() { Id = oldBId, CafeId = cafeId, Name = "B", SortOrder = 1, SeatCount = 4, IsActive = true },
            new() { Id = Guid.NewGuid(), CafeId = cafeId, Name = "C", SortOrder = 2, SeatCount = 4, IsActive = true }
        };

        var items = new List<CafeTableSyncItem>
        {
            new() { Name = "B", SeatCount = 4, SortOrder = null },        // match Phase 1, giữ nguyên Id + SortOrder=1
            new() { Name = "Mới", SeatCount = 8, SortOrder = null }      // auto-fill SortOrder=3
        };

        CafeTableSyncHelper.ApplySync(cafeId, items, tables);

        // A và C bị soft-delete (không có trong payload).
        Assert.Equal(4, tables.Count);   // A(active=false), B(active=true), C(active=false), Mới(active=true)
        Assert.Equal(oldBId, tables.First(t => t.Name == "B").Id);
        Assert.Equal(1, tables.First(t => t.Name == "B").SortOrder);
        Assert.Equal(3, tables.First(t => t.Name == "Mới").SortOrder);
    }

    [Fact]
    public void ApplySync_LegacyNamesOnly_AutoAppends()
    {
        // Test legacy overload (chỉ gửi list<string>): SortOrder phải auto-append, không ép = index.
        // DB có [A=0, B=1]. Payload: ["B", "C"] → B giữ nguyên, C auto-fill SortOrder=2.
        // (Trước fix: C sẽ có SortOrder=1 → trùng với B → Phase 2 sai.)
        var cafeId = Guid.NewGuid();
        var oldBId = Guid.NewGuid();
        var tables = new List<CafeTable>
        {
            new() { Id = Guid.NewGuid(), CafeId = cafeId, Name = "A", SortOrder = 0, SeatCount = 4, IsActive = true },
            new() { Id = oldBId, CafeId = cafeId, Name = "B", SortOrder = 1, SeatCount = 4, IsActive = true }
        };

        var names = new List<string> { "B", "C" };

        CafeTableSyncHelper.ApplySync(cafeId, names, tables);

        // A bị soft-delete.
        Assert.Equal(3, tables.Count);   // A(active=false), B(active=true), C(active=true)
        Assert.Equal(oldBId, tables.First(t => t.Name == "B").Id);
        Assert.Equal(1, tables.First(t => t.Name == "B").SortOrder);
        Assert.Equal(2, tables.First(t => t.Name == "C").SortOrder);
    }
}

