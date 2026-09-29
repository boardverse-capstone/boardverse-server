# WalkInController

**Base route:** `/api/v1/reservations/walkin`
**Controller:** `WalkInController.cs`
**Role:** POS Staff / Manager / Admin (JWT bearer với quyền vận hành quán).

Walk-in flow cho khách vãng lai không đặt online. Tất cả walk-in bookings **KHÔNG cọc** — thanh toán 100% tiền giờ tại POS.

Đặc điểm:

- **BR-WALKIN-01**: Chỉ tạo walk-in khi `WalkInWindow.Status ∈ {Available, Partial}`.
- **BR-WALKIN-04**: Walk-in KHÔNG cọc — thanh toán 100% tại POS qua SePay/VietQR.
- **BR-WALKIN-05**: OCC trên `WalkInWindow.Version` (EC-06) — chống race condition khi nhiều POS cùng tạo walk-in.
- **WalkInWindow** được tạo tự động khi Reservation checkout sớm (early checkout) hoặc no-show. `WindowEnd` = `Reservation.ScheduledEndTime` (BR-RESV-02).
- WalkInWindow có thời hạn (30 phút mặc định), sau đó tự động đóng bởi `WalkInWindowCleanupJob`.

---

## Mục lục

- [GET /windows](#get-windows)
- [POST /](#post-)
- [POST /windows/{windowId}/close](#post-windowswindowidclose)
- [Luồng tích hợp](#luồng-tích-hợp)

---

## GET /windows

Lấy danh sách WalkInWindow đang trống của 1 cafe + ngày. POS staff gọi trước khi tạo walk-in.

### Request

- Method: `GET`
- Path: `/api/v1/reservations/walkin/windows`
- Auth: POS Staff / Manager / Admin

### Query Parameters

| Name | Type | Required | Description |
|---|---|---|---|
| cafeId | Guid | Yes | Mã quán |
| date | DateOnly (yyyy-MM-dd) | Yes | Ngày muốn xem |

### Response 200

```json
{
  "items": [
    {
      "id": "...",
      "sourceReservationId": "...",
      "windowStart": "2026-08-12T15:30:00Z",
      "windowEnd": "2026-08-12T18:00:00Z",
      "totalSeats": 4,
      "availableSeats": 4,
      "heldSeats": 0,
      "inUseSeats": 0,
      "status": "Available",
      "expiresAt": "2026-08-12T16:00:00Z",
      "createdAt": "2026-08-12T15:30:00Z"
    }
  ]
}
```

### Response 401

Unauthorized — thiếu hoặc token không hợp lệ.

---

## POST /

Tạo WalkInBooking cho khách vãng lai.

### Request

- Method: `POST`
- Path: `/api/v1/reservations/walkin`
- Auth: POS Staff / Manager / Admin

### Request Body

```json
{
  "walkInWindowId": "...",
  "guestName": "Nguyễn Văn B",
  "guestPhone": "0912345678",
  "seats": 3,
  "idempotencyKey": "WI-20260812-001"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| walkInWindowId | Guid | Yes | Mã WalkInWindow muốn đặt |
| guestName | string | Yes | Tên khách (1-200 ký tự) |
| guestPhone | string | No | SĐT khách (≤ 20 ký tự) |
| seats | int | Yes | Số ghế (1-100) |
| idempotencyKey | string | No | Key chống double-tap (8-128 ký tự) |

### Response 201

```json
{
  "id": "...",
  "walkInWindowId": "...",
  "guestName": "Nguyễn Văn B",
  "guestPhone": "0912345678",
  "startTime": "2026-08-12T15:30:00Z",
  "endTime": "2026-08-12T18:00:00Z",
  "seats": 3,
  "hourlyRate": 10000,
  "totalAmount": 75000,
  "paymentStatus": "Unpaid",
  "status": "Active",
  "createdAt": "2026-08-12T15:35:00Z"
}
```

### Response 400

Validation error — thiếu required field hoặc giá trị không hợp lệ.

### Response 404

WalkInWindow không tìm thấy.

### Response 409 Conflict

Window không còn khả dụng, không đủ ghế, hoặc race condition (EC-06).

```json
{
  "statusCode": 409,
  "message": "Window đang được đặt bởi nhân viên khác. Vui lòng thử lại sau.",
  "data": null
}
```

---

## POST /windows/{windowId}/close

Đóng WalkInWindow thủ công bởi POS staff. Window đã đóng không thể nhận walk-in booking mới.

### Request

- Method: `POST`
- Path: `/api/v1/reservations/walkin/windows/{windowId}/close`
- Auth: POS Staff / Manager / Admin

### Request Body (optional)

```json
{
  "reason": "Hết giờ, không có khách"
}
```

### Response 200

```json
{
  "message": "WalkInWindow đã được đóng."
}
```

### Response 404

WalkInWindow không tìm thấy.

---

## Luồng tích hợp

```
POS Staff                              Server
   │                                      │
   │ GET /windows?cafeId=&date=          │
   │ ─────────────────────────────────▶  │
   │ ◀── list WalkInWindowDto ──────────┤
   │                                      │
   │ (Khách vãng lai đến)                │
   │                                      │
   │ POST /                              │
   │  body { walkInWindowId, guestName, │
   │          seats }                    │
   │ ─────────────────────────────────▶  │
   │                                      │
   │   OCC TryHoldSeats(window.Version)  │
   │   → Tạo WalkInBooking              │
   │   → Return booking + bill info       │
   │                                      │
   │ ◀── 201 WalkInBookingResponse ─────┤
   │                                      │
   │ (POS xử lý thanh toán SePay/VietQR)│
```

---

## Background Jobs liên quan

| Job | Trigger | Action |
|---|---|---|
| `WalkInWindowCleanupJob` | Mỗi 5 phút | Đóng windows đã hết hạn (`ExpiresAt < now` hoặc `WindowEnd < now`) |
| `EarlyCheckoutHandler` | Khi ActiveSession kết thúc sớm | Tạo WalkInWindow từ `Reservation.ScheduledEndTime` |
| `NoShowDetectionJob` | Khi Reservation no-show | Tạo WalkInWindow từ `Reservation.ScheduledEndTime` |

---

## WalkInWindow Status Flow

```
Available → Partial → Full → Closed/Expired
              ↑
              └── (khi ghế được giữ nhưng chưa đầy)
```

---

## Walk-in Lobby & ActiveSession Creation (Fix 2026-09-29)

Mỗi `WalkInBooking` được tạo kèm **synthetic `Lobby`** (private, `IsPrivate = true`) để:

- `GET /api/cafes/{cafeId}/pos/sessions/{sessionId}` trả `lobbyId` hợp lệ (không null) → FE render nút "Chuyển phiên" và "Ghép nhóm".
- Walk-in session có thể làm **source** trong lobby-merge flow (`POST /api/cafes/{cafeId}/lobby-merge/merge-requests` với `sourceLobbyId`).

### Circular FK bug (đã fix)

`ActiveSession.LobbyId` (FK → `Lobbies.Id`) và `Lobby.ActiveSessionId` (FK → `ActiveSessions.Id`) tạo cycle. EF Core không xác định được thứ tự INSERT khi cả 2 FK set đồng thời → throw `InvalidOperationException` lúc `SaveChanges`.

**Fix (2026-09-29):** 2-phase SaveChanges:

1. **Phase 1:** insert session (LobbyId=null) + lobby (ActiveSessionId=null) + box/table updates — cross-FK null an toàn.
2. **Phase 2:** set `session.LobbyId = walkInLobby.Id` và `walkInLobby.ActiveSessionId = session.Id` → 2 column UPDATE.

Cả 2 phase chạy trong cùng DB transaction → consistency đảm bảo. Xem chi tiết implementation trong [`cafe-pos.md` § Walk-in Lobby & Session Creation](./cafe-pos.md#walk-in-lobby--session-creation-circular-fk-fix-2026-09-29).
