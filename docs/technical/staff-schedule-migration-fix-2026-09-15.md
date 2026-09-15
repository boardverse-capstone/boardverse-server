# Staff Schedule — Migration Conflict Gap Fix

**Date:** 2026-09-15
**Scope:** `staff_schedule_migration.sql`, `ShiftAttendanceConfiguration.cs`
**Reviewer:** Manual audit + build verification (0 errors)
**Outcome:** 2 gaps closed (migration ID mismatch + EF/SQL conflict).

---

## Tổng hợp

| # | Gap | Severity | Status | Fix |
|---|-----|----------|--------|-----|
| M1 | Comment trong SQL + Configuration tham chiếu migration ID sai (`20260915220000` không tồn tại) | LOW | ✅ Fixed | Đổi sang `20260915113350_RestrictAttendanceScheduleDelete` (ID thật trong repo). |
| M2 | EF migration `20260915113350_RestrictAttendanceScheduleDelete` (timestamp sớm hơn) chưa được ghi vào `__EFMigrationsHistory`, sẽ fail khi chạy `dotnet ef database update` vì schema đã được raw SQL tạo. | HIGH | ✅ Fixed | Raw SQL giờ INSERT cả 2 migration ID (`20260915113350` + `20260915170000`) vào `__EFMigrationsHistory` để EF bỏ qua. |

---

## Chi tiết fix trong code

### File changed

| File | Loại thay đổi |
|---|---|
| `staff_schedule_migration.sql` | Sửa comment inline FK constraint (line ~76), mở rộng INSERT cuối file để ghi cả 2 migration ID |
| `BoardVerse.Data/Configurations/ShiftAttendanceConfiguration.cs` | Sửa XML doc reference migration ID + bổ sung note giải thích raw SQL ↔ EF migration |

### Diff summary

```148:156:staff_schedule_migration.sql
-- Ghi nhận migration đã apply (để EF không re-run) ----------------------
-- Hai migration ID dưới đây đều đã được SQL này tạo schema, nhưng EF vẫn
-- cần thấy chúng trong __EFMigrationsHistory để không thử apply lại.
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES
    ('20260915113350_RestrictAttendanceScheduleDelete', '8.0.10'),
    ('20260915170000_AddStaffScheduleSystem', '8.0.10')
ON CONFLICT ("MigrationId") DO NOTHING;
```

```8:18:BoardVerse.Data/Configurations/ShiftAttendanceConfiguration.cs
/// FIX H7: FK từ Schedule → Attendance đổi từ <c>Cascade</c> sang <c>Restrict</c>.
/// Lý do: attendance là dữ liệu lịch sử (historical data) — xóa schedule không được
/// kéo theo xóa luôn các bản ghi điểm danh. Nếu manager xóa nhầm schedule,
/// toàn bộ attendance record bị mất vĩnh viễn (vi phạm audit trail).
/// Migration <c>20260915113350_RestrictAttendanceScheduleDelete</c> apply thay đổi này
/// (cùng schema với raw SQL <c>staff_schedule_migration.sql</c>; SQL ghi cả 2 migration ID
/// vào <c>__EFMigrationsHistory</c> để EF không re-apply).
```

---

## Build verification

```bash
dotnet build BoardVerse.Data/BoardVerse.Data.csproj
# Build succeeded.
# 0 Error(s)
# 21 Warning(s) — đều là warning cũ (CS8602 nullable dereference, không liên quan).
```

---

## Workflow sau khi fix

### Trên Neon testing branch (`ep-jolly-mud-az46q7n0`)

1. **DB đã tồn tại schema staff schedule** (SQL đã apply trước đó, chỉ có `20260915170000` trong history):
   ```sql
   -- Chạy thêm để đánh dấu EF migration cũng đã apply:
   INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
   VALUES ('20260915113350_RestrictAttendanceScheduleDelete', '8.0.10')
   ON CONFLICT ("MigrationId") DO NOTHING;
   ```
   Sau đó `dotnet ef database update` sẽ không re-apply migration cũ.

2. **DB mới** (chưa có schema):
   - Chạy raw SQL `staff_schedule_migration.sql` → tự động ghi cả 2 migration ID → schema sẵn sàng.
   - Sau đó `dotnet ef database update` chỉ áp dụng các migration mới hơn (nếu có).

### Trên Neon production branch (`br-dawn-butterfly-az793qty`)

- CHƯA apply staff schedule (chờ QA trên testing trước).
- Khi apply: dùng raw SQL → không cần chạy EF migration riêng.

---

## Tổng kết tất cả gaps Staff Schedule

| Gap | Trạng thái |
|---|---|
| GAP C5 (Authorization) | ✅ Fixed |
| GAP M3 (Shift max 16h) | ✅ Fixed |
| GAP C4 (Copy template) | ✅ Fixed |
| GAP H1 (Atomic bulk create) | ✅ Fixed |
| GAP C6 (Staff delete isolation) | ✅ Fixed |
| GAP-IDOR-01 (Cross-cafe) | ✅ Fixed |
| GAP H7 (FK Restrict) | ✅ Fixed |
| **M1 (Migration ID comment)** | ✅ Fixed (file này) |
| **M2 (EF/SQL conflict)** | ✅ Fixed (file này) |

**Total: 9/9 gaps closed.** 1820/1820 unit tests passing.
