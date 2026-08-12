# Booking API — Tài liệu cho Front-end

> **Phiên bản:** Áp dụng cho schema ERD hiện tại (bảng `Bookings` 9 cột, enum `BookingStatus` 5 giá trị).
> **Base URL:** `/api/bookings`
> **Auth:** Tất cả endpoint đều yêu cầu JWT Bearer token (trừ khi ghi rõ khác).

---

## 1. Tổng quan & State Machine

Booking là đơn **đặt chỗ** trước tại quán cafe. Booking **KHÁC** với `BookingDeposit` (đơn đặt cọc online qua SePay). Một Booking có thể tồn tại mà không có Deposit (đặt chỗ thường, không cọc online).

### 1.1. State machine

```
                                ┌──────────────────────────────┐
                                ▼                              │
[PendingDeposit] ──(cọc OK)──▶ [Confirmed] ──(check-in)──▶ [CheckedIn] ──(check-out)──▶ [Confirmed]
      │                              │                              │
      │                              ├──────────────────────────────┤
      │                              │                              │
      │                              ▼                              ▼
      │                         [NoShow]                       [Cancelled]
      │                              │                              │
      │                              └──────────────────────────────┘
      │
      └──(cancel)────────────────────▶ [Cancelled]
```

### 1.2. Enum `BookingStatus`

| Value | Tên | Ý nghĩa |
|-------|-----|----------|
| `0` | `PendingDeposit` | Mới tạo từ Lobby lock — đang chờ cọc online |
| `1` | `Confirmed` | Đã cọc (hoặc không cần cọc) — chờ check-in tại quán |
| `2` | `CheckedIn` | Khách đã đến quán và POS đã quét QR |
| `3` | `NoShow` | Không đến sau khi quá giờ |
| `4` | `Cancelled` | Bị hủy (bởi user hoặc manager) |

### 1.3. Flow từ Lobby → Booking

```
1. POST /api/v1/lobbies              → Lobby.Status = Open
2. Members join                      → Lobby.Status = Full (host lock)
3. POST /api/bookings                → Booking.Status = PendingDeposit
4. POST /api/payments/booking-deposit → QR SePay
5. SePay webhook success             → Booking.Status = Confirmed
6. POST /api/bookings/{id}/check-in  → Booking.Status = CheckedIn (POS)
7. POST /api/bookings/{id}/check-out → Booking.Status = Confirmed (kết thúc)
```

---

## 2. Cấu trúc dữ liệu

### 2.1. `BookingResponseDto` — object trả về cho FE

```json
{
  "id": "uuid",
  "lobbyId": "uuid",
  "cafeId": "uuid",
  "cafeName": "Cờ Cá Nhà Bà Tám",
  "cafeTableId": "uuid",
  "cafeTableName": "Bàn 3 - Cửa sổ",
  "scheduledStartTime": "2026-08-01T14:00:00Z",
  "scheduleEndTime": "2026-08-01T18:00:00Z",
  "status": 1,
  "statusText": "Confirmed",
  "verificationQRCode": "BV-9c8e7f4a2b1d4e6f",
  "playerQuantity": 4
}
```

| Field | Type | Mô tả |
|-------|------|--------|
| `id` | UUID | Mã booking |
| `lobbyId` | UUID | FK → Lobby (luôn có) |
| `cafeId` | UUID | FK → Cafe |
| `cafeName` | string \| null | Tên cafe (cache để FE không phải lookup) |
| `cafeTableId` | UUID | FK → CafeTable |
| `cafeTableName` | string \| null | Tên bàn (vd: "Bàn 3") |
| `scheduledStartTime` | ISO 8601 datetime | Giờ bắt đầu dự kiến |
| `scheduleEndTime` | ISO 8601 datetime | Giờ kết thúc dự kiến |
| `status` | integer | Enum `BookingStatus` (xem bảng trên) |
| `statusText` | string | Tên trạng thái (vd: `"Confirmed"`) |
| `verificationQRCode` | string \| null | QR code để POS quét khi check-in |
| `playerQuantity` | integer | Số người chơi (1–50) |

### 2.2. Envelope response chuẩn của BoardVerse

Mọi response đều bọc trong envelope sau:

```json
{
  "statusCode": 200,
  "isSuccess": true,
  "message": "Lấy chi tiết booking thành công.",
  "data": { /* BookingResponseDto hoặc List<BookingResponseDto> */ }
}
```

Khi lỗi, `data = null` và có thêm `errorCode` + `errors` (validation):

```json
{
  "statusCode": 409,
  "isSuccess": false,
  "message": "Bàn đã có booking khác trong khoảng thời gian này.",
  "data": null
}
```

---

## 3. Danh sách API

### 3.1. `POST /api/bookings` — Tạo booking mới

**Role:** Player — **chỉ Host của lobby** mới được tạo.

**Điều kiện tiên quyết:**
- Lobby phải ở trạng thái `Full` (đã lock).
- Lobby chưa có booking nào.

