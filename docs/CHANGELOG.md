# BoardVerse — Changelog

Lịch sử thay đổi đáng chú ý của codebase. Cập nhật theo từng PR / commit quan trọng.

---

## 2026-09-19 — Database Schema Sync (4 gaps, raw SQL applied)

### 2. Sync Entity/EF Configuration với Production DB (4 gaps)

- **Phạm vi:** Production Neon DB (`br-hidden-shadow-aoqtn6su`) — so sánh schema vs Entity + EF Configuration.
- **Gaps đã phát hiện:**
  - **G1 (CRITICAL)** — `Lobbies` thiếu 3 columns: `CancellationLeadTimeMinutes` (int, default 30), `FullAt` (timestamptz, nullable), `ActiveSessionId` (uuid FK). EF sẽ throw khi load `Lobby.ActiveSession` vì column không tồn tại.
  - **G2 (HIGH)** — `CafeConfigs` thiếu 2 columns: `MinDepositRatePerPerson` (bigint, default 1), `MaxDepositRatePerPerson` (bigint, default 100). BR-DEPOSIT-03 deposit rate cap không persist được.
  - **G3 (MEDIUM)** — `ActiveSessionMembers` thiếu 4 columns QR (Split Bill mobile display): `QrImageUrl`, `QrPaymentUrl`, `QrOrderId`, `QrTransferContent`. Config đã khai báo nhưng column không tồn tại trong DB.
  - **G4 (MEDIUM)** — `SeatInventories.RowVersion` type drift: config khai báo `xid` nhưng DB lưu `bigint`. `dotnet ef database update` sẽ báo out-of-sync.
