# TournamentWaitlistController

**Base route:** `/api/v1/tournaments/{tournamentId}/waitlist`
**Controller:** `TournamentWaitlistController.cs`
**Role:** Player — đã đăng nhập (JWT)

API quản lý danh sách chờ (waitlist) của tournament. Khi tournament đầy, người chơi có thể join waitlist để nhận thông báo khi có slot trống.

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/` | POST | Tham gia waitlist |
| `/` | GET | Lấy danh sách waitlist |
| `/me` | GET | Kiểm tra trạng thái của mình trong waitlist |
| `/` | DELETE | Rời khỏi waitlist |
| `/confirm` | POST | Xác nhận tham gia từ waitlist |
| `/decline` | POST | Từ chối offer từ waitlist |

**Header:** `Authorization: Bearer <token>`

---

## POST /api/v1/tournaments/{tournamentId}/waitlist

Tham gia waitlist của một tournament đã đầy.

### Business Rules

- Tournament phải đầy (`CurrentParticipants >= MaxParticipants`).
- User chưa đăng ký participant trong tournament.
- User chưa có trong waitlist của tournament này.
- User không bị banned/suspended.

### Response 200

```json
{
  "statusCode": 200,
  "message": "Đã tham gia waitlist.",
  "data": {
    "waitlistEntryId": "guid",
    "tournamentId": "guid",
    "tournamentName": "Summer Catan Championship 2026",
    "userId": "guid",
    "username": "player1",
    "position": 5,
    "joinedAt": "2026-08-07T14:30:00Z",
    "status": "Waiting"
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `401` | Thiếu token |
| `404` | Tournament không tồn tại |
| `409` | Đã đăng ký hoặc đã trong waitlist |
| `500` | Lỗi hệ thống |

---

## GET /api/v1/tournaments/{tournamentId}/waitlist

Lấy danh sách waitlist của một tournament.

### Query

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `page` | int | No | Số trang (≥ 1). Mặc định 1 |
| `pageSize` | int | No | Số item/trang (1-100). Mặc định 20 |

### Response 200

```json
{
  "statusCode": 200,
  "message": "Danh sách waitlist.",
  "data": {
    "tournamentId": "guid",
    "tournamentName": "Summer Catan Championship 2026",
    "totalInWaitlist": 15,
    "items": [
      {
        "position": 1,
        "userId": "guid",
        "username": "player1",
        "displayName": "Player One",
        "karmaScore": 85,
        "elo": 1650,
        "joinedAt": "2026-08-05T10:00:00Z",
        "status": "Waiting"
      },
      {
        "position": 2,
        "userId": "guid",
        "username": "player2",
        "displayName": "Player Two",
        "karmaScore": 78,
        "elo": 1580,
        "joinedAt": "2026-08-05T11:00:00Z",
        "status": "Promoted"
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalCount": 15,
    "totalPages": 1
  }
}
```

### Waitlist Status

| Status | Mô tả |
|--------|-------|
| `Waiting` | Đang chờ slot trống |
| `Promoted` | Đã được promote lên participant |
| `Expired` | Hết hạn (không phản hồi trong 24h) |
| `Cancelled` | Tự rời khỏi waitlist |

### Error Codes

| Status | Description |
|--------|-------------|
| `401` | Thiếu token |
| `404` | Tournament không tồn tại |
| `500` | Lỗi hệ thống |

---

## GET /api/v1/tournaments/{tournamentId}/waitlist/me

Kiểm tra trạng thái của mình trong waitlist.

### Response 200

```json
{
  "statusCode": 200,
  "message": "Trạng thái waitlist của bạn.",
  "data": {
    "isInWaitlist": true,
    "waitlistEntryId": "guid",
    "position": 5,
    "status": "Waiting",
    "joinedAt": "2026-08-05T10:00:00Z",
    "promotionDeadline": null,
    "tournamentId": "guid",
    "tournamentName": "Summer Catan Championship 2026"
  }
}
```

Nếu không trong waitlist:

```json
{
  "statusCode": 200,
  "message": "Trạng thái waitlist của bạn.",
  "data": {
    "isInWaitlist": false
  }
}
```

---

## DELETE /api/v1/tournaments/{tournamentId}/waitlist

Rời khỏi waitlist.

### Response 200

```json
{
  "statusCode": 200,
  "message": "Đã rời khỏi waitlist.",
  "data": null
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `401` | Thiếu token |
| `404` | Tournament không tồn tại hoặc không trong waitlist |
| `500` | Lỗi hệ thống |

---

## POST /api/v1/tournaments/{tournamentId}/waitlist/confirm

Xác nhận tham gia tournament từ waitlist (khi có offer trống).

**Business Rules:**
- User phải đang ở trạng thái `Waiting` trong waitlist.
- Tournament phải còn slot trống.

**Response 200:**

```json
{
  "statusCode": 200,
  "message": "Đã xác nhận tham gia tournament.",
  "data": {
    "waitlistEntryId": "guid",
    "tournamentId": "guid",
    "status": "Promoted"
  }
}
```

**Lỗi:**

| Status | Description |
|--------|-------------|
| `401` | Thiếu token |
| `404` | Không có trong waitlist |
| `409` | Offer đã hết hạn hoặc không có slot |
| `500` | Lỗi hệ thống |

---

## POST /api/v1/tournaments/{tournamentId}/waitlist/decline

Từ chối offer từ waitlist (khi có slot trống).

**Business Rules:**
- User phải đang ở trạng thái `Waiting` trong waitlist.
- Sau khi decline, slot sẽ được offer cho user tiếp theo trong waitlist.

**Response 200:**

```json
{
  "statusCode": 200,
  "message": "Đã từ chối offer.",
  "data": {
    "waitlistEntryId": "guid",
    "tournamentId": "guid",
    "status": "Cancelled"
  }
}
```

**Lỗi:**

| Status | Description |
|--------|-------------|
| `401` | Thiếu token |
| `404` | Không có trong waitlist |
| `409` | Không có offer nào để từ chối |
| `500` | Lỗi hệ thống |

---

## Waitlist Promotion Flow

```
1. Tournament đầy → Users join waitlist
   ↓
2. Participant hủy đăng ký hoặc bị kick
   ↓
3. System promote user đầu tiên trong waitlist
   ↓
4. User nhận notification "Bạn đã được thêm vào tournament"
   ↓
5. User có 24h để xác nhận (nếu cần)
   ↓
6. Nếu không xác nhận → promote user tiếp theo
```

---

## Liên quan

- [tournament.md](./tournament.md) — Tournament registration
- [tournament-pos.md](./tournament-pos.md) — POS tournament operations
