# AdminTournamentController

**Base route:** `/api/v1/admin/tournaments`
**Controller:** `AdminTournamentController.cs`
**Role:** Admin

API Admin CRUD đầy đủ cho Tournament. Khác với `TournamentController` (Player-facing) chỉ xem + đăng ký/rút lui.

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/` | GET | Danh sách tất cả tournaments (phân trang, filter) |
| `/{tournamentId}` | GET | Chi tiết giải đấu |
| `/{tournamentId}` | PUT | Cập nhật giải đấu |
| `/` | POST | Tạo giải đấu mới |
| `/{tournamentId}` | DELETE | Xóa giải đấu (Draft/Cancelled only) |
| `/{tournamentId}/participants` | GET | Danh sách participants (filter status) |
| `/{tournamentId}/participants/{participantId}/check-in` | POST | Check-in participant |
| `/{tournamentId}/registration/open` | POST | Mở đăng ký |
| `/{tournamentId}/registration/close` | POST | Đóng đăng ký |
| `/{tournamentId}/start` | POST | Bắt đầu giải |
| `/{tournamentId}/complete` | POST | Hoàn thành giải |
| `/{tournamentId}/cancel` | POST | Hủy giải |

**Header:** `Authorization: Bearer <admin-token>`

---

## GET /api/v1/admin/tournaments

Lấy danh sách tất cả tournaments (phân trang, filter).

### Query

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `page` | int | No | Số trang (≥ 1). Mặc định 1 |
| `pageSize` | int | No | Số item/trang (1-100). Mặc định 20 |
| `status` | string | No | Filter theo status: `Draft`, `RegistrationOpen`, `RegistrationClosed`, `OnGoing`, `Completed`, `Cancelled` |
| `cafeId` | guid | No | Filter theo cafe |

### Response 200

```json
{
  "statusCode": 200,
  "message": "TournamentsRetrieved",
  "data": {
    "items": [
      {
        "id": "guid",
        "cafeId": "guid",
        "cafeName": "BoardGame Cafe A",
        "gameTemplateId": "guid",
        "gameName": "Catan",
        "name": "Summer Catan Championship 2026",
        "description": "Giải Catan mùa hè",
        "status": "RegistrationOpen",
        "registrationDeadline": "2026-08-10T23:59:59Z",
        "startTime": "2026-08-15T10:00:00Z",
        "maxParticipants": 32,
        "currentParticipants": 24,
        "entryFeeBvc": 50,
        "prizePoolBvc": 500,
        "minKarmaScore": 50,
        "minEloRequirement": 1000,
        "maxEloRequirement": 2000,
        "createdAt": "2026-07-01T10:00:00Z"
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalCount": 5,
    "totalPages": 1
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `500` | Lỗi hệ thống |

---

## GET /api/v1/admin/tournaments/{tournamentId}

Lấy chi tiết một tournament.

### Response 200

```json
{
  "statusCode": 200,
  "message": "TournamentRetrieved",
  "data": {
    "id": "guid",
    "cafeId": "guid",
    "cafeName": "BoardGame Cafe A",
    "gameTemplateId": "guid",
    "gameName": "Catan",
    "name": "Summer Catan Championship 2026",
    "description": "Giải Catan mùa hè",
    "status": "RegistrationOpen",
    "registrationDeadline": "2026-08-10T23:59:59Z",
    "startTime": "2026-08-15T10:00:00Z",
    "maxParticipants": 32,
    "currentParticipants": 24,
    "entryFeeBvc": 50,
    "prizePoolBvc": 500,
    "minKarmaScore": 50,
    "minEloRequirement": 1000,
    "maxEloRequirement": 2000,
    "createdAt": "2026-07-01T10:00:00Z",
    "updatedAt": "2026-07-15T14:30:00Z"
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `404` | Không tìm thấy tournament |
| `500` | Lỗi hệ thống |

---

## POST /api/v1/admin/tournaments

Tạo tournament mới (Draft status).

### Body

```json
{
  "cafeId": "guid",
  "gameTemplateId": "guid",
  "name": "Summer Catan Championship 2026",
  "description": "Giải Catan mùa hè",
  "registrationDeadline": "2026-08-10T23:59:59Z",
  "startTime": "2026-08-15T10:00:00Z",
  "maxParticipants": 32,
  "entryFeeBvc": 50,
  "prizePoolBvc": 500,
  "minKarmaScore": 50,
  "minEloRequirement": 1000,
  "maxEloRequirement": 2000
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `cafeId` | guid | ✅ | Cafe tổ chức |
| `gameTemplateId` | guid | ✅ | Game template |
| `name` | string | ✅ | Tên giải (5-200 ký tự) |
| `description` | string | No | Mô tả |
| `registrationDeadline` | datetime | ✅ | Hạn đăng ký |
| `startTime` | datetime | ✅ | Thời gian bắt đầu |
| `maxParticipants` | int | ✅ | Tối đa 4-256 |
| `entryFeeBvc` | int | No | Phí tham gia (BVC). Mặc định 0 |
| `prizePoolBvc` | int | No | Giải thưởng (BVC). Mặc định 0 |
| `minKarmaScore` | int | No | Karma tối thiểu (0-100). Mặc định 0 |
| `minEloRequirement` | int | No | Elo tối thiểu. Mặc định 0 |
| `maxEloRequirement` | int | No | Elo tối đa. Mặc định 9999 |

### Response 201

```json
{
  "statusCode": 201,
  "message": "TournamentCreated",
  "data": {
    "id": "guid",
    "status": "Draft",
    ...
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `400` | Dữ liệu không hợp lệ |
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `404` | Cafe/Game không tồn tại |
| `409` | Tên tournament trùng trong cùng cafe/ngày |
| `500` | Lỗi hệ thống |

---

## PUT /api/v1/admin/tournaments/{tournamentId}

Cập nhật tournament (chỉ Draft/Cancelled).

### Body

```json
{
  "name": "Updated Championship Name",
  "description": "Updated description",
  "registrationDeadline": "2026-08-12T23:59:59Z",
  "startTime": "2026-08-17T10:00:00Z",
  "maxParticipants": 48,
  "entryFeeBvc": 100,
  "prizePoolBvc": 1000,
  "minKarmaScore": 60,
  "minEloRequirement": 1200,
  "maxEloRequirement": 2200
}
```

### Response 200

```json
{
  "statusCode": 200,
  "message": "TournamentUpdated",
  "data": { ... }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `400` | Dữ liệu không hợp lệ |
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `404` | Không tìm thấy tournament |
| `409` | Tournament không ở Draft/Cancelled |
| `500` | Lỗi hệ thống |

---

## DELETE /api/v1/admin/tournaments/{tournamentId}

Xóa tournament (chỉ Draft/Cancelled).

### Response 200

```json
{
  "statusCode": 200,
  "message": "TournamentDeleted"
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `404` | Không tìm thấy tournament |
| `409` | Tournament đang Active (RegistrationOpen/OnGoing/Completed) |
| `500` | Lỗi hệ thống |

---

## GET /api/v1/admin/tournaments/{tournamentId}/participants

Lấy danh sách participants.

### Query

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `status` | string | No | Filter: `Registered`, `CheckedIn`, `Withdrawn`, `NoShow` |

### Response 200

```json
{
  "statusCode": 200,
  "message": "ParticipantsRetrieved",
  "data": {
    "items": [
      {
        "participantId": "guid",
        "userId": "guid",
        "username": "player1",
        "displayName": "Player One",
        "avatarUrl": "https://...",
        "karmaScore": 85,
        "gamerTier": "Gold",
        "elo": 1650,
        "status": "Registered",
        "checkedInAt": null,
        "finalRank": null,
        "registeredAt": "2026-08-05T10:00:00Z"
      }
    ],
    "totalCount": 24
  }
}
```

---

## POST /api/v1/admin/tournaments/{tournamentId}/registration/open

Mở đăng ký cho tournament.

### Response 200

```json
{
  "statusCode": 200,
  "message": "RegistrationOpened",
  "data": {
    "tournamentId": "guid",
    "status": "RegistrationOpen"
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `409` | Tournament không ở Draft |

---

## POST /api/v1/admin/tournaments/{tournamentId}/registration/close

Đóng đăng ký.

### Response 200

```json
{
  "statusCode": 200,
  "message": "RegistrationClosed",
  "data": {
    "tournamentId": "guid",
    "status": "RegistrationClosed",
    "totalParticipants": 24
  }
}
```

---

## POST /api/v1/admin/tournaments/{tournamentId}/start

Bắt đầu giải đấu (tạo Round 1 brackets).

### Response 200

```json
{
  "statusCode": 200,
  "message": "TournamentStarted",
  "data": {
    "tournamentId": "guid",
    "status": "OnGoing",
    "currentRound": 1
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `409` | Tournament không ở RegistrationClosed |
| `409` | Chưa đủ participants (ít nhất 2) |

---

## POST /api/v1/admin/tournaments/{tournamentId}/complete

Hoàn thành giải đấu.

### Response 200

```json
{
  "statusCode": 200,
  "message": "TournamentCompleted",
  "data": {
    "tournamentId": "guid",
    "status": "Completed",
    "winnerId": "guid",
    "totalRounds": 4
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `409` | Tournament không ở OnGoing |

---

## POST /api/v1/admin/tournaments/{tournamentId}/cancel

Hủy giải đấu (hoàn entry fee).

### Body

```json
{
  "reason": "Sự cố không thể tổ chức"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `reason` | string | ✅ | Lý do hủy (5-500 ký tự) |

### Response 200

```json
{
  "statusCode": 200,
  "message": "TournamentCancelled",
  "data": {
    "tournamentId": "guid",
    "status": "Cancelled",
    "reason": "Sự cố không thể tổ chức",
    "refundedParticipants": 24
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `400` | Thiếu reason |
| `409` | Tournament đã Completed |

---

## Tournament Status Flow

```
Draft
 → RegistrationOpen (POST .../registration/open)
 → RegistrationClosed (POST .../registration/close)
 → OnGoing (POST .../start)
 → Completed (POST .../complete)
   or
 → Cancelled (POST .../cancel)
```

---

## Liên quan

- [tournament.md](./tournament.md) — Player-facing tournament API
- [tournament-pos.md](./tournament-pos.md) — POS tournament operations
