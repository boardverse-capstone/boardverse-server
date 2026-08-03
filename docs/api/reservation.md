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
- **BR-DEPOSIT-02..04**: `finalDeposit = ratePerPerson × maxPlayers × riskMultiplier` (nhưng ≥ `minDeposit`).
- **BR-NEW-01**: `maxPlayers` + `minDeposit` theo khoảng cách `playDate`.
- **BR-LOBBY-01a/b**: Buffer ≥ 120 phút OK, 60–120 cảnh báo, < 60 từ chối.
- **BR-USER-LIMIT-01..05**: 1 host lobby + 1 member lobby = tối đa 2 active; cap tổng heldBalance.
- **BR-NEW-02**: 1 lobby active / `playDate` / user.
- **BR-NEW-08**: 1 lobby active / `playDate+timeSlot` / `cafe` / user.
- **BR-NEW-11**: `playDate ≥ DistantThresholdDays` (mặc định 2) → lobby ở `PendingCafeApproval`, chờ cafe duyệt 24h.
- **BR-REFUND-02/03**: Cancel theo mốc 24h/6h + grace 15 phút.
- **BR-RESERVATION-01/02**: Giữ `maxPlayers` ghế + 1 game copy.

---

## Mục lục

- [GET /{id}](#get-id)
- [GET /](#get-)
- [POST /quote](#post-quote)
- [POST /confirm](#post-confirm)
- [POST /{id}/cancel](#post-idcancel)
- [POST /{id}/cafe-approval](#post-idcafe-approval)
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
    "scheduledTime": "2026-08-04T18:00:00Z",
    "recruitmentDeadline": "2026-08-04T17:40:00Z",
    "minPlayers": 4,
    "maxPlayers": 6,
    "currentPlayers": 3,
    "depositAmount": 100000,
    "status": "Holding",
    "lobbyId": "...",
    "lobbyShareCode": "K7H3NP9X",
    "lobbyStatus": "Open",
    "requiresCafeApproval": false,
    "cafeApprovalDeadline": null,
    "createdAt": "2026-08-02T15:30:00Z",
    "updatedAt": "2026-08-02T15:30:00Z"
  }
}
```

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
        "minPlayers": 4,
        "maxPlayers": 6,
        "currentPlayers": 3,
        "depositAmount": 100000,
        "status": "Holding",
        "lobbyId": "...",
        "lobbyShareCode": "K7H3NP9X",
        "lobbyStatus": "Open",
        "isHost": true,
        "createdAt": "2026-08-02T15:30:00Z"
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalItems": 5,
    "hasMore": false
  }
}
```

### Lỗi thường gặp

| Status | Message |
|--------|---------|
| `401` | Thiếu token |
| `400` | `page` hoặc `pageSize` không hợp lệ |

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
  "timeSlot": "evening",
  "preferredStartTime": "19:30",
  "minPlayers": 4,
  "maxPlayers": 6,
  "idempotencyKey": "quote-abc-12345"
}
```

| Field | Type | Required | Values |
|---|---|---|---|
| `cafeId` | guid | Yes | Cafe còn hoạt động. |
| `gameId` | guid | Yes | Game có trong `CafeGameInventory`. |
| `playDate` | date | Yes | Trong khoảng `[today, today+7]`. |
| `timeSlot` | enum | Yes | `morning` / `afternoon` / `evening` / `night`. |
| `preferredStartTime` | time | No | Phải nằm trong `[timeSlot.startTime, timeSlot.endTime]`. |
| `minPlayers` | int | Yes | ≥ 2. |
| `maxPlayers` | int | Yes | `minPlayers ≤ maxPlayers`. |
| `idempotencyKey` | string | Yes | 8–128 ký tự. Idempotent theo key. |

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
    "scheduledTime": "2026-08-04T18:00:00Z",
    "recruitmentDeadline": "2026-08-04T17:40:00Z",
    "minPlayers": 4,
    "maxPlayers": 6,
    "depositRatePerPerson": 5,
    "baseDeposit": 30,
    "riskMultiplier": 1.0,
    "minDepositApplied": 100000,
    "finalDeposit": 100000,
    "currentBalance": 50000,
    "missingAmount": 50000,
    "bufferMinutes": 240,
    "bufferWarning": false,
    "requiresCafeApproval": false,
    "expiresAt": "2026-08-02T11:00:00Z",
    "warnings": []
  }
}
```

### Lỗi thường gặp

| Status | Message rule |
|---|---|
| `400` | `playDate` ngoài [today, +7] |
| `400` | `minPlayers < 2` |
| `400` | `maxPlayers < minPlayers` |
| `400` | `preferredStartTime` không nằm trong `timeSlot` window |
| `400` | Buffer < 60 phút (BR-LOBBY-01b) |
| `401` | Thiếu token |
| `403` | User bị `suspended` / `banned` (BR-RISK-04) |
| `403` | Vượt cap tổng `heldBalance` (BR-USER-LIMIT-03) |
| `404` | Cafe không tồn tại / không active |
| `404` | Game không có trong `CafeGameInventory` |
| `409` | Đã có lobby overlap (BR-USER-LIMIT-02) |
| `409` | Đã host lobby `playDate+cafe+slot` (BR-NEW-08) |
| `409` | Vượt 5 lần tạo/hủy / `playDate` (BR-NEW-05) |
| `409` | Cooling-off (BR-NEW-10) |

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
  "timeSlot": "evening",
  "preferredStartTime": "19:30",
  "minPlayers": 4,
  "maxPlayers": 6,
  "expectedFinalDeposit": 100000,
  "idempotencyKey": "confirm-abc-12345"
}
```

Lưu ý: `expectedFinalDeposit` phải khớp với `finalDeposit` từ quote. Server validate lại và reject nếu trong lúc chờ xác nhận giá thay đổi (BR §XVII.2).

### Response 200

```json
{
  "statusCode": 200,
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

Nếu `requiresCafeApproval: true`, lobby ở `PendingCafeApproval` cho đến khi cafe duyệt hoặc hết 24h (`expiredByCafe` + refund 100% BVC).

### Lỗi thường gặp

| Status | Message rule |
|---|---|
| `400` | `expectedFinalDeposit` khác server (quote cũ) |
| `400` | Buffer < 60 phút |
| `400` | Insufficient `availableBalance` |
| `401` | Thiếu token |
| `403` | User không đủ điều kiện (BR-USER-LIMIT-*) |
| `404` | Cafe/Game không tồn tại |
| `409` | `IdempotencyKey` đã dùng cho user khác |
| `409` | Cafe hết chỗ (`AvailableSeats < maxPlayers`) |
| `409` | Cafe hết game copy |

### Atomic transaction (BR §17.4)

`BeginTransactionAsync(ReadCommitted)` → `SELECT FOR UPDATE` seat_inventory + game_inventory → `HoldDepositAsync` (trừ BVC + ghi ledger) → INSERT reservation (Holding) → INSERT lobby (PendingCafeApproval / PendingActivation) → Update Inventory counters → `SaveChangesAsync` → `CommitAsync`. Nếu throw → `RollbackAsync`.

---

## POST /{id}/cancel

Host hủy reservation. Refund theo BR-REFUND-02/03.

### Request

- Method: `POST`
- Path: `/api/v1/reservations/{reservationId}/cancel`
- Auth: Player (JWT) — chỉ host mới được hủy

### Body

```json
{ "reason": "Hủy vì thay đổi kế hoạch" }
```

### Refund policy (BR §X.2)

| Điều kiện | Hoàn BVC | Karma |
|---|---|---|
| Trong grace 15p + chưa có member | 100% | Không phạt |
| ≥ 24 giờ trước giờ chơi | 100% | Không phạt |
| 6–24 giờ trước giờ chơi | 50% | Giảm nhẹ |
| < 6 giờ trước giờ chơi | 0% | Giảm đáng kể |

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

Cafe duyệt hoặc từ chối lobby đang chờ (BR-NEW-11).

### Request

- Method: `POST`
- Path: `/api/v1/reservations/{reservationId}/cafe-approval`
- Auth: Cafe Manager — phải là `cafe.ManagerId` của reservation.

### Body

```json
{ "approve": true, "reason": null }
```

hoặc từ chối:

```json
{ "approve": false, "reason": "Quán đông, không nhận thêm" }
```

### Response 200

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

Khi từ chối: `lobbyStatus = "RejectedByCafe"`, `refundBvc = finalDeposit`, status reservation → `CancelledByCafe`.

### Lỗi thường gặp

| Status | Message |
|---|---|
| `400` | Lobby không ở `PendingCafeApproval` |
| `401` | Thiếu token |
| `403` | Không phải manager của cafe |
| `404` | Reservation không tồn tại |

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

```text
Reservation:
  draft → Holding → Confirmed → CheckedIn → Completed
                  ↘ Expired         ↘ CancelledByPlayer
                  ↘ CancelledByCafe       ↘ CancelledByCafe
                  ↘ NoShow                ↘ RejectedByCafe
```

Lobby:

```text
PendingActivation → PendingCafeApproval → Open → Viable → Full → InProgress → Closed
                                                                       ↘ TimeoutFailed
                                                                       ↘ HostCancelled
                                                                       ↘ ExpiredByCafe
                                                                       ↘ RejectedByCafe
```

---

## Idempotency (BR §XVII.1)

| Endpoint | Idempotency field |
|---|---|
| `POST /quote` | `idempotencyKey` (request) — server không cache, chỉ client dedupe |
| `POST /confirm` | `idempotencyKey` (request) — server check `Reservation.IdempotencyKey` table; cùng key + cùng user → trả cùng response; khác user → 409 |
| `POST /cancel` | Dựa trên `reservationId` + `updatedAt` (server build idempotency key nội bộ) |
| `POST /cafe-approval` | Dựa trên lobby.status (chỉ xử lý 1 lần khi `PendingCafeApproval`) |
