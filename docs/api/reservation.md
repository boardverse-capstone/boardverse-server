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
- **BR-NEW-11**: `playDate ≥ DistantThresholdDays` (mặc định 2) + lobby **public** → lobby ở `PendingCafeApproval`, chờ cafe duyệt 24h. Lobby **private** không cần cafe duyệt.
- **BR-REFUND-02/03**: Cancel theo mốc 24h/6h + grace 15 phút.
- **BR-RESERVATION-01/02**: Giữ `maxPlayers` ghế + 1 game copy.

---

## Mục lục

- [GET /{id}](#get-id)
- [GET /](#get-)
- [GET /pending-cafe-approval](#get-pending-cafe-approval)
- [GET /{id}/cafe-approval](#get-idcafe-approval)
- [POST /quote](#post-quote)
- [POST /confirm](#post-confirm)
- [POST /{id}/cancel](#post-idcancel)
- [POST /{id}/cafe-approval](#post-idcafe-approval)
- [POST /{id}/check-in](#post-idcheck-in)
- [POST /{id}/end](#post-idend)
- [POST /{id}/extend](#post-idextend)
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
    "createdAt": "2026-08-02T15:30:00Z",
    "updatedAt": "2026-08-02T15:30:00Z"
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
  "timeSlot": "evening",
  "preferredStartTime": "19:30",
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
| `playDate` | date | Yes | Trong khoảng `[today, today+7]`. |
| `timeSlot` | enum | Yes | `morning` / `afternoon` / `evening` / `night`. |
| `preferredStartTime` | time | No | Phải nằm trong `[timeSlot.startTime, timeSlot.endTime]`. |
| `minPlayers` | int | Yes | ≥ 2. |
| `maxPlayers` | int | Yes | `minPlayers ≤ maxPlayers`. |
| `isPrivate` | bool | No | `false` = public lobby (có thể cần cafe duyệt). `true` = private lobby (mời bạn, không cần cafe duyệt). Mặc định `false`. |
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
    "scheduledStartTime": "2026-08-04T18:00:00Z",
    "scheduledEndTime": "2026-08-04T23:00:00Z",
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
    "riskLevel": "low",
    "riskMultiplier": 1.0,
    "expiresAt": "2026-08-02T11:00:00Z",
    "warnings": []
  }
}
```

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
| `400` | `playDate` ngoài [today, +7] | - |
| `400` | `minPlayers < 1` hoặc `maxPlayers > 30` | - |
| `400` | `maxPlayers < minPlayers` | - |
| `400` | `preferredStartTime` không nằm trong `timeSlot` window | BR-LOBBY-15b |
| `400` | Buffer < 60 phút (từ chối) | BR-LOBBY-01b |
| `400` | Buffer 60-120 phút (cảnh báo) | BR-LOBBY-01c |
| `401` | Thiếu token | - |
| `403` | User bị `suspended` / `banned` | BR-RISK-04 |
| `403` | Cooling-off active (chỉ tạo lobby trong ngày, cọc ×2) | BR-NEW-10 |
| `403` | User đang là member của lobby active (không được host) | BR-USER-LIMIT-04 |
| `403` | User đang là host của lobby active (không được join) | BR-USER-LIMIT-05 |
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
  "timeSlot": "evening",
  "preferredStartTime": "19:30",
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

### Idempotency strict params (fix 2026-08-06)

Confirm endpoint **verify tất cả params** trước khi trả kết quả cũ:

- `CafeId`, `GameId`, `PlayDate`, `TimeSlot`
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
- Tính refund theo BR-REFUND-05: ≥90% on-time = 0 refund; 50-89% early = 30% refund; <50% short = 0 refund
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
