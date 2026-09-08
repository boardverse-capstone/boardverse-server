# BoardVerse — Changelog

Lịch sử thay đổi đáng chú ý của codebase. Cập nhật theo từng PR / commit quan trọng.

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
