# AdminReportController

**Base route:** `/api/v1/admin/reports`
**Controller:** `AdminReportController.cs`
**Role:** Admin

API báo cáo tổng hợp cho Admin dashboard: overview, lobby failures, deposits, cafe performance.

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/overview` | GET | Tổng quan dashboard |
| `/lobby-failures` | GET | Báo cáo lobby failures |
| `/deposits` | GET | Báo cáo deposits |
| `/cafe-performance` | GET | Báo cáo hiệu suất cafe |

**Header:** `Authorization: Bearer <admin-token>`

---

## GET /api/v1/admin/reports/overview

Tổng quan dashboard: users, cafes, tournaments, lobbies, bookings, deposits, revenue.

### Response 200

```json
{
  "statusCode": 200,
  "message": "Tổng quan dashboard.",
  "data": {
    "totalUsers": 1500,
    "activeUsers": 450,
    "totalCafes": 25,
    "activeCafes": 20,
    "totalTournaments": 50,
    "activeTournaments": 5,
    "totalLobbies": 1200,
    "activeLobbies": 45,
    "totalBookings": 3500,
    "pendingBookings": 12,
    "totalDeposits": 8000,
    "pendingDeposits": 150,
    "totalRevenue": 125000000,
    "recentActivity": [
      {
        "type": "lobby_created",
        "timestamp": "2026-08-07T14:30:00Z",
        "details": "Lobby ABC created by user XYZ"
      }
    ]
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

## GET /api/v1/admin/reports/lobby-failures

Báo cáo lobby failures: tổng hợp theo loại (timeout, host-cancelled, cafe-rejected, cafe-expired).

### Query

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `page` | int | No | Số trang (≥ 1). Mặc định 1 |
| `pageSize` | int | No | Số item/trang (1-100). Mặc định 20 |
| `fromUtc` | datetime | No | Bắt đầu khoảng thời gian (UTC) |
| `toUtc` | datetime | No | Kết thúc khoảng thời gian (UTC) |
| `failureType` | string | No | Filter: `TimeoutFailed`, `HostCancelled`, `RejectedByCafe`, `ExpiredByCafe` |

### Response 200

```json
{
  "statusCode": 200,
  "message": "Báo cáo lobby failures.",
  "data": {
    "summary": {
      "totalFailures": 150,
      "timeoutFailures": 45,
      "hostCancelled": 60,
      "rejectedByCafe": 25,
      "expiredByCafe": 20,
      "totalBvcForfeited": 35000,
      "totalBvcRefunded": 85000
    },
    "items": [
      {
        "lobbyId": "guid",
        "lobbyName": "Catan Evening Session",
        "cafeId": "guid",
        "cafeName": "BoardGame Cafe A",
        "hostId": "guid",
        "hostUsername": "host_user",
        "failureType": "TimeoutFailed",
        "playDate": "2026-08-05",
        "timeSlot": "evening",
        "maxPlayers": 6,
        "currentPlayers": 2,
        "minPlayers": 4,
        "depositAmount": 30000,
        "failureReason": "Không đủ người trước deadline",
        "createdAt": "2026-08-04T15:00:00Z",
        "failedAt": "2026-08-05T17:40:00Z"
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalCount": 150,
    "totalPages": 8
  }
}
```

### Failure Types

| Type | Mô tả |
|------|-------|
| `TimeoutFailed` | Lobby hết deadline mà chưa đủ minPlayers |
| `HostCancelled` | Host hủy lobby |
| `RejectedByCafe` | Cafe từ chối lobby pending approval |
| `ExpiredByCafe` | Cafe không duyệt trong 24h |

### Error Codes

| Status | Description |
|--------|-------------|
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `500` | Lỗi hệ thống |

---

## GET /api/v1/admin/reports/deposits

Báo cáo deposits: tổng hợp theo trạng thái (pending, paid, refunded, forfeited).

### Query

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `page` | int | No | Số trang (≥ 1). Mặc định 1 |
| `pageSize` | int | No | Số item/trang (1-100). Mặc định 20 |
| `fromUtc` | datetime | No | Bắt đầu khoảng thời gian (UTC) |
| `toUtc` | datetime | No | Kết thúc khoảng thời gian (UTC) |
| `status` | string | No | Filter: `Pending`, `Paid`, `Refunded`, `Forfeited` |

### Response 200

```json
{
  "statusCode": 200,
  "message": "Báo cáo deposits.",
  "data": {
    "summary": {
      "totalDeposits": 5000,
      "pendingDeposits": 45,
      "paidDeposits": 4200,
      "refundedDeposits": 500,
      "forfeitedDeposits": 255,
      "totalAmountPending": 2500000,
      "totalAmountPaid": 125000000,
      "totalAmountRefunded": 15000000,
      "totalAmountForfeited": 7650000
    },
    "items": [
      {
        "depositId": "guid",
        "bookingId": "guid",
        "lobbyId": "guid",
        "userId": "guid",
        "username": "player1",
        "cafeId": "guid",
        "cafeName": "BoardGame Cafe A",
        "amountBvc": 300,
        "amountVnd": 300000,
        "status": "Paid",
        "createdAt": "2026-08-05T10:00:00Z",
        "paidAt": "2026-08-05T10:05:00Z",
        "refundedAt": null,
        "forfeitedAt": null
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalCount": 5000,
    "totalPages": 250
  }
}
```

### Deposit Status

| Status | Mô tả |
|--------|-------|
| `Pending` | Chờ thanh toán |
| `Paid` | Đã thanh toán thành công |
| `Refunded` | Đã hoàn tiền (timeout/cancel) |
| `Forfeited` | Bị tịch thu (no-show) |

### Error Codes

| Status | Description |
|--------|-------------|
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `500` | Lỗi hệ thống |

---

## GET /api/v1/admin/reports/cafe-performance

Báo cáo hiệu suất cafe: doanh thu, số phiên, đánh giá, lobby activity.

### Query

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `page` | int | No | Số trang (≥ 1). Mặc định 1 |
| `pageSize` | int | No | Số item/trang (1-100). Mặc định 20 |
| `fromUtc` | datetime | No | Bắt đầu khoảng thời gian (UTC) |
| `toUtc` | datetime | No | Kết thúc khoảng thời gian (UTC) |
| `sortBy` | string | No | Sắp xếp: `revenue`, `sessions`, `rating`. Mặc định `revenue` |
| `sortOrder` | string | No | `asc` hoặc `desc`. Mặc định `desc` |

### Response 200

```json
{
  "statusCode": 200,
  "message": "Báo cáo hiệu suất cafe.",
  "data": {
    "summary": {
      "totalCafes": 25,
      "activeCafes": 20,
      "totalRevenue": 125000000,
      "totalSessions": 3500,
      "averageSessionRevenue": 35714,
      "averageRating": 4.2
    },
    "items": [
      {
        "cafeId": "guid",
        "cafeName": "BoardGame Cafe A",
        "address": "123 Main Street",
        "managerName": "Manager A",
        "operationalStatus": "ACTIVE",
        "totalRevenue": 15000000,
        "totalSessions": 420,
        "totalMembers": 1250,
        "averageSessionDuration": 180,
        "averageRating": 4.5,
        "totalReviews": 85,
        "activeLobbies": 5,
        "lobbySuccessRate": 0.85,
        "periodStart": "2026-08-01T00:00:00Z",
        "periodEnd": "2026-08-07T23:59:59Z"
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalCount": 25,
    "totalPages": 2
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

## Caching

Dashboard overview được cache 5 phút để giảm tải database.

---

## Liên quan

- [admin-cafe.md](./admin-cafe.md) — Admin Cafe CRUD
- [admin-moderation.md](./admin-moderation.md) — Karma moderation
- [reservation.md](./reservation.md) — Lobby/Reservation lifecycle
