# TournamentPosController

**Base route:** `/api/v1/pos/tournaments`
**Controller:** `TournamentPosController.cs`
**Role:** Manager — phải là `ManagerId` của cafe tạo tournament

API dành cho máy POS tại quán: tạo/cập nhật/hủy giải, mở/đóng đăng ký, start giải (build Swiss Round 1), check-in participant, đánh dấu no-show, start/record/cancel match, chuyển vòng, complete giải đấu (apply Karma + Elo vào profile).

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/cafes/{cafeId}` | POST | Tạo giải đấu mới |
| `/{tournamentId}` | PATCH | Cập nhật thông tin (Draft only) |
| `/cafes/{cafeId}` | GET | Danh sách giải theo cafe |
| `/cafes/{cafeId}/active` | GET | Giải đang OnGoing của cafe (POS auto-load) |
| `/{tournamentId}/open-registration` | POST | Mở đăng ký |
| `/{tournamentId}/close-registration` | POST | Đóng đăng ký |
| `/{tournamentId}/start` | POST | Bắt đầu giải (build Round 1) |
| `/{tournamentId}/cancel` | POST | Hủy giải |
| `/{tournamentId}/complete` | POST | Hoàn thành giải (apply Karma + Elo) |
| `/{tournamentId}/advance-round` | POST | Chuyển sang vòng tiếp theo |
| `/{tournamentId}/pairing-mode` | PATCH | Chuyển Auto ↔ Manual pairing |
| `/{tournamentId}/preview-pairings/{roundNumber}` | GET | Preview pairings trước khi set |
| `/{tournamentId}/pairings/{roundNumber}` | PUT | Set pairings thủ công |
| `/{tournamentId}/pairings/{roundNumber}` | DELETE | Xóa pairings thủ công, dùng Auto |
| `/{tournamentId}/participants/{participantId}/check-in` | POST | Check-in participant |
| `/{tournamentId}/participants/{participantId}/no-show` | POST | Đánh dấu no-show (Karma penalty) |
| `/{tournamentId}/walk-in` | POST | Thêm khách vãng lai (walk-in) |
| `/matches/{matchId}/start` | POST | Bắt đầu 1 bàn đấu |
| `/matches/{matchId}/result` | POST | Ghi nhận kết quả |
| `/matches/{matchId}/result` | PATCH | Sửa kết quả đã ghi (Swiss rounds only) |
| `/matches/{matchId}/cancel` | POST | Hủy bàn đấu |

---

## POST /api/v1/pos/tournaments/cafes/{cafeId}

Tạo giải đấu Splendor mới ở trạng thái `Draft`.

**Body mẫu:**

```json
{
  "title": "Splendor Tournament Thủ Đức - July 2026",
  "description": "Giải đấu dành cho player trong khu vực Thủ Đức.",
  "gameTemplateId": "<optional, mặc định lookup 'Splendor'>",
  "startTime": "2026-08-01T19:00:00Z",
  "registrationDeadline": "<optional, mặc định startTime - 24h>",
  "roundDurationMinutes": 45,
  "maxParticipants": 16,
  "minKarmaRequirement": 0,
  "winnerKarmaBonus": 50,
  "finalistKarmaBonus": 20,
  "noShowKarmaPenalty": -30
}
```

**Validation:**
- `Title`: 5-200 ký tự.
- `StartTime > now`.
- `MaxParticipants`: 4-32, là bội số của 4.
- `RegistrationDeadline < StartTime`.

**Response 201:** `TournamentResponseDto` — `status = Draft`.

**Lỗi:** `400` dữ liệu không hợp lệ; `403` không phải Manager; `404` không tìm thấy Splendor.

---

## PATCH /api/v1/pos/tournaments/{tournamentId}

Cập nhật thông tin (partial). Chỉ hoạt động khi Tournament đang ở trạng thái `Draft`.

**Body:** các trường optional (chỉ gửi field muốn đổi).

**Response 200:** `TournamentResponseDto`.

**Lỗi:** `400`; `403`; `404`; `409` không phải `Draft`.

---

## GET /api/v1/pos/tournaments/cafes/{cafeId}

Danh sách giải của cafe (mặc định mới nhất trước).

**Query:**

| Param | Type | Required | Mô tả |
|-------|------|----------|--------|
| `status` | string | ❌ | `Draft` / `RegistrationOpen` / `RegistrationClosed` / `OnGoing` / `Completed` / `Cancelled` |

**Response 200:** danh sách `TournamentResponseDto`.

---

## POST /api/v1/pos/tournaments/{tournamentId}/open-registration

Chuyển `Draft → RegistrationOpen`. Yêu cầu `RegistrationDeadline > now`.

**Response 200:** `TournamentResponseDto`.

**Lỗi:** `403`; `404`; `409` không ở `Draft` hoặc đã quá hạn.

---

## POST /api/v1/pos/tournaments/{tournamentId}/close-registration

Chuyển `RegistrationOpen → RegistrationClosed`. Cũng được gọi tự động bởi `TournamentExpiryJob` mỗi 60s nếu `RegistrationDeadline <= now`.

**Response 200:** `TournamentResponseDto`.

---

## POST /api/v1/pos/tournaments/{tournamentId}/start

Bắt đầu giải (`RegistrationClosed → OnGoing`). Tự động build Swiss Round 1 từ các participants đã check-in.

**Validation:** số người `CheckedIn/Active >= MinParticipants` (mặc định 4).

**Response 200:** `TournamentResponseDto` — `currentRound = 1`.

**Lỗi:** `409` chưa đóng đăng ký / chưa đủ người.

---

## POST /api/v1/pos/tournaments/{tournamentId}/walk-in

Thêm walk-in participant (khách vãng lai, không có tài khoản BoardVerse).

**Cơ chế lock thực tế (Option A):**
- Cho phép khi: giải đang ở `RegistrationOpen` / `RegistrationClosed` / `OnGoing` (R1 chưa Completed).
- Từ chối khi: R1 đã có ≥1 match `Completed` (Swiss score đã tồn tại).
- Lý do fairness: player gốc đã đầu tư 1 round, walk-in không thể nhảy vào R2+ để "rửa" Swiss score.

Walk-in luôn `JoinedRoundNumber = 1`. **Không nhận Elo/Karma** (BR-13/14 mirror). **Được vào Final** nếu đủ top N.

**Body mẫu:**

```json
{
  "displayName": "Lê A",
  "phoneNumber": "0912345678"
}
```

| Field | Required | Mô tả |
|-------|----------|--------|
| `displayName` | ✅ | Tên hiển thị (1-100 ký tự) |
| `phoneNumber` | ❌ | SĐT liên lạc (dùng để thông báo kết quả/prize collection nếu thắng giải) |

**Response 201:** `TournamentParticipantResponseDto` — `isWalkIn = true`, `walkInPhoneNumber = "0912345678"`.

**Lỗi:**
- `400` thiếu `displayName`.
- `403` không phải Manager của cafe.
- `404` không tìm thấy giải.
- `409`:
  - Giải đang ở `Draft` / `Completed` / `Cancelled`.
  - R1 đã hoàn thành (≥1 match Completed) → không thể thêm walk-in.
  - Đã có bàn chung kết (Final).
  - Round hiện tại đang diễn ra (≥1 match `OnGoing` ở `currentRound`).
  - Trùng `displayName` (case-insensitive).

---

## POST /api/v1/pos/tournaments/{tournamentId}/cancel

Hủy giải. Yêu cầu lý do nếu đã có người đăng ký.

**Body:**

```json
{ "reason": "Cafe đóng cửa đột xuất do bảo trì" }
```

**Response 200:** `TournamentResponseDto` — `status = Cancelled`.

**Lỗi:** `400` thiếu lý do khi đã có người đăng ký; `403`; `404`; `409` không thể hủy (Completed/OnGoing).

---

## POST /api/v1/pos/tournaments/{tournamentId}/complete

Hoàn thành giải đấu. Yêu cầu Final match đã `Completed`.

**Side effects:**
- Apply `WinnerKarmaBonus` + `FinalistKarmaBonus` cho các participant (ghi `KarmaLog`).
- Sync `FinalElo` + bonus vào `UserProfile.GlobalElo` (Winner +50 Elo bonus).
- Set `FinalRank` cho 4 finalist.

**Response 200:** `TournamentResponseDto` — `status = Completed`.

**Lỗi:** `403`; `404`; `409` chưa phải `OnGoing` hoặc Final chưa hoàn thành.

---

## POST /api/v1/pos/tournaments/{tournamentId}/advance-round

Chuyển sang vòng đấu kế tiếp. Manager trigger thủ công sau khi toàn bộ bàn của vòng hiện tại đã ghi nhận kết quả.

**Behavior:**
- `nextRound ≤ PreliminaryRounds` (3): build Swiss round mới từ Active participants.
- `nextRound = TotalRounds` (4): build bàn chung kết (Top 4 theo Swiss score).

**Validation:** tất cả match của vòng hiện tại phải ở `Completed` hoặc `Cancelled`.

**Response 200:** `TournamentResponseDto` — `currentRound` đã tăng.

**Lỗi:** `403`; `404`; `409` giải không ở `OnGoing` / vòng hiện tại chưa xong / đã đến vòng cuối.

---

## POST /api/v1/pos/tournaments/{tournamentId}/participants/{participantId}/check-in

Check-in participant tại quán.

**Response 200:** `TournamentParticipantResponseDto` — `status = CheckedIn`.

**Lỗi:** `403`; `404`; `409` đã check-in rồi.

---

## POST /api/v1/pos/tournaments/{tournamentId}/participants/{participantId}/no-show

Đánh dấu no-show. Áp dụng `NoShowKarmaPenalty` (mặc định -30) vào profile + ghi `KarmaLog` audit.

**Response 200:** `TournamentParticipantResponseDto` — `status = NoShow`.

**Lỗi:** `403`; `404`; `409` participant đã Finished.

---

## POST /api/v1/pos/tournaments/matches/{matchId}/start

Chuyển trạng thái bàn đấu `Scheduled → OnGoing`. Set `ActualStartTime = now`.

**Response 200:** `TournamentMatchResponseDto`.

**Lỗi:** `403`; `404`; `409` bàn đã bắt đầu hoặc đã kết thúc.

---

## POST /api/v1/pos/tournaments/matches/{matchId}/result

Ghi nhận kết quả bàn đấu: điểm từng người chơi + người thắng.

**Hỗ trợ draw:** Nếu không có winner (tất cả hòa), để `winnerUserId = null`. Hệ thống sẽ:
- Không cập nhật Elo
- Tăng `SwissDraws` cho tất cả players

**Walk-in trong Final:** Walk-in (UserId=null) có thể tham gia Final và được xếp rank. Không nhận Elo/Karma.

**Body mẫu:**

```json
{
  "matchId": "<matchId>",
  "winnerUserId": "<userId hoặc null nếu draw>",
  "results": [
    { "userId": "<user1>", "score": 15, "cardsBought": 7 },
    { "userId": "<user2>", "score": 12, "cardsBought": 8 },
    { "userId": "<user3>", "score": 10, "cardsBought": 6 },
    { "userId": "<user4>", "score": 8,  "cardsBought": 9 }
  ],
  "notes": "Ván đấu sát nút",
  "recordedByStaffId": "<optional>"
}
```

| Field | Required | Mô tả |
|-------|----------|--------|
| `matchId` | ✅ | Match ID |
| `winnerUserId` | ✅ | Winner (nullable = draw). Walk-in winner: dùng `participantId` (Guid) |
| `results` | ✅ | Điểm từng player |

**Side effects:**
- Aggregate `TotalPrestigePoints` + `TotalCardsBought` cho participant (Swiss tiebreaker).
- Apply Elo delta (4-player Splendor: 1 winner, 3 losers, draw-all nếu `winnerUserId = null`).
- Increment `SwissWins` / `SwissDraws` / `SwissLosses`.
- Nếu là Final: assign `FinalRank` cho 4 finalist (kể cả walk-in), mark participants `Finished`.

**Response 200:** `TournamentMatchResponseDto`.

**Lỗi:** `400` Winner không nằm trong 4 players; `403`; `404`; `409` bàn không ở `OnGoing`/`Scheduled`.

---

## POST /api/v1/pos/tournaments/matches/{matchId}/cancel

Hủy bàn đấu (ví dụ: bàn thiếu người, dispute). Yêu cầu lý do (lưu vào Notes).

**Body:**

```json
{ "reason": "Bàn thiếu 1 người do check-in sai" }
```

**Side effects:** Status → `Cancelled`, Notes append audit log.

**Response 200:** `TournamentMatchResponseDto`.

**Lỗi:** `400` thiếu lý do; `403`; `404`; `409` bàn đã `Completed`.

---

## PATCH /api/v1/pos/tournaments/matches/{matchId}/result

Sửa kết quả bàn đấu đã ghi (chỉ cho Swiss rounds, không cho Final).

**Validation:** Match đã `Completed`, Swiss round chưa qua (chưa build round kế tiếp).

**Body mẫu:**

```json
{
  "matchId": "<matchId>",
  "winnerUserId": "<userId>",
  "correctionReason": "Nhập sai điểm user2",
  "results": [
    { "userId": "<user1>", "score": 15, "cardsBought": 7 },
    { "userId": "<user2>", "score": 14, "cardsBought": 8 },
    { "userId": "<user3>", "score": 10, "cardsBought": 6 },
    { "userId": "<user4>", "score": 8,  "cardsBought": 9 }
  ]
}
```

**Side effects:**
- Revert Elo + Swiss scores cũ
- Apply Elo + Swiss scores mới
- Append correction reason vào Notes

**Response 200:** `TournamentMatchResponseDto`.

---

## PATCH /api/v1/pos/tournaments/{tournamentId}/pairing-mode

Chuyển chế độ pairing: Auto ↔ Manual.

**Body:**

```json
{ "mode": "Manual" }
```

| Mode | Mô tả |
|------|--------|
| `Auto` | Dùng Adaptive Balanced Swiss (mặc định) |
| `Manual` | Manager tự set pairings trước khi start round |

**Response 200:** `TournamentResponseDto`.

---

## GET /api/v1/pos/tournaments/{tournamentId}/preview-pairings/{roundNumber}

Preview pairings cho round N (dùng Auto algorithm) mà không apply.

**Response 200:** `RoundPairingsResponseDto` — danh sách bàn với participants.

---

## PUT /api/v1/pos/tournaments/{tournamentId}/pairings/{roundNumber}

Set pairings thủ công cho round N (chỉ khi `PairingMode = Manual`).

**Body:**

```json
{
  "tables": [
    { "playerIds": ["<guid1>", "<guid2>", "<guid3>", "<guid4>"] },
    { "playerIds": ["<guid5>", "<guid6>", "<guid7>", "<guid8>"] }
  ]
}
```

**Validation:**
- Số players = bội số của 4 (hoặc remainder table nếu có <4)
- Không repeat pairing (2 players đã đấu nhau ở round trước)

**Response 200:** `RoundPairingsResponseDto`.

---

## DELETE /api/v1/pos/tournaments/{tournamentId}/pairings/{roundNumber}

Xóa pairings thủ công, chuyển về Auto cho round đó.

**Response 200:** `RoundPairingsResponseDto`.

---

## GET /api/v1/pos/tournaments/cafes/{cafeId}/active

Lấy giải đang OnGoing của cafe (dùng cho POS auto-load khi mở app).

**Response 200:** danh sách `TournamentResponseDto` với `status = OnGoing`.

---

## State machine

```
Draft ──open──> RegistrationOpen ──close──> RegistrationClosed ──start──> OnGoing
  │                    │                                                        │
  ├──cancel──┐         ├──cancel──┐                                            │
  │          ▼         │          ▼                                            ▼
  └──> Cancelled <─────┴────── Cancelled                                    Completed
                                                                                ▲
                                                                                │
                                                ┌──advance-round (Round N+1)──┤
                                                │                            │
                                          R1 Scheduled → OnGoing → Completed ─┘
                                          R2 (auto-built by advance-round)
                                          R3 → advance-round auto-builds Final
                                          R4 Final → record → complete
