# CafePosController

**Base route:** `/api/cafes/{cafeId}/pos`
**Controller:** `CafePosController.cs`
**Role:** Manager hoặc CafeStaff

API vận hành quầy: bàn, kho hộp game, phiên chơi, kiểm kê, khách vô danh, thanh toán một phần và thanh toán toàn bộ.

> **Lưu ý:** Một số endpoint liên quan đến phiên chơi đang được tách sang `ActiveSessionController` với base route `/api/cafes/{cafeId}/sessions`. Xem bảng bên dưới để biết chính xác từng thao tác thuộc controller nào.

## Endpoints

| Endpoint | Method | Mô tả | Controller |
|----------|--------|--------|------------|
| `/tables` | GET | Sơ đồ bàn realtime | `CafePosController` |
| `/boxes` | GET | Danh sách hộp game | `CafePosController` |
| `/boxes/by-barcode/{barcode}` | GET | Tra cứu hộp sau khi quét POS | `CafePosController` |
| `/sessions/active` | GET | Phiên đang chơi | `CafePosController` |
| `/bookings/{bookingCode}` | GET | Preview booking trước check-in (AC 1.1) | `CafePosController` |
| `/sessions` | POST | Giao game cho bàn — bắt đầu phiên chơi | `CafePosController` |
| `/sessions/from-booking` | POST | Host-led check-in từ mã đặt chỗ (AC 1.2-1.4) | `CafePosController` |
| `/sessions/{sessionId}/end` | POST | Trả game / kết thúc phiên chơi | `CafePosController` |
| `/sessions/{sessionId}/checkout` | POST | Thanh toán toàn bộ sau kiểm kê linh kiện | `ActiveSessionController` |
| `/sessions/{sessionId}/guest-slots` | POST | Thêm khách vô danh | `ActiveSessionController` |
| `/sessions/{sessionId}/partial-checkout` | POST | Thanh toán một phần khi có người về sớm | `ActiveSessionController` |

---

## GET /api/cafes/{cafeId}/pos/bookings/{bookingCode}

Preview thông tin booking trước khi check-in.

**AC 1.1:** Hiển thị danh sách thành viên + game info TRƯỚC khi check-in.

**Query params:**
- `bookingCode` (path): Mã đặt chỗ (OrderId)

**Response 200:**
```json
{
  "bookingCode": "BV12345678",
  "depositStatus": "Paid",
  "depositAmount": 50000,
  "scheduledStartTime": "2026-08-01T19:00:00Z",
  "registeredMemberCount": 4,
  "canCheckIn": true,
  "host": {
    "userId": "guid",
    "displayName": "Nguyễn Văn A",
    "avatarUrl": "https://...",
    "karmaScore": 85
  },
  "lobby": {
    "lobbyId": "guid",
    "gameName": "Catan",
    "minPlayers": 3,
    "maxPlayers": 4,
    "currentMemberCount": 4
  }
}
```

**Lỗi:**
- `401` thiếu token
- `403` không đủ quyền
- `404` không tìm thấy booking

---

## POST /api/cafes/{cafeId}/pos/sessions/from-booking

Host-led check-in: Quét mã đặt chỗ (BookingCode/OrderId) để kích hoạt phiên chơi cho cả nhóm.

**AC 1.2:** Nhân viên bấm xác nhận check-in → Trạng thái bàn chuyển sang Occupied.

**AC 1.3:** `DepositAppliedAmount = 0` — Thanh toán session KHÔNG trừ tiền cọc (BR-09 mới).

**AC 1.4:** Gửi SignalR notification để mobile app cập nhật "Đang chơi tại quán".

**Body mẫu:**
```json
{
  "bookingCode": "BV12345678",
  "cafeTableId": "guid",
  "barcode": "BV-bbbbbbbb-xxxxxxxx-001"
}
```

**Response 201:** `ActiveSessionDto`

**Lỗi:**
- `400` mã đặt chỗ không hợp lệ
- `401` thiếu token
- `403` không đủ quyền
- `404` quán, bàn hoặc game không tồn tại
- `409` đơn đặt chỗ chưa thanh toán hoặc bàn/game không khả dụng

---

## POST /api/cafes/{cafeId}/pos/sessions

POS quét barcode và chọn bàn → tạo `ActiveSession`, đặt hộp `InUse`, bàn `InUse` nếu đang trống.

**Body mẫu:**

```json
{
  "cafeTableId": "table-id",
  "barcode": "BV-bbbbbbbb-xxxxxxxx-002",
  "bookingId": "booking-id",
  "lobbyId": "lobby-id",
  "initialMemberUserIds": ["user-id-1"]
}
```

**Response 201:** `ActiveSessionDto` — `startedAt`, `elapsedMinutes`, `estimatedRemainingMinutes`, `defaultPlayTimeMinutes`.

**Lỗi:** `404` bàn/barcode; `409` hộp không Available, đã có session, hoặc bàn Reserved/Event.

---

## POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/end

Kết thúc phiên chơi — trả hộp game và giải phóng bàn nếu không còn session khác.

**Response 200:** Phiên đã đóng; hộp về Available; bàn về Available khi không còn session trên bàn đó.

**Lỗi:** `404` không tìm thấy phiên; `500` lỗi hệ thống.

---

## SignalR Notifications

**Hub:** `/hubs/pos`

Khi check-in thành công, hệ thống gửi event `SessionActivated` đến mobile app.

**Event Payload:**
```json
{
  "eventType": "SessionActivated",
  "sessionId": "guid",
  "cafeId": "guid",
  "cafeName": "Board Game Cafe",
  "hostId": "guid",
  "timestamp": "2026-08-01T19:00:00Z"
}
```

**Mobile app cần:**
1. Connect đến `/hubs/pos`
2. Gọi `JoinUserNotifications(userId)` để subscribe notifications cá nhân
3. Listen event `SessionActivated` để cập nhật UI → "Đang chơi tại quán"

---

## Luồng test

```powershell
# 1. Login staff/manager
# 2. GET .../pos/bookings/{bookingCode}  (preview booking)
#    → Xem thông tin: host, game, số người, trạng thái cọc
# 3. POST .../pos/sessions/from-booking  (check-in)
#    → Tạo session, bàn → InUse, gửi SignalR notification
# 4. POST .../pos/sessions/{id}/end
#    → Kết thúc phiên
```