**Request body:**

```json
{
  "lobbyId": "uuid",
  "cafeId": "uuid",
  "cafeTableId": "uuid",
  "scheduledStartTime": "2026-08-01T14:00:00Z",
  "scheduleEndTime": "2026-08-01T18:00:00Z",
  "playerQuantity": 4
}
```

| Field | Required | Constraints |
|-------|----------|-------------|
| `lobbyId` | ✅ | UUID, phải là lobby user đang host và đã Full |
| `cafeId` | ✅ | UUID, cafe phải tồn tại |
| `cafeTableId` | ✅ | UUID, bàn phải thuộc cafe |
| `scheduledStartTime` | ✅ | ISO 8601, > hiện tại |
| `scheduleEndTime` | ✅ | ISO 8601, > scheduledStartTime |
| `playerQuantity` | ❌ | integer 1–50, mặc định = số members trong lobby |

**Response 201:**

```json
{
  "statusCode": 201,
  "isSuccess": true,
  "message": "Tạo booking thành công.",
  "data": {
    "id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    "lobbyId": "...",
    "cafeId": "...",
    "cafeName": "Cờ Cá Nhà Bà Tám",
    "cafeTableId": "...",
    "cafeTableName": "Bàn 5",
    "scheduledStartTime": "2026-08-01T14:00:00Z",
    "scheduleEndTime": "2026-08-01T18:00:00Z",
    "status": 0,
    "statusText": "PendingDeposit",
    "verificationQRCode": "BV-9c8e7f4a2b1d4e6f",
    "playerQuantity": 4
  }
}
```

**Mã lỗi:**

| Status | Nguyên nhân |
|--------|-------------|
| `400` | `scheduleEndTime <= scheduledStartTime`, hoặc `scheduledStartTime` trong quá khứ, hoặc `cafeTableId` không thuộc `cafeId` |
| `403` | User không phải Host của lobby |
| `404` | Không tìm thấy lobby / cafe / bàn |
| `409` | Lobby chưa ở status `Full`, hoặc lobby đã có booking trước đó, hoặc bàn bị trùng giờ |

---

### 3.2. `GET /api/bookings/{bookingId}` — Chi tiết booking

**Role:** Player (chỉ owner / member lobby), Manager, CafeStaff, Admin.

**Response 200:**

```json
{
  "statusCode": 200,
  "isSuccess": true,
  "message": "Lấy chi tiết booking thành công.",
  "data": { /* BookingResponseDto */ }
}
```

**Mã lỗi:** `401`, `404`.

---

### 3.3. `GET /api/bookings/lobby/{lobbyId}` — Booking theo lobby

**Role:** Player (chỉ member lobby), Manager, CafeStaff, Admin.

**Dùng cho màn hình lobby:** kiểm tra lobby đã có booking chưa để biết có cần tạo hay không.

**Response 200:**

```json
{
  "statusCode": 200,
  "isSuccess": true,
  "message": "Lấy booking theo lobby thành công.",
  "data": { /* BookingResponseDto hoặc null */ }
}
```

> **Lưu ý:** `data` có thể là `null` nếu lobby chưa được tạo booking.

---

### 3.4. `GET /api/bookings/cafe/{cafeId}` — Danh sách booking của cafe

**Role:** Manager, CafeStaff (của cafe này), Admin.

**Dùng cho màn hình POS / dashboard quán:** xem lịch booking trong ngày.

**Response 200:**

```json
{
  "statusCode": 200,
  "isSuccess": true,
  "message": "Lấy danh sách booking của quán thành công.",
  "data": [
    { /* BookingResponseDto */ },
    { /* BookingResponseDto */ }
  ]
}
```

Danh sách **đã được sắp xếp** theo `scheduledStartTime` tăng dần.

---

### 3.5. `PATCH /api/bookings/{bookingId}` — Cập nhật booking

**Role:** Player — **chỉ Host của lobby**.

**Điều kiện:**
- Chỉ owner (host lobby) mới được sửa.
- Chỉ sửa được khi booking chưa `CheckedIn` và chưa `Cancelled`.

**Request body (chỉ gửi các trường muốn đổi, không bắt buộc tất cả):**

```json
{
  "cafeTableId": "uuid-mới",
  "scheduledStartTime": "2026-08-01T15:00:00Z",
  "scheduleEndTime": "2026-08-01T19:00:00Z",
  "playerQuantity": 5
}
```

| Field | Type | Constraints |
|-------|------|-------------|
| `cafeTableId` | UUID \| null | Bàn mới phải thuộc cùng cafe |
| `scheduledStartTime` | ISO 8601 \| null | Thời gian bắt đầu mới |
| `scheduleEndTime` | ISO 8601 \| null | Phải sau `scheduledStartTime` |
| `playerQuantity` | int (1–50) \| null | Số người mới |

**Response 200:** `BookingResponseDto` đã cập nhật.

