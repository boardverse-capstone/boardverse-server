# Cafe Booking API

**Controller:** `CafeBookingController.cs`
**Base route:** `/api/cafes/{cafeId:guid}`
**Auth:** Player (đã đăng nhập — JWT bearer)

API read-only giúp Mobile xem trước capacity + chọn bàn trước khi vào luồng Booking. Hai endpoint:

| Endpoint | Method | Role | Mục đích |
|---|---|---|---|
| `/api/cafes/{cafeId}/available-tables` | GET | Player | Danh sách bàn trống theo khung giờ + số ghế (Task #1) |
| `/api/cafes/{cafeId}/availability` | GET | Player | Capacity tổng quát + slot thay thế khi hết chỗ (Task #2) |

---

## Get available tables

**GET /api/cafes/{cafeId}/available-tables**

Mobile `BookingSummaryPage` gọi để hiển thị dropdown chọn bàn trước khi gọi `POST /api/bookings`.

**Auth:** Player đã đăng nhập.

**Query parameters:**

| Name | Type | Required | Description |
|---|---|---|---|
| `cafeId` | guid | ✅ | Từ path |
| `scheduledStartTime` | datetime (ISO 8601 UTC) | ✅ | Giờ bắt đầu dự kiến |
| `scheduleEndTime` | datetime (ISO 8601 UTC) | ✅ | Giờ kết thúc dự kiến |
| `seatCount` | int | ✅ | Số ghế tối thiểu (≥ 1) |

**Response 200:**
```json
{
  "statusCode": 200,
  "isSuccess": true,
  "message": "Lấy danh sách bàn trống thành công.",
  "data": [
    {
      "id": "table-uuid",
      "name": "Bàn 1",
      "seatCount": 4,
      "isAvailable": true,
      "pricePerHour": 90000
    },
    {
      "id": "table-uuid-2",
      "name": "Bàn 2",
      "seatCount": 6,
      "isAvailable": true,
      "pricePerHour": 120000
    }
  ]
}
```

| Field | Type | Mô tả |
|---|---|---|
| `id` | guid | `cafeTableId` — gửi lên `POST /api/bookings` |
| `name` | string | Tên hiển thị |
| `seatCount` | int | Sức chứa của bàn |
| `isAvailable` | bool | `true` khi không bị conflict giờ và status = `Available` |
| `pricePerHour` | decimal | `Cafe.BasePrice` — để mobile hiển thị trước cho user |

**Errors:** `400` thiếu query / giờ không hợp lệ; `401`; `404` không tìm thấy cafe; `500`.

> **Note:** `CafeTables.SeatCount` (default 4) là field mới từ migration `20260801045906_AddBookingNoShowVotesAndRatings` — trước đó bàn không có khái niệm capacity. Tất cả bàn cũ được backfill `SeatCount=4`.

---

## Get availability

**GET /api/cafes/{cafeId}/availability**

Mobile `BoardGameDetailPage` gọi để cảnh báo "quán hết chỗ" trước khi user vào luồng Booking. Khi `hasCapacity=false`, mobile hiển thị danh sách `alternativeSlots` để user chọn khung giờ khác.

**Auth:** Player đã đăng nhập.

**Query parameters:**

| Name | Type | Required | Default | Description |
|---|---|---|---|---|
| `cafeId` | guid | ✅ | — | Từ path |
| `startTime` | datetime (ISO 8601 UTC) | ✅ | — | Giờ bắt đầu muốn đặt |
| `endTime` | datetime (ISO 8601 UTC) | ✅ | — | Giờ kết thúc muốn đặt |
| `seatCount` | int | ❌ | 1 | Số ghế player cần |
| `gameTemplateId` | guid | ❌ | null | Game đang chọn — check số hộp Available |

**Response 200:**
```json
{
  "statusCode": 200,
  "isSuccess": true,
  "message": "Khảo sát capacity quán thành công.",
  "data": {
    "cafeId": "uuid",
    "cafeName": "Cờ Cá Nhà Bà Tám",
    "requestedStartTime": "2026-08-01T19:00:00Z",
    "requestedEndTime": "2026-08-01T21:00:00Z",
    "hasCapacity": false,
    "availableSeats": 2,
    "totalSeats": 16,
    "availableGameBoxCount": 1,
    "selectedGameAvailabilityStatus": "PartiallyAvailable",
    "alternativeSlots": [
      { "startTime": "2026-08-01T20:00:00Z", "endTime": "2026-08-01T22:00:00Z", "availableSeats": 12 },
      { "startTime": "2026-08-01T21:00:00Z", "endTime": "2026-08-01T23:00:00Z", "availableSeats": 16 }
    ]
  }
}
```

| Field | Type | Mô tả |
|---|---|---|
| `hasCapacity` | bool | `true` khi `availableSeats >= seatCount` |
| `availableSeats` | int | Tổng ghế trống khả dụng trong khung giờ |
| `totalSeats` | int | Tổng ghế của toàn bộ bàn `IsActive` trong cafe |
| `availableGameBoxCount` | int | Số hộp game Available (chỉ khi filter `gameTemplateId`) |
| `selectedGameAvailabilityStatus` | enum? | `Available` / `PartiallyAvailable` / `Unavailable` / `NotRequested` |
| `alternativeSlots` | array | Top khung giỡ gần nhất (cách 30 phút) còn capacity |

**Errors:** `400` giờ không hợp lệ; `401`; `404`; `500`.

---

## Luồng mobile tích hợp

```
1. Mobile: User chọn cafe + game + giờ trên BoardGameDetailPage
2. Mobile: GET /api/cafes/{cafeId}/availability?startTime=...&endTime=...&gameTemplateId=...
3. Mobile: 
   - Nếu hasCapacity=true → tiếp tục bước 4
   - Nếu hasCapacity=false → hiển thị alternativeSlots → user chọn slot khác hoặc quán khác
4. Mobile: GET /api/cafes/{cafeId}/available-tables?scheduledStartTime=...&scheduleEndTime=...&seatCount=...
5. Mobile: user chọn bàn → POST /api/bookings với cafeTableId đã chọn
```

> **Performance:** Hai endpoint này chỉ query nhẹ (`CafeTables.IsActive=true` + scan `Bookings` overlap khung giờ), không gọi sang payment gateway. Nên cache ngắn hạn ở client (30s) để giảm load khi user ch�nh sửa giờ liên tục.

---

## Liên quan

- [booking.md](./booking.md) — POST /api/bookings
- [cafe.md](./cafe.md) — Cafe config (BasePrice, RefundPolicy)
- [board-games.md](./board-games.md) — GameTemplate + game box inventory
