# LeaderboardController

**Base route:** `/api/v1/leaderboard`
**Controller:** `LeaderboardController.cs`
**Role:** Public (không cần đăng nhập)

API lấy bảng xếp hạng người chơi theo Karma Points và Global Elo.

## Endpoints

| Endpoint | Method | Role | Mô tả |
|----------|--------|------|--------|
| `/karma` | GET | Public | Top N người chơi theo Karma Points |
| `/elo` | GET | Public | Top N người chơi theo Global Elo |

---

## GET /api/v1/leaderboard/karma

Lấy bảng xếp hạng theo Karma Points.

### Query

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `top` | int | No | 50 | Số lượng top (1-100) |
| `offset` | int | No | 0 | Bỏ qua N người đầu tiên |

### Response 200

```json
{
  "statusCode": 200,
  "message": "KarmaLeaderboardRetrieved",
  "data": {
    "entries": [
      {
        "rank": 1,
        "userId": "guid",
        "username": "topplayer",
        "displayName": "Top Player",
        "avatarUrl": "https://...",
        "karmaPoints": 150,
        "gamerTier": "Diamond",
        "level": 25
      },
      {
        "rank": 2,
        "userId": "guid",
        "username": "secondplayer",
        "displayName": "Second Player",
        "avatarUrl": "https://...",
        "karmaPoints": 145,
        "gamerTier": "Platinum",
        "level": 23
      }
    ],
    "totalCount": 10000,
    "offset": 0,
    "limit": 50
  }
}
```

### GamerTier Mapping

| Tier | Karma Range |
|------|-------------|
| Bronze | 0-49 |
| Silver | 50-99 |
| Gold | 100-149 |
| Platinum | 150-199 |
| Diamond | 200+ |

---

## GET /api/v1/leaderboard/elo

Lấy bảng xếp hạng theo Global Elo.

### Query

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `top` | int | No | 50 | Số lượng top (1-100) |
| `offset` | int | No | 0 | Bỏ qua N người đầu tiên |

### Response 200

```json
{
  "statusCode": 200,
  "message": "EloLeaderboardRetrieved",
  "data": {
    "entries": [
      {
        "rank": 1,
        "userId": "guid",
        "username": "elochampion",
        "displayName": "Elo Champion",
        "avatarUrl": "https://...",
        "globalElo": 2450,
        "gamerTier": "Diamond",
        "level": 30
      },
      {
        "rank": 2,
        "userId": "guid",
        "username": "elosecond",
        "displayName": "Elo Second",
        "avatarUrl": "https://...",
        "globalElo": 2380,
        "gamerTier": "Diamond",
        "level": 28
      }
    ],
    "totalCount": 5000,
    "offset": 0,
    "limit": 50
  }
}
```

---

## User's Own Rank

Nếu user đã đăng nhập (có JWT), response sẽ bao gồm thêm `userRank`:

```json
{
  "statusCode": 200,
  "message": "KarmaLeaderboardRetrieved",
  "data": {
    "entries": [...],
    "userRank": {
      "rank": 156,
      "userId": "guid",
      "username": "currentuser",
      "displayName": "Current User",
      "karmaPoints": 95,
      "gamerTier": "Gold",
      "level": 15
    },
    "totalCount": 10000,
    "offset": 0,
    "limit": 50
  }
}
```

---

## Error Codes

| Status | Description |
|--------|-------------|
| `400` | `top` hoặc `offset` không hợp lệ |
| `500` | Lỗi hệ thống |

---

## Caching

- Leaderboard được cache 5 phút để giảm tải database.
- Cache invalidation khi có thay đổi Karma/Elo.

---

## Liên quan

- [tournament.md](./tournament.md) — Tournament leaderboard
- [user-profile.md](./user-profile.md) — User Karma/Elo details
