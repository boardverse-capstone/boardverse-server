# LobbyController

**Base route:** `/api/v1/lobbies`  
**Controller:** `LobbyController.cs`  
**Role:** Player — đã đăng nhập

API phòng chờ trực tuyến: tạo phòng, tham gia, rời phòng, tìm phòng theo game, đóng phòng, khóa phòng để bắt đầu ghép đội và mở cửa sổ đánh giá Karma sau khi POS thanh toán xong. Tuân thủ BR-07, BR-08, BR-10.

||| Endpoint | Method | Mô tả |
||----------|--------|--------|
||| `/` | POST | Tạo phòng chờ mới |
||| `/{lobbyId}/join` | POST | Tham gia phòng chờ |
||| `/{lobbyId}/leave` | POST | Rời phòng chờ |
||| `/{lobbyId}` | GET | Tra cứu chi tiết phòng |
||| `/search` | POST | Tìm phòng theo tựa game |
||| `/{lobbyId}/close` | POST | Đóng phòng (Host) |
||| `/{lobbyId}/lock` | POST | Khóa phòng chờ để bắt đầu ghép đội (Host) |
||| `/{lobbyId}/open-karma-window` | POST | Mở cửa sổ đánh giá Karma sau thanh toán (Host) |

---

## POST /api/v1/lobbies

Tạo phòng chờ. Host đặt giờ chơi, tựa game và sức chứa tối đa.

**Body mẫu:**

```json
{
  "gameTemplateId": "game-id",
  "scheduledStartTime": "2026-07-10T19:00:00Z",
  "maxMembers": 4,
  "cancellationLeadTimeMinutes": 30
}
```

**Response 201:** `LobbyResponseDto` — `status = Open`, danh sách thành viên chỉ có Host.

**Lỗi:** `400` thiếu field hoặc thời gian không hợp lệ; `404` không tìm thấy game; `500` lỗi hệ thống.

---

## POST /api/v1/lobbies/{lobbyId}/join

Tham gia phòng chờ. Hệ thống kiểm tra `MaxMembers` và `Booking.SeatCount` nếu phòng đã có booking liên kết.

**Response 200:** `LobbyResponseDto` cập nhật danh sách thành viên.

**Lỗi:** `404` không tìm thấy phòng; `409` phòng đã đầy hoặc bạn đã tham gia.

---

## POST /api/v1/lobbies/{lobbyId}/leave

Rời phòng chờ. Nếu Host rời đi, phòng chuyển sang `HOST_CANCELLED`.

**Response 200:** `LobbyResponseDto`.

**Lỗi:** `404` không tìm thấy phòng; `500` lỗi hệ thống.

---

## POST /api/v1/lobbies/{lobbyId}/close

Đóng phòng chờ. Chỉ Host được phép đóng.

**Response 200:** `LobbyResponseDto` — `status = Closed`.

**Lỗi:** `403` không phải Host; `404` không tìm thấy phòng.

---

## POST /api/v1/lobbies/{lobbyId}/lock

Khóa phòng chờ để bắt đầu ghép đội. Chỉ Host được phép khóa. Chuyển trạng thái `OPEN → FULL` khi đủ điều kiện.

**Response 200:** `LobbyResponseDto` — `status = Full`.

**Lỗi:** `403` không phải Host; `404` không tìm thấy phòng; `409` phòng không ở trạng thái mở.

---

## POST /api/v1/lobbies/{lobbyId}/open-karma-window

Mở cửa sổ đánh giá Karma sau khi phiên chơi kết thúc và POS thanh toán xong. Chỉ Host được phép mở.

**Response 200:** `LobbyResponseDto` — `ratingOpenedAt` được cập nhật.

**Lỗi:** `403` không phải Host; `404` không tìm thấy phòng; `500` lỗi hệ thống.

---

## POST /api/v1/lobbies/search

Tìm phòng chờ đang mở theo tựa game.

**Body mẫu:**

```json
{
  "gameTemplateId": "game-id",
  "latitude": 10.776889,
  "longitude": 106.700806,
  "radiusKm": 5,
  "minKarmaScore": 80
}
```

**Response 200:** danh sách `LobbyResponseDto`.

**Lỗi:** `400` thiếu `gameTemplateId`; `500` lỗi hệ thống.

---

## Luồng tích hợp

1. Host tạo phòng → `Open`.
2. Thành viên tham gia → khi đủ `MaxMembers` hoặc Host khóa sớm → `Full`.
3. Nhóm check-in tại quán → `InProgress`.
4. POS hoàn tất thanh toán → gọi `/open-karma-window` để mở đánh giá Karma.
5. Nếu trước giờ hẹn mà chưa đủ người → hệ thống hủy `TimeoutFailed`.