- **Duplicated entity files phát hiện thêm:**
  - `BookingDeposit.cs`, `PlayerActionHistory.cs`, `CafeConfig.cs`, `CafeSettlement.cs` — trùng ở đường dẫn `/` vs `\` (cùng thư mục, chỉ khác separator). Cần dọn dẹp.
- **Fix:** Raw SQL đã cung cấp cho user tự apply trên production (2026-09-19).
- **API docs đã cập nhật:** `docs/api/cafe.md`, `docs/api/cafe-pos.md`, `docs/api/payment.md`, `docs/api/active-session.md` — bổ sung tên columns mới (`MinDepositRatePerPerson`, `MaxDepositRatePerPerson`, `QrImageUrl`, `QrPaymentUrl`, `QrOrderId`, `QrTransferContent`).
- **Chi tiết:** [technical/gaps-fix-2026-09-19.md](./technical/gaps-fix-2026-09-19.md).

---

## 2026-09-15 — Staff Work Schedule module (9/9 gaps closed, 1820/1820 tests PASS)

### 1. Module Staff Schedule — Đóng 9 gaps + ship production-ready

- **Phạm vi:** Staff Schedule (Manager quản lý ca làm việc cho staff) + Time-off + Shift Swap + Attendance + Unavailable Dates.
- **Files mới:**
  - Entities: `BoardVerse.Core/Entities/{StaffSchedule, ShiftAttendance, TimeOffRequest, ShiftSwapRequest, StaffUnavailableDate}.cs`.
  - Repositories: `BoardVerse.Data/Repositories/{StaffSchedule, ShiftAttendance, TimeOffRequest, ShiftSwapRequest, StaffUnavailableDate}Repository.cs`.
  - Configurations: `BoardVerse.Data/Configurations/{StaffSchedule, ShiftAttendance, TimeOffRequest, ShiftSwapRequest, StaffUnavailableDate}Configuration.cs`.
  - DTOs: `BoardVerse.Core/DTOs/StaffSchedule/{StaffScheduleRequestDtos, StaffScheduleResponseDtos}.cs`.
  - Services: `BoardVerse.Services/Services/{StaffSchedule, ShiftAttendance, TimeOffRequest, ShiftSwapRequest, StaffUnavailableDate}Service.cs`.
  - Interfaces: `BoardVerse.Core/IRepositories/{IStaffSchedule, IShiftAttendance, ITimeOffRequest, IShiftSwapRequest, IStaffUnavailableDate}Repository.cs`.
  - Controller: `BoardVerse.API/Controllers/StaffScheduleController.cs`.
  - Tests: `BoardVerse.Tests/Services/{StaffSchedule, ShiftAttendance, TimeOffRequest, ShiftSwapRequest, StaffUnavailableDate}ServiceTests.cs` (~135 test cases mới).
  - Enums: `BoardVerse.Core/Enum/{StaffScheduleStatus, ShiftType, TimeOffStatus, ShiftSwapStatus}.cs`.
  - SQL: `staff_schedule_migration.sql` (5 bảng mới + indexes + FK).
- **Gaps đã đóng:**
  - **C5** — Manager cafe A truy cập resource của cafe B qua StaffScheduleController (IDOR ngang). Fix: `EnsureCafeManagerAsync` guard cho mọi Manager endpoint.
  - **M3** — Không validate max duration 16h. Fix: validate `duration <= 960 phút` trong service, throw `DurationExceeds16Hours`.
  - **C4** — Copy template copy cả lịch staff không đi làm tuần nguồn. Fix: filter `ShiftAttendance` của tuần nguồn; fallback copy toàn bộ Active nếu rỗng.
  - **H1** — Bulk create không atomic. Fix: wrap trong 1 transaction Serializable; validate tất cả trước khi insert bất kỳ record nào.
  - **C6** — `DELETE staff` không xóa schedule cũ. Fix: soft-cancel tất cả `StaffSchedule.Status = Cancelled` trong transaction.
  - **IDOR-01** — Cross-cafe access qua `scheduleId` route param. Fix: re-resolve cafe qua FK và áp dụng `EnsureCafeManagerAsync`.
  - **H7** — FK `ShiftAttendances.ScheduleId` ON DELETE CASCADE. Fix: đổi sang RESTRICT qua EF migration `20260915113350_RestrictAttendanceScheduleDelete` + raw SQL.
  - **M1** — Comment trong SQL + Configuration tham chiếu migration ID sai. Fix: đổi sang ID thật.
  - **M2** — EF migration `20260915113350` chưa ghi vào `__EFMigrationsHistory`. Fix: raw SQL INSERT cả 2 migration ID với `ON CONFLICT DO NOTHING`.
- **Test:** 1820/1820 PASS (tăng từ 1685 → 1820, +135 test cases mới).
- **Build:** `BoardVerse.Data` 0 Error / 21 Warning (cũ, nullable dereference, không liên quan).
- **Docs:**
  - `docs/api/staff-schedule.md` — API reference đầy đủ cho StaffScheduleController (Manager + Staff self-service).
  - `docs/technical/gaps-fix-2026-09-15.md` — Tổng kết 7 code gaps + migration fix.
  - `docs/technical/staff-schedule-migration-fix-2026-09-15.md` — Chi tiết M1 + M2 migration conflict fix.
  - `docs/CHANGELOG.md` — Entry này.
  - `docs/TEST_REPORT.md` — Test count + 5 staff schedule test entries.
  - `docs/README.md` — Thêm `StaffScheduleController` reference.

### 2. Trạng thái deploy

- **Testing branch (`ep-jolly-mud-az46q7n0`):** Schema đã apply + `__EFMigrationsHistory` đã ghi cả 2 migration ID. `dotnet ef database update` sẽ không re-apply.
- **Production branch (`br-dawn-butterfly-az793qty`):** CHƯA apply. Khi sẵn sàng go-live: chạy `staff_schedule_migration.sql` (không cần `dotnet ef database update` riêng).

---

## 2026-09-08 — Karma rating 403 fix + Build fixes (round 3)

### 1. Karma rating 403 "không phải thành viên" cho host qua PendingCafeApproval

- **Triệu chứng:** Host tạo lobby qua flow có cafe approval (`PendingCafeApproval`) → cafe approve → host gọi `GET /api/v1/users/ratings/karma/lobbies/{lobbyId}` → 403 "Bạn không phải thành viên của phòng này nên không thể đánh giá."
- **Root cause:** Step 18 của `ReservationService.ConfirmAsync` skip insert host vào `LobbyMember` khi `lobby.Status == PendingCafeApproval`. `HandleCafeApprovalAsync` (approve branch) cũng không thêm host → host không có record `LobbyMember` → `KarmaRatingRepository.GetLobbyForRatingAsync` (filter `Members.Where(IsActive)`) trả collection rỗng → `RequireLobbyMemberContextAsync` throw 403.
- **Fix:** `HandleCafeApprovalAsync` nay tự động thêm host làm `LobbyMember` (`IsHost=true, IsActive=true, Status=Joined`) khi approve. Idempotent — không tạo duplicate khi re-approve.
- **File:** `BoardVerse.Services/Services/ReservationService.cs` (~line 1472).
- **Test:** `BoardVerse.Tests/Services/ReservationServiceCafeApprovalHostMembershipTests.cs` (2 tests).
- **Docs:** `docs/api/reservation.md`, `docs/api/user-ratings.md`, `docs/api/lobby.md` updated.

### 2. `BoardVerse.Core/Messages/ApiErrorMessages.cs` — Brace structure fix

- **Triệu chứng:** Compiler báo `CS0117: 'ApiErrorMessages' does not contain a definition for 'Discovery'` (và tương tự cho `Session`, `System`).
- **Root cause:** Hai `}` thừa ở L3358 và L3438 làm các class `System`, `Session`, `Discovery` "rớt" ra khỏi `ApiErrorMessages`, nằm ở namespace level. Brace count tổng vẫn chẵn nên pre-build guard pass, nhưng compiler vẫn phát hiện nested sai.
- **Fix:** Di chuyển `}` ở L3358 (đóng `System` sớm) ra cuối file để đóng `namespace`. Đồng thời bỏ `}` thừa tại L3438.
- **Ảnh hưởng:** Pure build fix — không đổi behavior, không đổi API. `ApiErrorMessages.Discovery.*`, `ApiErrorMessages.Session.*`, `ApiErrorMessages.System.*` giờ truy cập được bình thường.

### 3. `BoardVerse.Services/Services/BoardGameDiscoveryService.cs` — Compilation fixes

- **Triệu chứng 1:** `CS1729: 'PaginationParams' does not contain a constructor that takes 2 arguments`. Class `PaginationParams` chỉ có parameterless constructor + property setters.
- **Fix 1:** Đổi `new PaginationParams(1, 1)` → `new PaginationParams { PageNumber = 1, PageSize = 1 }`.
- **Triệu chứng 2:** Sau fix brace, xuất hiện tiếp `CS1061: 'NearbyCafeSearchResultDto' does not contain a definition for 'Data'`.
- **Fix 2:** Đổi `cafeResult.Data?.FirstOrDefault()` → `cafeResult.Cafes.Data?.FirstOrDefault()` (DTO có property lồng `Cafes.Data`).
- **Ảnh hưởng:** Pure bug fix — sửa code gọi sai property. Service `BoardGameDiscoveryService.RunSurveyAsync` giờ gọi `ICafeService.GetNearbyCafesAsync` đúng cách.

### 4. Pre-build guard đã chạy đúng

- File `BoardVerse.Core/BoardVerse.Core.csproj` có `Target Name="GuardApiErrorMessages"` đếm `{` / `}` mỗi lần build.
- Trong trường hợp này brace count tổng chẵn (570 mỗi loại) nên guard KHÔNG bắt được — nhưng trình biên dịch C# vẫn phát hiện sai vị trí `}`. **Cảnh báo:** Cân nhắc nâng cấp guard thành kiểm tra indent / stack-position chứ không chỉ đếm tổng.

### 5. `BoardGameDiscoveryService.RunSurveyAsync` chưa có controller endpoint

- Interface `IBoardGameDiscoveryService` đã có 3 method (`RunSurveyAsync`, `GetSavedGamesAsync`, `ToggleSaveAsync`), nhưng `BoardGameController` chưa có action `[HttpPost("survey")]` nào gọi service này.
- Tức là service tồn tại nhưng chưa exposed qua API. Có thể là gap phía frontend đang chờ FE wiring.

---

## 2026-09-07 — Encoding scan

- Total .cs files scanned: 978.
- 8 file UTF-8 có BOM (canonical).
- 970 file UTF-8 không BOM — recommend ADD BOM cho VS/Rider tương thích tốt hơn.
- 0 file non-UTF-8.

---

## 2026-09-05 — Test report v1.0

- 1685 test cases — 1681 PASS, 4 SKIP, 0 FAIL (99.76% pass).
- Chi tiết xem `docs/TEST_REPORT.md`.

---

## Trước 2026-09-05

- Xem `docs/feature-audit.md` (2026-08-12 pass) và `docs/technical/gaps-report-20260827.md` cho snapshot business features tại thời điểm đó.