**Mã lỗi:** `400`, `403`, `404`, `409`.

---

### 3.6. `DELETE /api/bookings/{bookingId}` — Hủy booking

**Role:** Player — **chỉ Host của lobby**.

**Điều kiện:**
- Không thể hủy khi booking đã ở `CheckedIn`.
- Hủy xong, status chuyển thành `Cancelled` (không xóa khỏi DB).

**Query string (optional):**

```
?reason=Thành viên hủy vì bận việc đột xuất
```

| Field | Required | Mô tả |
|-------|----------|--------|
| `reason` | ❌ | Lý do hủy, optional |

**Response 200:**

```json
{
  "statusCode": 200,
  "isSuccess": true,
  "message": "Hủy booking thành công.",
  "data": { /* BookingResponseDto, status = 4 (Cancelled) */ }
}
```

**Mã lỗi:** `403`, `404`, `409`.

---

### 3.7. `POST /api/bookings/{bookingId}/check-in` — Check-in tại quán

**Role:** Manager, CafeStaff (của cafe này).

**Dùng cho POS:** quét `verificationQRCode` → gọi API này.

**Điều kiện:**
- Booking phải ở `Confirmed` (đã cọc OK).
- Booking chưa `CheckedIn`.

**Response 200:** `BookingResponseDto` với `status = 2 (CheckedIn)`.

**Mã lỗi:**

| Status | Nguyên nhân |
|--------|-------------|
| `403` | Không phải Manager/Staff của cafe sở hữu booking |
| `404` | Không tìm thấy booking |
| `409` | Booking chưa ở `Confirmed` (vd: vẫn đang `PendingDeposit`) |

---

### 3.8. `POST /api/bookings/{bookingId}/check-out` — Check-out tại quán

**Role:** Manager, CafeStaff (của cafe này).

**Điều kiện:**
- Booking phải ở `CheckedIn`.

**Response 200:** `BookingResponseDto` với `status = 1 (Confirmed)` — phiên chơi kết thúc.

> Theo ERD, booking không có trạng thái `Completed`. Sau khi check-out, status trở về `Confirmed` để báo hiệu phiên đã đóng.

---

## 4. Bảng tóm tắt nhanh cho Front-end

| Endpoint | Method | Role | Trả về |
|----------|--------|------|--------|
| `/api/bookings` | `POST` | Player (Host) | `BookingResponseDto` |
| `/api/bookings/{id}` | `GET` | Player / Staff / Admin | `BookingResponseDto` |
| `/api/bookings/lobby/{lobbyId}` | `GET` | Player (member lobby) / Staff | `BookingResponseDto \| null` |
| `/api/bookings/cafe/{cafeId}` | `GET` | Manager / CafeStaff / Admin | `List<BookingResponseDto>` |
| `/api/bookings/{id}` | `PATCH` | Player (Host) | `BookingResponseDto` |
| `/api/bookings/{id}` | `DELETE` | Player (Host) | `BookingResponseDto` |
| `/api/bookings/{id}/check-in` | `POST` | Manager / CafeStaff | `BookingResponseDto` |
| `/api/bookings/{id}/check-out` | `POST` | Manager / CafeStaff | `BookingResponseDto` |

---

## 5. Lưu ý quan trọng cho FE

1. **Không tự quản lý `userId`** trong body — backend lấy từ JWT token.
2. **`status` trả về là integer**, không phải string. FE nên dùng `statusText` để hiển thị text, dùng `status` (int) để so sánh logic.
3. **`scheduledStartTime`/`scheduleEndTime`** luôn là UTC (suffix `Z`). FE phải convert sang timezone local của user khi hiển thị.
4. **Tạo booking** yêu cầu lobby đã `Full`. FE cần gọi `POST /api/v1/lobbies/{id}/lock` trước, chờ response thành công rồi mới tạo booking.
5. **`cafeTableId` BẮT BUỘC** khi tạo booking. FE cần load danh sách bàn của cafe trước (qua `CafePosController` hoặc tương đương) để cho user chọn.
6. **Trùng giờ ở cùng bàn** sẽ trả 409. Nên có UI cảnh báo hoặc auto-refresh sau khi tạo.
7. **`verificationQRCode`** được backend tự sinh, FE chỉ hiển thị cho user và gửi cho POS khi check-in.
8. **Hủy (DELETE)** chỉ chuyển status, **không xóa** record. Nếu FE muốn biết booking có active hay không, kiểm tra `status != Cancelled`.

---

## 6. Liên kết

- API cọc online (SePay): xem [`payment.md`](./payment.md) — endpoint `POST /api/payments/booking-deposit`.
- API Lobby (tạo/lock): xem [`lobby.md`](./lobby.md).
- Webhook SePay: xem [`sepay-webhook.md`](./sepay-webhook.md).
- Enum chi tiết: `BoardVerse.Core/Enum/BookingStatus.cs`.