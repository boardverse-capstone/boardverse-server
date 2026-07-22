# TournamentController

**Base route:** `/api/v1/tournaments`
**Controller:** `TournamentController.cs`
**Role:** Player — đã đăng nhập

API Tournament dành cho mobile app (Player): xem danh sách giải đang mở, đăng ký / rút lui, xem chi tiết giải, danh sách participants + matches, lịch sử Elo cá nhân, bảng xếp hạng toàn hệ thống. Tuân thủ BR-05, BR-10 (Elo chỉ dùng trong Tournament).

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/open` | GET | Danh sách giải đang mở đăng ký (mọi game) |
| `/{tournamentId}` | GET | Chi tiết giải đấu |
| `/{tournamentId}/participants` | GET | Danh sách người chơi đã đăng ký |
| `/{tournamentId}/matches` | GET | Danh sách toàn bộ bàn đấu |
| `/{tournamentId}/matches/round/{roundNumber}` | GET | Danh sách bàn đấu của 1 vòng |
| `/{tournamentId}/register` | POST | Đăng ký tham gia |
| `/{tournamentId}/unregister` | POST | Rút lui trước khi giải bắt đầu |
| `/my-registrations` | GET | Danh sách giải của tôi |
| `/my-elo-history` | GET | Lịch sử Elo của tôi |
| `/leaderboard` | GET | Top N người chơi theo GlobalElo |

---

## GET /api/v1/tournaments/open

Lấy danh sách tournament đang mở đăng ký (mọi game — Player lọc theo `gameTemplateId` ở client).

**Response 200:** danh sách `TournamentResponseDto`.

**Lỗi:** `400` thiếu `gameTemplateId`; `401` thiếu token; `500` lỗi hệ thống.

---

## GET /api/v1/tournaments/{tournamentId}

Lấy thông tin chi tiết giải đấu kèm trạng thái đăng ký của user hiện tại (nếu có).

**Response 200:** `TournamentResponseDto`.

**Lỗi:** `401` thiếu token; `404` không tìm thấy; `500` lỗi hệ thống.

---

## GET /api/v1/tournaments/{tournamentId}/participants

Danh sách người chơi đã đăng ký giải (bao gồm đã rút lui).

**Response 200:** danh sách `TournamentParticipantResponseDto`.

**Lỗi:** `401`; `404` không tìm thấy giải; `500`.

---

## GET /api/v1/tournaments/{tournamentId}/matches

Toàn bộ bàn đấu của giải (Swiss + Final), sắp xếp theo Round → MatchNumber.

**Response 200:** danh sách `TournamentMatchResponseDto`.

**Lỗi:** `401`; `404`; `500`.

---

## GET /api/v1/tournaments/{tournamentId}/matches/round/{roundNumber}

Bàn đấu của một vòng cụ thể.

**Path param:** `roundNumber` (1-3 Swiss, 4 Final).

**Response 200:** danh sách `TournamentMatchResponseDto`.

**Lỗi:** `401`; `404`; `500`.

---

## POST /api/v1/tournaments/{tournamentId}/register

Đăng ký tham gia. Snapshot `Karma` + `Elo` tại thời điểm đăng ký.

**Điều kiện:**
- Tournament `Status = RegistrationOpen` và `RegistrationDeadline > now`.
- Karma hiện tại `>= MinKarmaRequirement` (range 0-100).
- Elo hiện tại nằm trong `[MinEloRequirement, MaxEloRequirement]` (mặc định 800-2400).
- Chưa đăng ký trước đó.
- Chưa đầy `MaxParticipants`.

**Response 201:** `TournamentParticipantResponseDto` — `status = Registered`.

**Lỗi:**
- `401` thiếu token.
- `403` không đủ Karma hoặc Elo ngoài range.
- `404` không tìm thấy giải.
- `409` giải không mở / đã quá hạn / đã đăng ký / đầy chỗ.

---

## POST /api/v1/tournaments/{tournamentId}/unregister

Rút lui khỏi giải đấu. Chỉ thực hiện được khi giải chưa OnGoing.

**Response 200:** `TournamentParticipantResponseDto` — `status = Withdrawn`.

**Lỗi:**
- `401`; `404` chưa đăng ký.
- `409` đã rút lui / giải đã bắt đầu.

---

## GET /api/v1/tournaments/my-registrations

Danh sách giải của người dùng hiện tại (đang đăng ký / đã tham gia / đã rút lui).

**Query:**

| Param | Type | Required | Mô tả |
|-------|------|----------|--------|
| `status` | string | ❌ | Filter theo TournamentStatus (`Draft`, `RegistrationOpen`, `RegistrationClosed`, `OnGoing`, `Completed`, `Cancelled`) |

**Response 200:** danh sách `TournamentResponseDto`.

---

## GET /api/v1/tournaments/my-elo-history

Lịch sử Elo của người dùng hiện tại qua các tournament.

**Response 200:** `EloHistoryResponseDto`.

---

## GET /api/v1/tournaments/leaderboard

Top N người chơi theo `GlobalElo` (mặc định 100).

**Query:**

| Param | Type | Required | Mô tả |
|-------|------|----------|--------|
| `topCount` | int | ❌ | Số lượng (mặc định 100, max 500) |

**Response 200:** `LeaderboardResponseDto`.

---

## Luồng tích hợp

1. Player mở app → `GET /tournaments/open` xem các giải đang tuyển (lọc game ở client).
2. `POST /tournaments/{id}/register` để đăng ký (status 201 Created).
3. Theo dõi vòng đấu: `GET /tournaments/{id}/matches/round/{n}`.
4. Check-in tại quán: Manager dùng POS (`POST /api/v1/pos/tournaments/{id}/participants/{participantId}/check-in`).
5. Sau khi giải kết thúc → Karma cộng vào profile, lịch sử Elo cập nhật (`GET /my-elo-history`).

---

## Lưu ý phân quyền

`TournamentController` (Player-facing) chỉ phục vụ flow xem + đăng ký/rút lui. Mọi thao tác vận hành (tạo giải, start, extend-registration, ghi kết quả, complete, hủy, pairing-mode, walk-in, no-show) đều nằm trong `TournamentPosController` xem tại `docs/api/tournament-pos.md`.

---

## Auto-Pair Algorithm (Swiss pairing)

Hệ thống tự động ghép bàn cho từng round Swiss bằng **Adaptive Balanced Swiss** (`SwissPairingHelper` + `TableSizeOptimizer`).

### Design goals (priority order)

1. **Anti-repeat** — 2 người không gặp lại nhau giữa các round.
2. **Swiss score balance** — cùng điểm gặp nhau.
3. **Elo balance** — variance Elo giữa các bàn thấp nhất.
4. **Table size balance** — tự tính số bàn + kích thước tối ưu.

### Strategies

| Round | Strategy |
|-------|----------|
| **Round 1** | Seeded snake draft theo Elo desc |
| **Round 2+** | Greedy constraint solver với 16 attempts, giữ attempt có quality score cao nhất |

### Table size auto-optimization

`TableSizeOptimizer.CalculateOptimalTableSizes(N)` thử 3 strategies và chọn cái có quality score cao nhất:

| Strategy | Mô tả |
|----------|-------|
| **Equal** | Chia đều nhất có thể |
| **Tailed** | Full bàn từ đầu, bàn cuối nhỏ hơn |
| **Front-heavy** | Giống Tailed, prefer full tables |

**Ví dụ auto-sizing:**

| N người | Optimal split |
|---------|---------------|
| 5 | [3, 2] |
| 6 | [3, 3] |
| 7 | [4, 3] |
| 9 | [3, 3, 3] |
| 10 | [4, 3, 3] |
| 11 | [4, 4, 3] |
| 12 | [4, 4, 4] |
| 13 | [4, 3, 3, 3] |

**Quality scoring:**

| Component | Weight |
|-----------|--------|
| Equal bonus (low variance) | +15 × (1 − variance/maxSize) |
| Full table bonus | +2 mỗi bàn 4 |
| Solo penalty (bàn 1 người) | −100 (không hợp lệ) |
| Size-2 penalty | −3 mỗi bàn 2 |
| Count penalty (overhead) | −2 mỗi bàn |

### Snake draft

Sau khi có optimal sizes, distribute theo **snake draft** để mỗi bàn có 1 top Elo + 1 bottom Elo + 2 mid:

```
9 players [2000, 1990, ..., 1920], tables = [3, 3, 3]:
  Bàn 1: 1 (top), 6, 7          ← top + 2 bottom-side
  Bàn 2: 2, 5, 8
  Bàn 3: 3, 4, 9 (bottom)
```

### Edge cases

| Case | Handling |
|------|----------|
| N ≤ 4 | 1 bàn duy nhất |
| N = 1 | Trả về 1 bàn 1 người (manager xử lý bye thủ công) |
| 5, 9, 13 (không chia hết 4) | Auto-split [3,2], [3,3,3], [4,3,3,3] — không có bàn 1 |
| Constraint solver timeout | Best-effort split by score, log warning |

Xem chi tiết tại `BoardVerse.Core/Helpers/SwissPairingHelper.cs` + `TableSizeOptimizer.cs`.