# BookingController

**Base route:** `/api/v1/bookings`  
**Controller:** `BookingController.cs`  
**Role:** Player — đã đăng nhập

API đặt chỗ trực tuyến: tạo đơn, giữ ghế, xác nhận sau khi thanh toán cọc, hủy đơn và tra cứu trạng thái đơn. Tuân thủ BR-05, BR-06, BR-09.

|| Endpoint | Method | Mô tả |
|----------|--------|--------|
|| `/` | POST | Tạo đơn đặt chỗ mới |
|| `/{bookingId}/confirm` | POST | Xác nhận thanh toán cọc thành công |
|| `/{bookingId}/cancel` | POST | Hủy đơn đặt chỗ |
|| `/{bookingId}` | GET | Tra cứu chi tiết đơn |

---
## POST /api/v1/bookings

Tạo đơn đặt chỗ. Hệ thống kiểm tra số ghế còn trống theo khung giờ, tạo `Booking` ở `PENDING_DEPOSIT` và chuyển ghế sang `HOLDING` trong 5 phút.

**Body mẫu:**

```json
{
  "cafeId": "cafe-id",
  "gameTemplateId": "game-id",
  "seatCount": 4,
  "scheduledStartTime": "2026-07-10T19:00:00Z",
  "holdDurationMinutes": 30,
  "refundPolicy": "Full"
}
```

**Response 201:** `BookingResponseDto` — `status = PENDING_DEPOSIT`, `expiresAt = scheduledStartTime + holdDurationMinutes`.

**Lỗi:** `400` thiếu field; `404` không tìm thấy quán/game; `409` quán hết chỗ cho khung giờ.

---
## POST /api/v1/bookings/{bookingId}/confirm

Xác nhận đơn sau khi nhận được `Payment: Success` từ cổng thanh toán. Booking chuyển sang `CONFIRMED`; ghế chuyển `RESERVED`.

```json
{
  "paymentTransactionId": "transaction-id"
}
```

**Lỗi:** `400` mã giao dịch không hợp lệ; `404` không tìm thấy đơn; `409` đơn đã xác nhận hoặc hết hạn giữ chỗ.

---
## POST /api/v1/bookings/{bookingId}/cancel

Hủy đơn đặt chỗ của người dùng. Ghế được giải phóng về `AVAILABLE`.

```json
{
  "reason": "Đổi kế hoạch."
}
```

**Lỗi:** `400` lý do không hợp lệ; `404` không tìm thấy đơn; `409` đơn không thể hủy.

---
## GET /api/v1/bookings/{bookingId}

Tra cứu chi tiết đơn đặt chỗ của người dùng.

**Lỗi:** `401` thiếu token; `403` không đủ quyền; `404` không tìm thấy đơn.

---
## Luồng tích hợp

1. Mobile tạo `POST /api/v1/bookings` → `PENDING_DEPOSIT`, ghế `HOLDING`.
2. Người dùng thanh toán cọc → `POST /api/v1/bookings/{id}/confirm` → `CONFIRMED`, ghế `RESERVED`.
3. Nhân viên POS quét QR Host → kích hoạt `ActiveSession`, ghế `IN_USE`.
4. Quá hạn không đến → chuyển `EXPIRED`, tịch thu cọc, giải phóng ghế.
