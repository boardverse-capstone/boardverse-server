# LeaderboardController

**Base route:** `/api/v1/leaderboard`
**Controller:** `LeaderboardController.cs`
**Role:** Public (không cần đăng nhập). Khi user đã đăng nhập, response sẽ kèm thêm block `userRank` với thứ hạng hiện tại của viewer.

API lấy bảng xếp hạng người chơi theo **Karma Points** và **Global Elo** (BR §K-06).

## Endpoints

| Endpoint | Method | Role | Mô tả |
|----------|--------|------|--------|
| `/karma` | GET | Public | Top N người chơi theo Karma Points (DESC) |
| `/elo` | GET | Public | Top N người chơi theo Global Elo (DESC) |

---

## GET /api/v1/leaderboard/karma

Lấy bảng xếp hạng theo Karma Points.

### Query

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `top` | int | No | 50 | Số lượng top (1-100, mặc định 50). |
| `offset` | int | No | 0 | Bỏ qua N người đầu tiên (≥ 0). Dùng khi phân trang. |

### Response 200

```json
{
  "statusCode": 200,
  "message": "Lấy trạng thái karma thành công.",
  "data": {
    "entries": [
      {
        "rank": 1,
        "userId": "guid",
        "username": "topplayer",
        "displayName": "Top Player",
        "avatarUrl": "https://...",
        "karmaPoints": 250,
        "globalElo": 2200,
        "level": 30,
        "gamerTier": "Grandmaster"
      }
    ],
    "offset": 0,
    "limit": 50,
    "totalCount": 10000,
    "generatedAt": "2026-08-08T10:00:00Z",
    "userRank": null
  },
  "timestamp": "2026-08-08T10:00:00Z",
  "path": "/api/v1/leaderboard/karma"
}
```

### GamerTier mapping (BR §K-06)

| Tier | Karma Range |
|------|-------------|
| Bronze | 0-49 |
| Silver | 50-99 |
| Gold | 100-149 |
| Platinum | 150-199 |
| Diamond | 200-249 |
| Master | 250-299 |
| Grandmaster | 300+ |

> Tier được tính theo `KarmaPoints`, nhưng response trả sẵn `GamerTier` để client không cần tự tính.

---

## GET /api/v1/leaderboard/elo

Lấy bảng xếp hạng theo Global Elo (DESC).

### Query

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `top` | int | No | 50 | Số lượng top (1-100). |
| `offset` | int | No | 0 | Bỏ qua N người đầu tiên (≥ 0). |

### Response 200

```json
{
  "statusCode": 200,
  "message": "Lấy bảng xếp hạng Elo thành công.",
  "data": {
    "entries": [
      {
        "rank": 1,
        "userId": "guid",
        "username": "elochampion",
        "avatarUrl": "https://...",
        "globalElo": 2450,
        "gamerTier": "Diamond",
        "level": 30
      }
    ],
    "offset": 0,
    "limit": 50,
    "totalCount": 5000,
    "generatedAt": "2026-08-08T10:00:00Z",
    "userRank": null
  },
  "timestamp": "2026-08-08T10:00:00Z",
  "path": "/api/v1/leaderboard/elo"
}
```

---

## User's Own Rank (khi đã đăng nhập)

Nếu request có JWT hợp lệ, response sẽ kèm thêm block `userRank` với rank hiện tại của viewer. Lưu ý:

- `userRank` chỉ trả về khi viewer nằm trong **top 1000** của metric đang query. Nếu viewer nằm ngoài top 1000 → `userRank = null` (tránh scan toàn bảng mỗi request).
- `userRank` dùng schema `LeaderboardEntryDto` (đầy đủ Karma/Elo/Level/DisplayName) thay vì schema compact của từng loại leaderboard.

```json
{
  "statusCode": 200,
  "message": "Lấy trạng thái karma thành công.",
  "data": {
    "entries": [ /* ... top 50 ... */ ],
    "offset": 0,
    "limit": 50,
    "totalCount": 10000,
    "generatedAt": "2026-08-08T10:00:00Z",
    "userRank": {
      "rank": 156,
      "userId": "guid",
      "username": "currentuser",
      "displayName": "Current User",
      "avatarUrl": "https://...",
      "karmaPoints": 95,
      "globalElo": 1450,
      "level": 15,
      "gamerTier": "Silver"
    }
  }
}
```

---

## Error Codes

| Status | Description |
|--------|-------------|
| `200` | Thành công. |
| `500` | Lỗi hệ thống. |

`top` và `offset` luôn được clamp về phạm vi hợp lệ (`top: 1-100`, `offset: ≥ 0`) — không trả 400.

---

## Caching & Performance

- Leaderboard được cache 5 phút qua `IMemoryCache` (key = `lb:{karma|elo}:{offset}:{limit}`) để giảm tải DB.
- Cache miss → query DB + populate. Cache hit → trả thẳng entries + totalCount.
- `userRank` lookup KHÔNG dùng cache (vì phụ thuộc viewer hiện tại) — chỉ fetch entries + total từ cache.

---

## Liên quan

- [tournament.md](./tournament.md) — Tournament leaderboard (xoay quanh 1 giải cụ thể).
- [user-profile.md](./user-profile.md) — User Karma/Elo details.
