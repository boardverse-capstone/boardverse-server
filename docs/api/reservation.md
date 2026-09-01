# ReservationController

**Base route:** `/api/v1/reservations`
**Controller:** `ReservationController.cs`
**Role:** Player — đã đăng nhập (JWT bearer). `cafe-approval` yêu cầu role Cafe Manager.

Luồng đặt cọc + giữ chỗ mới theo `lobby-booking-deposit-bvc.mdc` (BR §XXI-A.2..21A.6).
Thay thế luồng cũ `BookingDeposit` (VND/SePay) — flow mới dùng BVC atomic transaction.

Đặc điểm:

- **Idempotency** (BR §XVII.1): mọi request phải có `IdempotencyKey` 8–128 ký tự; retry với cùng key trả cùng kết quả.
- **Server authoritative** (BR §XVII.2): client chỉ hiển thị `FinalDeposit` từ quote; `ConfirmAsync` kiểm tra `ExpectedFinalDeposit` khớp với server.
- **Atomic transaction** (BR §17.4): hold BVC + lock seat + lock game + insert Reservation + insert Lobby trong 1 transaction.
- **Outbox** (BR §17.5): commit thành công mới phát `LobbyActivated` qua SignalR.

Tuân thủ business rules:

- **BR-DEPOSIT-01**: Host trả toàn bộ cọc.
- **BR-DEPOSIT-02 (2026-08-27)**: `finalDeposit = round(20% × cafeBasePrice / 1000, floor ≥ 1) × finalMaxPlayers` (BVC). Cọc tỷ lệ với quy mô nhóm và giá vé cơ bản của cafe.
- **BR-DEPOSIT-02 (2026-08-27 — chỉnh)**: Backend trả về `finalDeposit` (BVC tổng) + `cafeBasePriceVnd` (VND/người) + `maxPlayers`. FE không còn hiển thị breakdown theo công thức `% × giá × số người` (bỏ `depositPerPerson`, `depositPercentage`, `depositRatePerPerson`, `baseDeposit`, `riskMultiplier`, `minDepositApplied`).
- **BR-NEW-01**: `maxPlayers` + `minDeposit` theo khoảng cách `playDate`.
- **BR-LOBBY-01a/b**: Buffer ≥ 120 phút OK, 60–120 cảnh báo, < 60 từ chối.
- **BR-USER-LIMIT-01..05**: 1 host lobby + 1 member lobby = tối đa 2 active; cap tổng heldBalance.
- **BR-NEW-02**: 1 lobby active / `playDate` / user.
- **BR-NEW-08**: 1 lobby active / `playDate+scheduledStartTime` / `cafe` / user.
- **BR-NEW-11**: `playDate ≥ DistantThresholdDays` (mặc định 2) + lobby **public** → lobby ở `PendingCafeApproval`, chờ cafe duyệt 24h. Lobby **private** không cần cafe duyệt.
- **BR-NEW-15 (2026-08-18):** Hệ thống giờ dùng `preferredStartTime` + `preferredEndTime` thay vì `TimeSlot` enum. `CafeScheduleOverride` dùng `ApplyDate` thay vì `TimeSlot`.
- **BR-REFUND-02**: Cancel theo grace 15 phút + ≥24h / <24h. **Early checkout** dùng `playedRatio`: <50% → 0%, ≥50% → 30%, ≥90% → 0% (treated as on-time).
- **BR-RESERVATION-01/02**: Giữ `maxPlayers` ghế + 1 game copy.

---

## Mục lục

