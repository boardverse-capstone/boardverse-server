# LobbyController (Karma rating window)

**Base route:** `/api/v1/lobbies`  
**Controller:** `LobbyController.cs`

| Endpoint | Method | Role |
|----------|--------|------|
| `/{lobbyId}/karma-rating/open` | POST | Manager, CafeStaff, Admin |

---

## POST /api/v1/lobbies/{lobbyId}/karma-rating/open

Gọi sau khi nhân viên **hoàn tất thanh toán** trên Web POS (AC 3.1). Chuyển phòng sang `RatingOpen` và trả danh sách `memberUserIds` để mobile app phát push *"Yêu cầu đánh giá thành viên"*.

> Push notification thực tế do mobile/FCM xử lý; backend cung cấp payload và trạng thái phòng.

**Response 200:**

```json
{
  "statusCode": 200,
  "message": "Lobby karma rating window opened successfully",
  "data": {
    "lobbyId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "memberUserIds": [
      "11111111-1111-1111-1111-111111111111",
      "22222222-2222-2222-2222-222222222222"
    ],
    "ratingOpenedAt": "2026-06-17T10:30:00Z",
    "notificationType": "KarmaRatingRequired"
  }
}
```

**Lỗi:** `400` phòng chưa `InProgress`/`Closed`, `409` đã mở cửa sổ trước đó, `404` không tìm thấy phòng.

**Luồng tích hợp POS (gợi ý):**

1. POS hoàn tất billing → gọi endpoint này với `lobbyId` gắn phiên chơi.
2. Mobile nhận push (hoặc poll) → `GET /api/v1/users/ratings/karma/lobbies/{lobbyId}`.
3. Player submit → `POST /api/v1/users/ratings/karma`.