```

## Background jobs

### TournamentExpiryJob
Chạy mỗi 60 giây, auto-close các giải đã quá `RegistrationDeadline` mà Manager quên close thủ công. Idempotent (chỉ xử lý `RegistrationOpen`).

### TournamentReminderJob
Chạy mỗi 5 phút, gửi reminder notification cho participants chưa check-in của các giải đấu sắp bắt đầu.

**Reminder schedule:**
| Thời điểm | Nội dung |
|------------|----------|
| T-30 phút | "Giải đấu 'X' bắt đầu sau 30 phút... Vui lòng check-in sớm!" |
| T-15 phút | "Nhắc nhở: Giải đấu 'X' bắt đầu sau 15 phút... Hãy có mặt ngay!" |
| T-5 phút | "Cảnh báo: Giải đấu 'X' bắt đầu sau 5 phút... Vui lòng check-in ngay!" |

### TournamentNoShowDetectionJob
Chạy mỗi 1 phút, tự động đánh dấu no-show cho participants đã đăng ký nhưng không check-in khi giải bắt đầu.

**Trigger conditions:**
- Tournament `Status = OnGoing`
- `CurrentRound = 1`
- `StartedAt` trong vòng 5 phút gần đây

**Side effects:**
- Đánh dấu participant `Status = NoShow`
- Áp dụng `NoShowKarmaPenalty` (mặc định -30 điểm) vào `UserProfile.KarmaPoints`
- Ghi `KarmaLog` audit trail
- Gửi notification thông báo bị no-show

## TournamentResponseDto fields

| Field | Mô tả |
|-------|--------|
| `StartedAt` | Thời điểm bắt đầu giải (khi chuyển sang OnGoing). Dùng để track khi nào tournament start để detect no-show. |