- [GET /{id}](#get-id)
- [GET /](#get-)
- [GET /search](#get-search)
- [GET /pending-cafe-approval](#get-pending-cafe-approval)
- [GET /{id}/cafe-approval](#get-idcafe-approval)
- [POST /quote](#post-quote)
- [POST /confirm](#post-confirm)
- [POST /{id}/cancel](#post-idcancel)
- [POST /{id}/cafe-approval](#post-idcafe-approval)
- [POST /{id}/check-in](#post-idcheck-in)
- [POST /{id}/end](#post-idend)
- [POST /{id}/extend](#post-idextend)
- [POST /by-code/{reservationCode}/check-in](#post-by-codereservationcodecheck-in)
- [POST /{id}/cancel-after-checkin](#post-idcancel-after-checkin)
- [Luồng tích hợp](#luồng-tích-hợp)
- [State machine](#state-machine)

---

## GET /{id}

Lấy chi tiết một reservation.

### Request

- Method: `GET`
- Path: `/api/v1/reservations/{reservationId}`
- Auth: Player (JWT)

### Response 200

```json
{
  "statusCode": 200,
  "message": "ReservationRetrieved",
  "data": {
    "id": "...",
    "cafeId": "...",
    "cafeName": "BoardGame Cafe A",
    "gameId": "...",
    "gameName": "Catan",
    "hostId": "...",
    "hostDisplayName": "Player A",
    "playDate": "2026-08-04",
    "timeSlot": "evening",
    "preferredStartTime": "19:30:00",
    "scheduledStartTime": "2026-08-04T18:00:00Z",
    "scheduledEndTime": "2026-08-04T23:00:00Z",
    "recruitmentDeadline": "2026-08-04T17:40:00Z",
    "minPlayers": 4,
    "maxPlayers": 6,
    "currentPlayers": 3,
    "depositAmount": 100000,
    "status": "Holding",
    "lobbyId": "...",
    "lobbyShareCode": "K7H3NP9X",
    "lobbyStatus": "Open",
    "cafeRejectionReason": null,
    "requiresCafeApproval": false,
    "cafeApprovalDeadline": null,
    "reservationCode": "K7H3NP9X",
    "createdAt": "2026-08-02T15:30:00Z",
    "updatedAt": "2026-08-02T15:30:00Z",
    "isHost": true,
    "canCancel": true,
    "checkedInAt": null,
    "actualEndAt": null,
    "playedRatio": null,
    "endReason": null,
    "walkInWindowId": null,
    "cancelledBy": null,
    "cancelReason": null,
    "tableNumber": null
  }
}
```

**Ví dụ khi bị cafe từ chối** (`status = CancelledByCafe`):

```json
{
  "statusCode": 200,
  "message": "ReservationRetrieved",
  "data": {
    "id": "...",
    "status": "CancelledByCafe",
    "lobbyStatus": "RejectedByCafe",
    "cafeRejectionReason": "Quán đông, không nhận thêm khách",
    "depositAmount": 120000,
    "refundPolicyApplied": "BR-REFUND-04",
    ...
  }
}
```

> **Lưu ý:** Khi `status = CancelledByCafe`, `cafeRejectionReason` chứa lý do cafe từ chối. Player có thể filter `GET /reservations?statuses=CancelledByCafe` để xem danh sách reservation bị từ chối.

### Lỗi thường gặp

| Status | Message |
|--------|---------|
| `401` | Thiếu token |
| `403` | Không có quyền xem reservation này |
| `404` | Không tìm thấy reservation |

---

## GET /

Lấy danh sách reservation của user (host hoặc member). Có filter + phân trang.

### Request

- Method: `GET`
- Path: `/api/v1/reservations`
- Auth: Player (JWT)

### Query

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `status` | enum | No | Filter theo status: `Holding`, `Confirmed`, `Expired`, `Cancelled`, `Completed`, `NoShow` |
| `playDate` | date | No | Filter theo ngày dự kiến |
| `cafeId` | guid | No | Filter theo cafe |
| `hostedByMe` | bool | No | `true` để chỉ xem reservation do user host |
| `joinedByMe` | bool | No | `true` để chỉ xem reservation user tham gia (lobby member) |
| `page` | int | No | Số trang (≥ 1). Mặc định 1 |
| `pageSize` | int | No | Số item/trang (1-50). Mặc định 20 |

**Lưu ý:** Nếu không truyền `hostedByMe` hoặc `joinedByMe`, mặc định trả cả hai (tất cả reservation liên quan đến user).

### Response 200

```json
{
  "statusCode": 200,
  "message": "ReservationsRetrieved",
  "data": {
    "items": [
      {
        "id": "...",
        "cafeId": "...",
        "cafeName": "BoardGame Cafe A",
        "gameId": "...",
        "gameName": "Catan",
        "playDate": "2026-08-04",
        "timeSlot": "evening",
        "currentPlayers": 3,
        "maxPlayers": 6,
        "depositAmount": 100000,
        "status": "Holding",
        "lobbyId": "...",
        "lobbyStatus": "Open",
        "reservationCode": "K7H3NP9X",
        "recruitmentDeadline": "2026-08-04T17:40:00Z",
        "isHost": true,
        "createdAt": "2026-08-02T15:30:00Z"
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalCount": 5,
    "totalPages": 1
  }
}
```

### Lỗi thường gặp

| Status | Message |
|--------|---------|
| `401` | Thiếu token |
| `400` | `page` hoặc `pageSize` không hợp lệ |

---

## GET /search

Tìm kiếm lịch hẹn theo tên game hoặc ngày tháng. Có filter + phân trang.

### Request

- Method: `GET`
- Path: `/api/v1/reservations/search`
- Auth: Player (JWT)

### Query

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `gameName` | string | No | Từ khóa tìm kiếm theo tên game (fuzzy, case-insensitive). |
| `fromDate` | date | No | Ngày bắt đầu filter (inclusive). |
| `toDate` | date | No | Ngày kết thúc filter (inclusive). |
| `statuses` | enum[] | No | Filter theo trạng thái. |
| `cafeId` | guid | No | Filter theo cafe. |
| `hostedByMe` | bool | No | Chỉ reservation do user host (default true). |
| `joinedByMe` | bool | No | Chỉ reservation user tham gia (default false). |
| `page` | int | No | Số trang (≥ 1). Mặc định 1 |
| `pageSize` | int | No | Số item/trang (1-100). Mặc định 20 |

**Lưu ý:** Nếu không truyền `hostedByMe` hoặc `joinedByMe`, mặc định trả cả hai (tất cả reservation liên quan đến user).

### Response 200

```json
{
  "statusCode": 200,
  "message": "ReservationsSearched",
  "data": {
    "items": [
      {
        "id": "...",
        "cafeId": "...",
        "cafeName": "BoardGame Cafe A",
        "gameId": "...",
        "gameName": "Catan",
        "playDate": "2026-08-04",
        "preferredStartTime": "19:30:00",
        "preferredEndTime": "22:00:00",
        "currentPlayers": 3,
        "maxPlayers": 6,
        "depositAmount": 100000,
        "status": "Holding",
        "lobbyId": "...",
        "lobbyStatus": "Open",
        "reservationCode": "K7H3NP9X",
        "scheduledStartTime": "2026-08-04T19:30:00Z",
        "scheduledEndTime": "2026-08-04T22:00:00Z",
        "recruitmentDeadline": "2026-08-04T19:10:00Z",
        "createdAt": "2026-08-02T15:30:00Z",
        "isHost": true,
        "tableNumber": null
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalCount": 5,
    "totalPages": 1
  }
}
```

### Ví dụ

```
GET /api/v1/reservations/search?gameName=Catan&fromDate=2026-08-01&toDate=2026-08-31
GET /api/v1/reservations/search?gameName=Splendor&hostedByMe=true
GET /api/v1/reservations/search?fromDate=2026-08-20&toDate=2026-08-25&statuses=Holding,Confirmed
GET /api/v1/reservations/search?cafeId=abc123-guid&page=1&pageSize=10
```

### Lỗi thường gặp

| Status | Message |
|--------|---------|
| `401` | Thiếu token |
| `400` | `page` hoặc `pageSize` không hợp lệ |
| `500` | Lỗi hệ thống |

---

## GET /pending-cafe-approval

Lấy danh sách lobby đang chờ cafe duyệt (BR-NEW-11). Dùng cho dashboard của Cafe Manager.

### Request

- Method: `GET`
- Path: `/api/v1/reservations/pending-cafe-approval`
- Auth: Cafe Manager (JWT)

### Query

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `cafeId` | guid | No | Filter theo cafe cụ thể |
| `playDate` | date | No | Filter theo ngày. Mặc định hôm nay |
| `page` | int | No | Số trang (≥ 1). Mặc định 1 |
| `pageSize` | int | No | Số item/trang (1-50). Mặc định 20 |

### Response 200

```json
{
  "statusCode": 200,
  "message": "PendingCafeApprovalLobbiesRetrieved",
  "data": {
    "items": [
      {
        "reservationId": "...",
        "lobbyId": "...",
        "hostId": "...",
        "hostName": "Player A",
        "hostPhone": "0912345678",
        "cafeId": "...",
        "cafeName": "BoardGame Cafe A",
        "gameId": "...",
        "gameName": "Catan",
        "playDate": "2026-08-07",
        "timeSlot": "evening",
        "timeSlotDisplay": "Tối (18:00 - 23:00)",
        "minPlayers": 4,
        "maxPlayers": 6,
        "currentPlayers": 1,
        "depositAmount": 120000,
        "scheduledStartTime": "2026-08-07T18:00:00Z",
        "scheduledEndTime": "2026-08-07T23:00:00Z",
        "cafeApprovalDeadline": "2026-08-05T18:00:00Z",
        "remainingApprovalHours": 24,
        "createdAt": "2026-08-04T10:00:00Z"
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalCount": 3,
    "totalPages": 1
  }
}
```

### Lỗi thường gặp

| Status | Message |
|--------|---------|
| `401` | Thiếu token |
| `403` | Không phải manager của bất kỳ cafe nào |

---

## GET /{id}/cafe-approval

Lấy chi tiết một reservation đang chờ cafe duyệt (BR-NEW-11). Dùng để xem thông tin đầy đủ trước khi duyệt/từ chối.

### Request

- Method: `GET`
- Path: `/api/v1/reservations/{reservationId}/cafe-approval`
- Auth: Cafe Manager (JWT)

### Response 200

```json
{
  "statusCode": 200,
  "message": "PendingCafeApprovalDetailRetrieved",
  "data": {
    "reservationId": "...",
    "lobbyId": "...",
    "hostId": "...",
    "hostName": "Player A",
    "cafeId": "...",
    "cafeName": "BoardGame Cafe A",
    "gameId": "...",
    "gameName": "Catan",
    "playDate": "2026-08-07",
    "timeSlot": "evening",
    "timeSlotDisplay": "Tối (18:00 - 23:00)",
    "minPlayers": 4,
    "maxPlayers": 6,
    "currentPlayers": 1,
    "depositAmount": 120000,
    "scheduledStartTime": "2026-08-07T18:00:00Z",
    "scheduledEndTime": "2026-08-07T23:00:00Z",
    "cafeApprovalDeadline": "2026-08-05T18:00:00Z",
    "remainingApprovalHours": 24,
    "createdAt": "2026-08-04T10:00:00Z"
  }
}
```

### Lỗi thường gặp

| Status | Message |
|--------|---------|
| `401` | Thiếu token |
| `403` | Không phải manager của cafe này |
| `404` | Không tìm thấy reservation hoặc reservation không ở trạng thái PendingCafeApproval |

---

## POST /quote

Tạo quote cho reservation. **KHÔNG tạo row DB** — chỉ validate + tính cọc.

### Request

- Method: `POST`
- Path: `/api/v1/reservations/quote`
- Auth: Player (JWT)

### Body

```json
{
  "cafeId": "guid",
  "gameId": "guid",
  "playDate": "2026-08-04",
  "preferredStartTime": "19:30",
  "preferredEndTime": "22:00",
  "minPlayers": 4,
  "maxPlayers": 6,
  "isPrivate": false,
  "idempotencyKey": "quote-abc-12345"
}
```

| Field | Type | Required | Values |
|---|---|---|---|
| `cafeId` | guid | Yes | Cafe còn hoạt động. |
| `gameId` | guid | Yes | Game có trong `CafeGameInventory`. |
| `playDate` | date | Yes | Trong khoảng `[today, today+7]` (cố định toàn hệ thống — BR-RES-08, G2 fix 2026-09-01). |
| `preferredStartTime` | time | Yes | `HH:mm`. Phải `>= CafeSchedule.OpenTime` và `> now()` (G11 fix 2026-09-01). |
| `preferredEndTime` | time | Yes | `HH:mm`. Phải > `preferredStartTime`. Nếu `> preferredStartTime` → cùng ngày; nếu `<` → hiểu là ngày kế tiếp (overnight). **KHÔNG ĐƯỢC** bỏ trống / mặc định `00:00` (G5 fix 2026-09-01). Nếu `==` → 400. |
| `minPlayers` | int | Yes | ≥ 1 (cho phép solo play). |
| `maxPlayers` | int | Yes | `minPlayers ≤ maxPlayers`. |
| `isPrivate` | bool | No | `false` = public lobby (có thể cần cafe duyệt). `true` = private lobby (mời bạn, không cần cafe duyệt). Mặc định `false`. |
| `idempotencyKey` | string | Yes | 8–128 ký tự. Idempotent theo key. |

### Validation chain (BR-RES-07/08/09 + G2/G5/G7/G8/G11/G13 — fix 2026-09-01)

Server validate theo thứ tự (fail sớm nhất):

1. **Cafe mở cửa ngày đó** (`CafeSchedule.IsClosed`) → 400.
2. **Preferred times nằm trong giờ mở/đóng thực tế** (`CafeScheduleValidator.ValidatePreferredTimesWithCafeScheduleAsync`, xử lý overnight) → 400 (G3 fix).
3. **`playDate` trong [today, today+7]** → 400 (G2 fix — `MaxAdvanceBookingDays = 7`).
4. **`scheduledStartTime > now()`** → 400 (`StartTimeInPast`) (G11 fix).
5. **`scheduledEndTime > scheduledStartTime`** → 400 (`PreferredTimesMustDiffer`).
6. **Duration ≤ 12 giờ** → 400 (`DurationTooLong`) (G7 fix).
7. **Duration ≥ 30 phút** → 400 (`DurationTooShort`) (G8 fix).
8. **Overnight rule**: nếu overnight, `scheduledEndTime.Date == scheduledStartTime.Date + 1` → 400 (`LateNightMustEndNextDay`).
9. **Same-day rule**: nếu không overnight, `scheduledEndTime.Date == scheduledStartTime.Date` → 400 (`ReservationEndTimeDifferentDay`).
10. **Defensive assertion**: `DateOnly.FromDateTime(scheduledStartTime) == playDate` (G13 fix — `Debug.Assert` trong dev).

### Response 200

```json
{
  "statusCode": 200,
  "message": "ReservationQuoteCreated",
  "data": {
    "reservationId": null,
    "cafeId": "...",
    "gameId": "...",
    "playDate": "2026-08-04",
    "timeSlot": "evening",
    "preferredStartTime": "19:30:00",
    "scheduledStartTime": "2026-08-04T18:00:00Z",
    "scheduledEndTime": "2026-08-04T23:00:00Z",
    "recruitmentDeadline": "2026-08-04T17:40:00Z",
    "minPlayers": 4,
    "maxPlayers": 6,
    "cafeBasePriceVnd": 50000,
    "finalDeposit": 60,
    "currentBalance": 50000,
    "missingAmount": 0,
    "bufferMinutes": 240,
    "bufferWarning": false,
    "requiresCafeApproval": false,
    "riskLevel": "low",
    "expiresAt": "2026-08-02T11:00:00Z",
    "warnings": []
  }
}
```

### Deposit breakdown

FE hiển thị breakdown cho người chơi (2026-08-27 — chỉ render `FinalDeposit` + `CafeBasePriceVnd`, không hiển thị công thức % nữa):

```
Tiền cọc: {FinalDeposit} BVC
Giá vé cơ bản: {CafeBasePriceVnd:N0}đ / người
Số người tối đa: {MaxPlayers}
```

Với ví dụ trên (cafeBasePrice = 50.000đ, 6 người):
- `cafeBasePriceVnd = 50.000đ`
- `maxPlayers = 6`
- `finalDeposit = 60 BVC` (= 20% × 50.000đ × 6 người = 60.000đ tổng cọc)

> **Lưu ý (2026-08-27):** Các field `DepositPercentage`, `DepositPerPerson`, `DepositRatePerPerson`, `BaseDeposit`, `RiskMultiplier`, `MinDepositApplied` không còn được trả về cho FE nữa (giữ trong response với giá trị mặc định = 0 cho backward compat). FE chỉ cần `FinalDeposit` + `CafeBasePriceVnd` + `MaxPlayers` để hiển thị breakdown.

### Quote warnings

Trường `warnings` chứa các cảnh báo từ server:

| Warning code | Message | Action |
|---|---|---|
| `BUFFER_60_120` | Buffer chỉ còn 60-120 phút. Khuyến nghị chọn thời gian sớm hơn. | UI hiển thị warning |
| `NEAR_CAPACITY` | Cafe gần đầy ghế cho khung giờ này. | UI hiển thị warning |
| `RISK_MEDIUM` | Tài khoản có mức rủi ro trung bình. Tiền cọc nhân 1.25x. | UI hiển thị warning |
| `RISK_HIGH` | Tài khoản có mức rủi ro cao. Tiền cọc nhân 1.5x. | UI cảnh báo mạnh |
| `NEAR_MAX_LOBBIES` | Đã gần đạt giới hạn lobby/ngày. | UI hiển thị warning |

### Risk level trong quote

| Risk Level | riskMultiplier | Mô tả |
|---|---|---|
| `low` | 1.0 | Bình thường |
| `medium` | 1.25 | 2-3 lobby fail trong 30 ngày |
| `high` | 1.5 | ≥4 lobby fail hoặc từng no-show |
| `critical` | 2.0 | Cooling-off hoặc tài khoản bị hạn chế |
```

### Lỗi thường gặp

| Status | Message rule | BR |
|---|---|---|
| `400` | `playDate` ngoài `[today, today+7]` (cố định hệ thống — `MaxAdvanceBookingDays = 7`) | BR-RES-08 (G2 fix 2026-09-01) |
| `400` | `minPlayers < 1` hoặc `maxPlayers > 30` | - |
| `400` | `maxPlayers < minPlayers` | - |
| `400` | `playDate < today` (ngày trong quá khứ) | BR-RES-08 (G2 fix 2026-09-01) |
| `400` | `PreferredStartBeforeOpen(<openTime>)` — start trước giờ mở cafe | BR-RES-07 (G3 fix) |
| `400` | `PreferredEndAfterClose(<closeTime>)` — end sau giờ đóng cafe | BR-RES-07 (G3 fix) |
| `400` | `PreferredTimesMustDiffer` (end == start, zero-duration) | BR-RES-07 |
| `400` | `PreferredEndTimeRequired` (endTime bị default = 00:00 hoặc thiếu) | BR-RES-07 (G5 fix 2026-09-01) |
| `400` | `StartTimeInPast` (`scheduledStartTime <= now()`) | BR-RES-07 (G11 fix 2026-09-01) |
| `400` | `DurationTooLong(12)` — duration > 12 giờ | BR-RES-07 (G7 fix 2026-09-01) |
| `400` | `DurationTooShort(30)` — duration < 30 phút | BR-RES-07 (G8 fix 2026-09-01) |
| `400` | `LateNightMustEndNextDay` — overnight nhưng endDate != startDate+1 | BR-NEW-15 |
| `400` | `ReservationEndTimeDifferentDay` — same-day nhưng endDate khác startDate | BR-RES-08 |
| `400` | `CafeScheduleClosedForPlayDate` — cafe đóng ngày đó | - |
| `400` | Buffer < 60 phút (từ chối) | BR-LOBBY-01b |
| `400` | Buffer 60-120 phút (cảnh báo) | BR-LOBBY-01c |
| `401` | Thiếu token | - |
| `403` | User bị `suspended` / `banned` | BR-RISK-04 |
| `403` | Cooling-off active (chỉ tạo lobby trong ngày, cọc ×2) | BR-NEW-10 |
| `403` | User đang là member của lobby active (không được host) | BR-USER-LIMIT-04 |
| `403` | User đang là host của 1 lobby (không được host thêm) | BR-USER-LIMIT-01 |
| `409` | Đã có lobby overlap (lịch chồng lấn +30 phút) | BR-USER-LIMIT-02 |
| `409` | Đã host lobby `playDate+cafe+slot` | BR-NEW-08 |
| `409` | Vượt 5 lần tạo/hủy / `playDate` | BR-NEW-05 |
| `409` | Đã có lobby active cho user / `playDate` | BR-NEW-02 |
| `409` | Vượt cap tổng `heldBalance` (500k thường, 1tr VIP, 200k risk cao) | BR-USER-LIMIT-03 |
| `404` | Cafe không tồn tại / không active | - |
| `404` | Game không có trong `CafeGameInventory` | - |

---

## POST /confirm

Confirm reservation — atomic transaction. Trừ BVC + giữ seat + giữ game + insert Reservation + insert Lobby.

### Request

- Method: `POST`
- Path: `/api/v1/reservations/confirm`
- Auth: Player (JWT)

### Body

```json
{
  "cafeId": "guid",
  "gameId": "guid",
  "playDate": "2026-08-04",
  "preferredStartTime": "19:30",
  "preferredEndTime": "22:00",
  "minPlayers": 4,
  "maxPlayers": 6,
  "isPrivate": false,
  "expectedFinalDeposit": 100000,
  "idempotencyKey": "confirm-abc-12345"
}
```

Lưu ý: `expectedFinalDeposit` phải khớp với `finalDeposit` từ quote. Server validate lại và reject nếu trong lúc chờ xác nhận giá thay đổi (BR §XVII.2).

### Response 201

```json
{
  "statusCode": 201,
  "message": "ReservationConfirmed",
  "data": {
    "reservationId": "...",
    "lobbyId": "...",
    "recruitmentDeadline": "2026-08-04T17:40:00Z",
    "requiresCafeApproval": false,
    "cafeApprovalDeadline": null,
    "heldBvc": 100000
  }
}
```

> Server trả `201 Created` (RFC 7231) vì endpoint tạo mới `Reservation` + `Lobby`. Idempotent retry với cùng params + `IdempotencyKey` cũng trả `201` với cùng payload (xem mục Idempotency bên dưới).

Nếu `requiresCafeApproval: true` (public lobby, playDate > 2 ngày), lobby ở `PendingCafeApproval` cho đến khi cafe duyệt hoặc hết 24h (`expiredByCafe` + refund 100% BVC).
Nếu `isPrivate: true`, lobby không cần cafe duyệt dù playDate cách xa.

### Lỗi thường gặp

| Status | Message rule | BR |
|---|---|---|
| `400` | `expectedFinalDeposit` khác server (quote cũ) | BR §XVII.2 |
| `400` | `PreferredStartBeforeOpen` / `PreferredEndAfterClose` (preferred times không khớp `CafeSchedule`) | BR-RES-07 (G3 fix) |
| `400` | `StartTimeInPast` / `DurationTooLong(12)` / `DurationTooShort(30)` / `PreferredEndTimeRequired` | BR-RES-07 (G fix 2026-09-01) |
| `400` | Buffer < 60 phút | BR-LOBBY-01b |
| `400` | Insufficient `availableBalance` | - |
| `401` | Thiếu token | - |
| `403` | User không đủ điều kiện (BR-USER-LIMIT-*) | BR-USER-LIMIT |
| `404` | Cafe/Game không tồn tại | - |
| `409` | `IdempotencyKey` đã dùng cho user khác | BR §XVII.1 |
| `409` | `IdempotencyKey` đã dùng với params khác (params mismatch) | BR §XVII.1 |
| `409` | Cafe hết chỗ (`AvailableSeats < maxPlayers`) | BR-RESERVATION-01 |
| `409` | Cafe hết game copy | BR-RESERVATION-02 |
| `500` | Lỗi hệ thống (xem server logs) | - |

### Private lobby (BR-LOBBY-PRIVACY-*)

| Field | Mô tả |
|---|---|
| `isPrivate: false` | Public lobby — xuất hiện trong search, có thể cần cafe duyệt nếu playDate > 2 ngày |
| `isPrivate: true` | Private lobby — không xuất hiện trong search, chỉ thành viên invited hoặc có share code mới thấy |

**Share code**: 6 ký tự alphanumeric uppercase (ví dụ `K7H3NP9X`). Chỉ thành viên active mới xem được.

**Cafe duyệt**: Public lobby với `playDate > 2 ngày` → `PendingCafeApproval` (chờ 24h). Private lobby luôn bypass cafe duyệt.

### Cooling-off (BR-NEW-10)

Khi active:

- User **không được tạo lobby** có `playDate > 1 ngày`
- Cọc **nhân ×2** (kết hợp với `riskMultiplier`)
- Thời hạn **30 ngày**

Trigger: 3 lobby fail (`timeoutFailed` hoặc `hostCancelled` sau grace) trong 7 ngày.

### Overnight Reservations (BR-RES-08)

`POST /quote` và `POST /confirm` chấp nhận khung giờ qua đêm: nếu `preferredEndTime < preferredStartTime`, hệ thống hiểu rằng `scheduledEndTime` thuộc ngày kế tiếp của `playDate`.

| Input | Interpretation |
|---|---|
| `preferredStartTime=21:00`, `preferredEndTime=00:00` | 21:00 hôm nay → 00:00 ngày kế tiếp (3 giờ) |
| `preferredStartTime=22:00`, `preferredEndTime=02:00` | 22:00 hôm nay → 02:00 ngày kế tiếp (4 giờ) |
| `preferredStartTime=19:00`, `preferredEndTime=21:00` | 19:00 hôm nay → 21:00 cùng ngày (2 giờ) |
| `preferredStartTime=10:00`, `preferredEndTime=10:00` | **400** — zero-duration, không hợp lệ |
| `preferredStartTime=05:00`, `preferredEndTime=08:00` | **400** — `preferredStartTime < DefaultOpenTime` (06:00) |
| `preferredStartTime=20:00`, `preferredEndTime=23:30` (same day) | **400** — `preferredEndTime > DefaultCloseTime` (23:00) |

Response trả `scheduledStartTime` + `scheduledEndTime` đầy đủ `DateTime` (kèm ngày thực tế), ví dụ:

```json
{
  "playDate": "2026-08-18",
  "preferredStartTime": "21:00:00",
  "preferredEndTime": "00:00:00",
  "scheduledStartTime": "2026-08-18T21:00:00Z",
  "scheduledEndTime": "2026-08-19T00:00:00Z",
  "durationMinutes": 180
}
```

Lỗi thường gặp thêm (BR-RES-07/08):

| Status | Message rule | BR |
|---|---|---|
| `400` | `PreferredTimesMustDiffer` (end == start, hoặc zero-duration) | BR-RES-07 |
| `400` | `PreferredStartBeforeOpen(<openTime>)` | BR-RES-07 (G3 fix 2026-09-01 — dùng `CafeSchedule.OpenTime` thực tế, không phải hardcoded 06:00) |
| `400` | `PreferredEndAfterClose(<closeTime>)` khi không overnight | BR-RES-07 (G3 fix) |
| `400` | `ReservationEndTimeDifferentDay` (end > start nhưng lệch sang ngày khác) | BR-RES-08 |
| `400` | `StartTimeInPast` (`scheduledStartTime <= now()`) | BR-RES-07 (G11 fix 2026-09-01) |
| `400` | `DurationTooLong(12)` — duration vượt 12 giờ | BR-RES-07 (G7 fix 2026-09-01) |
| `400` | `DurationTooShort(30)` — duration dưới 30 phút | BR-RES-07 (G8 fix 2026-09-01) |
| `400` | `PreferredEndTimeRequired` (endTime bị default / thiếu) | BR-RES-07 (G5 fix 2026-09-01) |

### Idempotency strict params (fix 2026-08-06)

Confirm endpoint **verify tất cả params** trước khi trả kết quả cũ:

- `CafeId`, `GameId`, `PlayDate`
- `PreferredStartTime`, `PreferredEndTime`
- `MaxPlayers`, `MinPlayers`
- `ExpectedFinalDeposit`

| Trường hợp | Hành vi |
|---|---|
| Retry với **cùng params** | ✅ 201 — trả kết quả cũ |
| Retry với **params khác** | ❌ 409 — `IdempotencyKeyParamsMismatch` |
| Lobby cũ đã bị hủy | ✅ 201 — vẫn trả kết quả cũ (client cần dùng key mới) |

**Ví dụ lỗi params mismatch:**

```json
{
  "statusCode": 409,
  "message": "IdempotencyKey 'abc123' đã được dùng cho reservation khác. " +
             "Các tham số không khớp: TimeSlot (existing=Afternoon, request=Morning), " +
             "MaxPlayers (existing=4, request=6). Dùng IdempotencyKey mới."
}
```

### Atomic transaction (BR §17.4)

`BeginTransactionAsync(Serializable)` → `SELECT FOR UPDATE` seat_inventory + game_inventory → `HoldDepositAsync` (trừ BVC + ghi ledger) → INSERT reservation (Holding) → INSERT lobby (`PendingCafeApproval` / `PendingActivation`) → Update Inventory counters → **Nếu lobby ở `PendingActivation` thì promote sang `Open` ngay trong transaction** → Insert 3 Outbox events → `SaveChangesAsync` → `CommitAsync`. Nếu throw → `RollbackAsync`.

> **Quan trọng (fix 2026-08-05):** Lobby `PendingActivation` được auto-promote sang `Open` ngay trong cùng transaction `ConfirmAsync` để tránh trạng thái stuck. Trước đây, lobby được insert với `PendingActivation` và chờ Outbox publisher promote — nhưng `LoggingOutboxPublisher` chỉ log, không update DB → lobby bị stuck vĩnh viễn, host không thể invite/join.
>
> Lobby `PendingCafeApproval` thì **giữ nguyên** — chờ `HandleCafeApprovalAsync` xử lý.

> **Fix 2026-08-06:** Nếu wallet của user chưa tồn tại trong database, hệ thống sẽ tự động tạo wallet mới với `AvailableBalance = 0` trước khi thực hiện `HoldDepositAsync`. Điều này xử lý trường hợp user chưa từng nạp BVC trước đó.

---

## POST /{id}/cancel

Host hủy reservation. Refund theo BR-REFUND-02.

### Request

- Method: `POST`
- Path: `/api/v1/reservations/{reservationId}/cancel`
- Auth: Player (JWT) — chỉ host mới được hủy

### Body

```json
{ "reason": "Hủy vì thay đổi kế hoạch" }
```

### Refund policy (BR-REFUND-02)

| Điều kiện | Hoàn BVC | Karma |
|---|---|---|
| Trong grace 15p + chưa có member | 100% | Không phạt |
| ≥ 24 giờ trước `ScheduledStartTime` | 100% | Không phạt |
| < 24 giờ trước `ScheduledStartTime` | 0% | Giảm −10 (HostDissolve violation, GAP-4 fix 2026-08-27) |

> **Lưu ý:** Không còn bậc 50% (6-24h) nữa. Chỉ có 100% (grace/≥24h) hoặc 0% (<24h).
> **HostDissolve (GAP-4 2026-08-27):** Khi host dissolve lobby (`DELETE /api/v1/lobbies/{id}`) ngoài grace, `PlayerKarmaService.RecordHostDissolveAsync` ghi `KarmaShortPlayRecord` với `ViolationType = HostDissolve`, `ReservationId` được set. Karma aggregation chạy `TriggerKarmaAggregationAsync` sau persist → warning (3-4 violations) hoặc restriction (5+) theo BR-KARMA-03.

**H7 Fix (BR-REFUND-03 hasMembers, 2026-08-09):**
- Điều kiện "chưa có member" check `members.Any(m => !m.IsHost && m.IsActive)` thay vì `members.Count > 1`.
- Trước fix: đếm tổng row → false positive khi host có 2 row hoặc soft-delete không đúng.
- Logic chính xác: "thành viên tham gia" = non-host & active.
- File: `ReservationService.cs` dòng ~933.

### Response 200

```json
{
  "statusCode": 200,
  "message": "ReservationCancelled",
  "data": {
    "reservationId": "...",
    "lobbyId": "...",
    "refundBvc": 50000,
    "forfeitBvc": 50000,
    "refundPolicyApplied": "Cancel-6h"
  }
}
```

### Lỗi thường gặp

| Status | Message |
|---|---|
| `400` | Reservation không ở `Holding` |
| `401` | Thiếu token |
| `403` | Không phải host |
| `404` | Reservation không tồn tại |
| `500` | Thiếu lobby (data integrity) |

---

## POST /{id}/cafe-approval

Cafe **chấp nhận** (`approve: true`) hoặc **từ chối** (`approve: false`) lobby đang chờ (BR-NEW-11).

### Request

- Method: `POST`
- Path: `/api/v1/reservations/{reservationId}/cafe-approval`
- Auth: Cafe Manager — phải là `cafe.ManagerId` của reservation.

### Body

**Chấp nhận lobby:**

```json
{ "approve": true, "reason": null }
```

**Từ chối lobby:**

```json
{ "approve": false, "reason": "Quán đông, không nhận thêm khách" }
```

| Field | Required | Description |
|-------|----------|-------------|
| `approve` | ✅ | `true` = chấp nhận, `false` = từ chối |
| `reason` | ❌ | Lý do từ chối (ghi vào audit log) |

### Response 200

**Khi chấp nhận:**

```json
{
  "statusCode": 200,
  "message": "ReservationCafeApproved",
  "data": {
    "reservationId": "...",
    "lobbyId": "...",
    "lobbyStatus": "Open",
    "approved": true,
    "refundBvc": 0
  }
}
```

**Khi từ chối:**

```json
{
  "statusCode": 200,
  "message": "ReservationCafeRejected",
  "data": {
    "reservationId": "...",
    "lobbyId": "...",
    "lobbyStatus": "RejectedByCafe",
    "approved": false,
    "refundBvc": 120000
  }
}
```

> Khi từ chối: lobby chuyển sang `RejectedByCafe`, refund **100% BVC** về ví host.

### Lỗi thường gặp

| Status | Message |
|---|---|
| `400` | Lobby không ở `PendingCafeApproval` |
| `401` | Thiếu token |
| `403` | Không phải manager của cafe |
| `404` | Reservation không tồn tại |

---

## POST /{id}/check-in

POS staff xác nhận khách đã đến quán, scan mã ReservationCode để bắt đầu phiên chơi (BR-CHECKIN-01).

### Request

- Method: `POST`
- Path: `/api/v1/reservations/{reservationId}/check-in`
- Auth: Manager hoặc CafeStaff.

### Body

```json
{
  "cafeId": "guid",
  "reservationCode": "8-char-code",
  "activeSessionId": "guid",
  "idempotencyKey": "pos-checkin-..."
}
```

| Field | Required | Description |
|---|---|---|
| cafeId | Yes | CafeId của POS staff đang quét — validate ownership |
| reservationCode | Yes | Mã 8-char alphanumeric từ app |
| activeSessionId | Yes | POS session ID |
| idempotencyKey | Yes | 8-128 chars; retry trả cùng response |

### Response — 200 OK

```json
{
  "reservationId": "guid",
  "lobbyId": "guid",
  "activeSessionId": "guid",
  "reservationStatus": "CheckedIn",
  "lobbyStatus": "InProgress",
  "checkedInAt": "2026-08-15T10:30:00Z",
  "heldBvc": 120
}
```

### Errors

| Code | Description |
|---|---|
| 400 | Mã reservation không hợp lệ hoặc đã check-in (idempotent) |
| 404 | Không tìm thấy reservation |
| 409 | Status không cho phép check-in (ví dụ: đã `Completed` / `Cancelled`) |

---

## POST /{id}/end

POS staff kết thúc phiên chơi (early checkout, on-time, hoặc staff override). BR-END-01..05 + BR-REFUND-05 + EC-09.

### Request

- Method: `POST`
- Path: `/api/v1/reservations/{reservationId}/end`
- Auth: Manager hoặc CafeStaff.

### Body

```json
{
  "reservationId": "guid",
  "actualEndAt": "2026-08-15T13:30:00Z",
  "reason": "staff_manual_close",
  "skipWalkInWindow": false
}
```

| Field | Required | Description |
|---|---|---|
| reservationId | Yes | Reservation cần end |
| actualEndAt | No | Default = UTC now |
| reason | No | "early_checkout" / "on_time" / "staff_override" |
| skipWalkInWindow | No | Skip tạo WalkInWindow khi early checkout |

### Response — 200 OK

```json
{
  "reservationId": "guid",
  "endReason": "EarlyLeave",
  "playedRatio": 0.43,
  "originalDeposit": 120,
  "refundBvc": 0,
  "forfeitBvc": 120,
  "refundReason": "EarlyCheckout",
  "checkedInAt": "2026-08-15T10:00:00Z",
  "actualEndAt": "2026-08-15T11:30:00Z",
  "scheduledStartTime": "2026-08-15T10:00:00Z",
  "scheduledEndTime": "2026-08-15T13:00:00Z",
  "walkInWindowId": null,
  "karmaRecorded": true
}
```

### Errors

| Code | Description |
|---|---|
| 404 | Không tìm thấy reservation |
| 409 | Status không cho phép end (chưa check-in) |

### Side effects

- Update `Reservation.Status = Completed` (hoặc `EarlyCheckout`)
- Update `Reservation.ActualEndAt`, `PlayedRatio`, `EndReason`
- Tính refund theo BR-REFUND-05: ≥90% on-time = 0 refund; ≥50% early = 30% refund; <50% short = 0 refund
- Nếu `playedRatio < 50%` và `duration >= 30 phút` và `!SkipWalkInWindow` → tạo `WalkInWindow` (EC-09)
- Nếu `playedRatio < 50%` → ghi `KarmaShortPlayRecord` (-5 karma)
- Outbox event `SessionEnded`

---

## POST /{id}/extend

Host mở rộng thời gian reservation (BR-EXT-01..05 + EC-05 + EC-08).

### Request

- Method: `POST`
- Path: `/api/v1/reservations/{reservationId}/extend`
- Auth: Host.

### Body

```json
{
  "extensionMinutes": 30,
  "idempotencyKey": "extend-..."
}
```

| Field | Required | Description |
|---|---|---|
| extensionMinutes | Yes | 1-120 phút (max 2 lần extend) |
| idempotencyKey | Yes | 8-128 chars |

### Response — 200 OK

```json
{
  "reservationId": "guid",
  "newScheduledEndTime": "2026-08-15T15:30:00Z",
  "previousEndTime": "2026-08-15T13:00:00Z",
  "extensionCount": 1,
  "extensionMinutes": 30,
  "remainingExtensionMinutes": 90
}
```

### Errors

| Code | Description |
|---|---|
| 403 | Không phải host |
| 404 | Không tìm thấy reservation |
| 409 | Status không phải Confirmed / quá max extension / overlap với WalkInWindow |

---

## POST /{id}/cancel-after-checkin

Host hủy reservation sau khi đã check-in. Áp dụng refund theo playedRatio (BR-REFUND-04/05).

### Request

- Method: `POST`
- Path: `/api/v1/reservations/{reservationId}/cancel-after-checkin`
- Auth: Player (JWT) — phải là host của reservation

### Request Body

```json
{
  "reason": "Trời mưa to không thể tiếp tục chơi",
  "idempotencyKey": "host-cancel-after-checkin-xyz789"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `reason` | string | No | Lý do hủy (hiển thị trong audit log) |
| `idempotencyKey` | string | Yes | Chống duplicate request |

### Response 200

```json
{
  "statusCode": 200,
  "message": "ReservationCancelledAfterCheckin",
  "data": {
    "reservationId": "guid",
    "previousStatus": "CheckedIn",
    "newStatus": "CancelledByPlayer",
    "playDurationMinutes": 45,
    "playedRatio": 0.75,
    "refundBvc": 30,
    "forfeitBvc": 70,
    "refundReason": "Hoàn 30% tiền cọc (playedRatio ≥ 50%)",
    "cancellationType": "PartialForfeit",
    "cancelledAt": "2026-08-15T11:00:00Z"
  }
}
```

### playedRatio và refund

| playedRatio | Hoàn BVC | Karma |
|------------|----------|-------|
| `< 50%` | 0% (forfeit 100%) | Giảm nhẹ |
| `≥ 50%` | 30% | Không phạt |
| `≥ 90%` | 0% (treated as on-time) | Không phạt |

### Lifecycle metadata (BR-END-02, fix 2026-08-27)

Khi staff nhấn Pay tại POS → `ActiveSessionService.PaySessionAsync` gọi `ReservationService.CompleteAndCaptureAsync`, hệ thống **BẮT BUỘC** populate 3 field lifecycle metadata trên Reservation row:

- `ActualEndAt`: timestamp thực tế đóng session.
- `PlayedRatio`: `clamp((actualEndAt - checkedInAt) / (scheduledEndTime - scheduledStartTime), 0, 1)`.
- `EndReason`: `EarlyLeave` / `OnTime` / `StaffOverride` (dựa trên `PlayedRatio` thresholds ở trên).

**Bug đã fix (2026-08-27):** Trước fix, `ExecuteCompleteAndCaptureTransactionAsync` chỉ flip `Status = Completed` mà KHÔNG set `ActualEndAt`, `PlayedRatio`, `EndReason`. Audit/karma/refund reports đọc NULL → "bàn tự dưng closed". Fix được bảo vệ bởi `ReservationCompleteCaptureFixTests` (9 test cases cover edge cases: zero-duration, negative ratio, > 100%, missing `CheckedInAt`, 90% boundary, 50% boundary).

**Side effect:** Nếu `CheckedInAt` cũng NULL (bug upstream trong `ExecuteCheckInTransactionAsync` step 9 — fixed trong cùng change-set), `EndAndSettleAsync` sẽ throw. Test `MissingCheckedInAt_FallbackToScheduledStart_AvoidsDivideByZero` đảm bảo fallback `checkedInAt ?? scheduledStart` để tránh chia cho 0.

### Lỗi

| Code | Mô tả |
|------|--------|
| 400 | Request không hợp lệ / thiếu idempotencyKey |
| 401 | Thiếu token |
| 403 | Không phải host |
| 404 | Không tìm thấy reservation |
| 409 | Reservation chưa check-in hoặc đã ở trạng thái terminal |

### Idempotency

Retry với cùng `idempotencyKey` trả cùng kết quả. Server check `ReservationActionAudit` trước khi xử lý.

---

## Bypass time-window (Dev/QA convenience)

BoardVerse cung cấp cờ bypass cho phép Dev/QA test các ràng buộc thời gian mà không bị chặn bởi deadline thực tế.

### Áp dụng cho các endpoint sau

| Endpoint | Check bị bypass | Operation key |
|----------|-----------------|---------------|
| `POST /confirm`, `POST /quote` | Time-slot buffer, deadline past | `Lobby.*` |
| `POST /{id}/check-in` (POS) | Check-in window (± grace) | `PlayerCheckIn.Window`, `Reservation.CheckInWindow` |
| `POST /{id}/cancel` | Refund milestones (24h/6h → 100% always) | `Reservation.RefundMilestone` |
| `POST /{id}/cancel-after-checkin` | playedRatio thresholds | `Reservation.RefundMilestone` |
| (background) `ReservationNoShowDetectionJob` | No-show detection grace | `ReservationNoShowDetectionJob` |

### Ba cách bật bypass (ưu tiên từ cao xuống thấp)

1. **HTTP header** `X-Bypass-Time-Window: true` — chỉ áp dụng cho 1 request.
2. **Query string** `?bypassTimeWindow=true` — chỉ áp dụng cho 1 request.
3. **DB config** `bypass_time_window_validations=true` — áp dụng toàn cục sau ≤ 10s.

### Ví dụ

```bash
# Bật bypass toàn cục (cần Admin role)
curl -X POST https://api.boardverse.dev/api/v1/admin/configs/bypass-time-window \
  -H "Authorization: Bearer <admin-token>"

# Sau đó test check-in ngoài khung giờ thoải mái
curl -X POST https://api.boardverse.dev/api/v1/reservations/{id}/check-in \
  -H "Authorization: Bearer <staff-token>" \
  -H "Content-Type: application/json" \
  -d '{ "arrivedMemberIds": [...] }'

# Hoặc dùng header cho 1 request duy nhất
curl -X POST https://api.boardverse.dev/api/v1/reservations/{id}/check-in \
  -H "Authorization: Bearer <staff-token>" \
  -H "X-Bypass-Time-Window: true" \
  -d '{ "arrivedMemberIds": [...] }'

# Tắt bypass
curl -X DELETE https://api.boardverse.dev/api/v1/admin/configs/bypass-time-window \
  -H "Authorization: Bearer <admin-token>"
```

Xem chi tiết: [admin-configuration.md](./admin-configuration.md#bypass-time-window-devqa-convenience).

> 💡 **Admin check nhanh**: Xem trạng thái `bypass_time_window_validations` (yêu cầu JWT Admin token) — dùng endpoint:
> ```bash
> curl https://api.boardverse.dev/api/v1/system-configs/bypass_time_window_validations \
>   -H "Authorization: Bearer <admin-token>"
> # → { ..., "inferredType": "bool", "parsedValue": true|false }
> ```
> Xem [system-config.md](./system-config.md).

---

## Luồng tích hợp

```
Client                    ReservationController                ReservationService
  │                              │                                  │
  │ POST /quote                 │                                  │
  ├─────────────────────────────►│ CreateQuoteAsync                 │
  │                              ├─────────────────────────────────►│ validate + tính quote
  │                              │◄─────────────────────────────────┤ ReservationQuoteDto
  │◄─────────────────────────────┤                                  │
  │ {finalDeposit, missingAmount}│                                  │
  │                              │                                  │
  │ (nếu missingAmount > 0)      │                                  │
  │ POST /wallet/topup (VNPay)   │                                  │
  │                              │                                  │
  │ POST /confirm                │                                  │
  ├─────────────────────────────►│ ConfirmAsync                     │
  │                              ├─────────────────────────────────►│ BEGIN TRANSACTION
  │                              │                                  │ SELECT FOR UPDATE seat + game
  │                              │                                  │ HoldDepositAsync (BVC + ledger)
  │                              │                                  │ INSERT reservation
  │                              │                                  │ INSERT lobby
  │                              │                                  │ UPDATE inventory counters
  │                              │                                  │ COMMIT
  │                              │◄─────────────────────────────────┤
  │◄─────────────────────────────┤ {reservationId, lobbyId, ...}    │
  │                              │                                  │
  │ (outbox)                     │                                  │ Worker → SignalR LobbyActivated
```

---

## State machine

### Reservation states

```text
draft
 → awaitingDeposit (chờ khóa BVC / top-up)
 → holding (đã khóa BVC, lobby đang tuyển)
 → confirmed (đạt minPlayers trước deadline)
 → expired (deadline trôi qua, không đủ người)
 → checkedIn (POS quét QR check-in)
 → completed (POS đóng phiên chơi)
 → cancelledByPlayer | cancelledByCafe | noShow
```

### Lobby states (12 trạng thái đầy đủ)

```text
pendingActivation (txn atomic đang xử lý, lobby chưa publish)
 → pendingCafeApproval (public lobby > 2 ngày, chờ cafe duyệt)
 → open (đang tuyển, recruitmentDeadline chưa tới)
 → viable (đạt minPlayers, vẫn nhận thêm)
 → full (đạt maxPlayers, ngừng nhận)
 → inProgress (đã check-in tại quán)
 → closed (phiên kết thúc)
 → timeoutFailed (deadline trôi qua, không đủ người)
 → hostCancelled (host hủy chủ động)
 → rejectedByCafe (cafe từ chối duyệt)
 → expiredByCafe (cafe không duyệt trong 24 giờ)
```

### Transition rules

| Trigger | Current State | Next State | Action |
|---|---|---|---|
| Confirm success (playDate ≤ 2 ngày) | - | `open` | Tạo reservation + lobby |
| Confirm success (playDate > 2 ngày, public) | - | `pendingCafeApproval` | Chờ cafe duyệt 24h |
| Cafe approve | `pendingCafeApproval` | `open` | Lobby publish |
| Cafe reject | `pendingCafeApproval` | `rejectedByCafe` | Refund 100% BVC |
| 24h timeout | `pendingCafeApproval` | `expiredByCafe` | Refund 100% BVC |
| Join → currentPlayers ≥ minPlayers | `open` | `viable` | Booking → confirmed |
| Join → currentPlayers = maxPlayers | `open` | `full` | Đóng tuyển |
| Check-in | `viable`/`full` | `inProgress` | Tạo ActiveSession |
| Check-in | `open` | `inProgress` | Tạo ActiveSession |
| Session paid | `inProgress` | `closed` | Settlement |
| Deadline + insufficient players | `open`/`viable` | `timeoutFailed` | Refund 100% BVC |
| Host cancel (grace 15p, no members) | `open` | `hostCancelled` | Refund 100% BVC |
| Host cancel (≥24h before) | `open` | `hostCancelled` | Refund 100% BVC |
| Host cancel (6-24h) | `open` | `hostCancelled` | Refund 50% BVC |
| Host cancel (<6h) | `open` | `hostCancelled` | Forfeit 0% BVC |

---

## Idempotency (BR §XVII.1)

Mọi request phải có `IdempotencyKey` 8–128 ký tự. Retry với cùng key trả cùng kết quả.

### Confirm endpoint (strict params validation)

Confirm verify **tất cả params** trước khi trả kết quả cũ:

| Request params verified | Mục đích |
|---|---|
| `CafeId` | Đảm bảo đúng cafe |
| `GameId` | Đảm bảo đúng game |
| `PlayDate` | Đảm bảo đúng ngày |
| `TimeSlot` | Đảm bảo đúng khung giờ |
| `MaxPlayers` | Đảm bảo đúng số người |
| `MinPlayers` | Đảm bảo đúng số người tối thiểu |
| `ExpectedFinalDeposit` | Đảm bảo đúng số tiền cọc |

### Idempotency matrix

| Endpoint | Idempotency field | Cùng params | Khác params |
|---|---|---|---|
| `POST /quote` | `idempotencyKey` | ✅ 200 (server không cache) | ✅ 200 (quote chỉ tính toán) |
| `POST /confirm` | `idempotencyKey` | ✅ 201 (trả kết quả cũ) | ❌ 409 (params mismatch) |
| `POST /cancel` | `reservationId` + `updatedAt` | ✅ 200 | ✅ 200 (idempotent by design) |
| `POST /cafe-approval` | `lobby.status` | ✅ 200 | ✅ 200 (chỉ xử lý 1 lần) |

### Error codes

| Code | Message | Trigger |
|---|---|---|
| `409` | `IdempotencyKeyConflict` | Cùng key, khác user |
| `409` | `IdempotencyKeyParamsMismatch` | Cùng key, cùng user, khác params |

### Best practices

1. **Dùng key mới** khi muốn tạo reservation mới sau khi lobby cũ bị hủy
2. **Lưu key** ở client để retry khi network fail
3. **Client dedupe**: quote không cần server-side dedupe vì chỉ đọc

---

## GET /{id}/extend/availability

Kiểm tra xem có thể extend reservation không (BR-EXT).

### Request

- Method: `GET`
- Path: `/api/v1/reservations/{reservationId}/extend/availability`
- Auth: Player (JWT) — chỉ host

### Query Parameters

| Param | Type | Required | Description |
|-------|------|----------|-------------|
| `extensionMinutes` | int | Yes | Số phút muốn extend |

### Response 200

```json
{
  "statusCode": 200,
  "message": "ExtendAvailability",
  "data": {
    "reservationId": "guid",
    "currentScheduledEndTime": "2026-08-15T13:00:00Z",
    "requestedExtensionMinutes": 30,
    "newScheduledEndTime": "2026-08-15T13:30:00Z",
    "isAvailable": true,
    "remainingExtensionMinutes": 90,
    "extensionCount": 1,
    "maxExtensionMinutes": 120,
    "reason": null
  }
}
```

### Response khi không khả dụng

```json
{
  "statusCode": 200,
  "message": "ExtendAvailability",
  "data": {
    "reservationId": "guid",
    "isAvailable": false,
    "reason": "Đã đạt số lần extend tối đa (2 lần)"
  }
}
```

### Lỗi

| Code | Mô tả |
|------|--------|
| 401 | Thiếu token |
| 404 | Không tìm thấy reservation |

---

## POST /by-code/{reservationCode}/check-in

POS staff quét QR theo ReservationCode (8-char). Endpoint thay thế cho FE không biết reservationId.

### Request

- Method: `POST`
- Path: `/api/v1/reservations/by-code/{reservationCode}/check-in`
- Auth: Manager, CafeStaff

### Path Parameters

| Param | Type | Description |
|-------|------|-------------|
| `reservationCode` | string | Mã 8-char alphanumeric từ QR code |

### Request Body

```json
{
  "cafeId": "guid",
  "activeSessionId": "guid",
  "idempotencyKey": "pos-checkin-abc123"
}
```

### Response 201

```json
{
  "statusCode": 201,
  "message": "ReservationCheckedIn",
  "data": {
    "reservationId": "guid",
    "lobbyId": "guid",
    "activeSessionId": "guid",
    "reservationStatus": "CheckedIn",
    "lobbyStatus": "InProgress",
    "checkedInAt": "2026-08-15T10:30:00Z"
  }
}
```

### Lỗi

| Code | Mô tả |
|------|--------|
| 400 | Request không hợp lệ |
| 401 | Thiếu token |
| 403 | Không đủ quyền vận hành quán |
| 404 | Không tìm thấy reservation |
| 409 | Reservation không thuộc cafe hoặc đã check-in rồi |

### Idempotency

Scan cùng QR trả cùng response (cùng ActiveSessionId). Key: `pos-checkin:{code}`.
