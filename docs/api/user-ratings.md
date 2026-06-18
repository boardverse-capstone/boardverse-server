# User ratings (Karma cross-rating)

**Base route:** `/api/v1/users/ratings`  
**Controller:** `UserRatingController.cs`

| Endpoint | Method | Role |
|----------|--------|------|
| `/karma/lobbies/{lobbyId}` | GET | Player (thành viên phòng) |
| `/karma` | POST | Player (thành viên phòng) |

**Mở cửa sổ đánh giá (AC 3.1 — sau thanh toán POS):** `POST /api/v1/lobbies/{lobbyId}/karma-rating/open` — xem [lobby.md](./lobby.md).

---

## GET /api/v1/users/ratings/karma/lobbies/{lobbyId}

Trả ngữ cảnh cho màn đánh giá chéo (AC 3.2): danh sách thành viên cùng phòng (trừ bản thân) và các tiêu chí tích chọn.

**Response 200:**

```json
{
  "statusCode": 200,
  "message": "Lobby karma rating context retrieved successfully",
  "data": {
    "lobbyId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "lobbyStatus": "RatingOpen",
    "canSubmitRatings": true,
    "availableTags": [
      { "tag": "OnTime", "karmaWeight": 0.5 },
      { "tag": "Civil", "karmaWeight": 0.5 },
      { "tag": "Friendly", "karmaWeight": 0.5 },
      { "tag": "Toxic", "karmaWeight": -2 },
      { "tag": "NoShow", "karmaWeight": -2 }
    ],
    "membersToRate": [
      {
        "userId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
        "username": "player2",
        "avatarUrl": null,
        "alreadyRated": false
      }
    ]
  }
}
```

| Field | Ghi chú |
|-------|---------|
| `canSubmitRatings` | `true` khi `lobbyStatus` là `RatingOpen` hoặc `Closed` |
| `alreadyRated` | `true` nếu người gọi đã gửi đánh giá cho thành viên đó trong phòng |

**Lỗi:** `403` không phải thành viên phòng, `404` phòng không tồn tại.

---

## POST /api/v1/users/ratings/karma

Tiếp nhận mảng đánh giá chéo và cập nhật `UserProfiles.KarmaPoints` (AC 3.3).

### Request body

```json
{
  "lobbyId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "ratings": [
    {
      "targetUserId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "tags": ["OnTime", "Friendly"]
    },
    {
      "targetUserId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
      "tags": ["Toxic"]
    }
  ]
}
```

| Tag | Trọng số karma |
|-----|----------------|
| `OnTime` (Đúng giờ) | +0.5 |
| `Civil` (Văn minh) | +0.5 |
| `Friendly` (Thân thiện) | +0.5 |
| `Toxic` | -2 |
| `NoShow` | -2 |

Các tag trong một entry được **cộng dồn** (vd. OnTime + Friendly = +1.0, làm tròn khi ghi vào profile).

### Ràng buộc

- Người gọi phải là thành viên active của `lobbyId`.
- Phòng phải ở trạng thái `RatingOpen` hoặc `Closed`.
- Không được tự đánh giá bản thân.
- Mỗi cặp `(rater, target, lobby)` chỉ submit **một lần** (409 nếu trùng).

### Response 200

```json
{
  "statusCode": 200,
  "message": "Karma ratings submitted successfully",
  "data": {
    "lobbyId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "appliedRatings": [
      {
        "targetUserId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
        "tags": ["OnTime", "Friendly"],
        "karmaDeltaApplied": 1.0,
        "targetKarmaPointsAfter": 101,
        "targetGamerTier": "Bronze"
      }
    ]
  }
}
```

**Lỗi:** `400` phòng chưa mở đánh giá / tag rỗng, `409` đã đánh giá trước đó.

### PowerShell mẫu

```powershell
curl.exe -X POST "http://localhost:5022/api/v1/users/ratings/karma" ^
  -H "Authorization: Bearer YOUR_TOKEN" ^
  -H "Content-Type: application/json" ^
  -d "{\"lobbyId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"ratings\":[{\"targetUserId\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"tags\":[\"Friendly\",\"Civil\"]}]}"
```
