# AdminJobsController

**Base route:** `/api/v1/admin/jobs`
**Controller:** `AdminJobsController.cs`
**Role:** Admin

Admin trigger thủ công các background job. Dùng khi cần chạy job ngay (test, recovery, hoặc scheduler bị delay). Tất cả endpoint đều **idempotent** — gọi nhiều lần OK.

> **⚠️ Cảnh báo:** Các endpoint này chỉ dùng cho vận hành hệ thống. Lạm dụng có thể gây race condition.

---

## Mục lục

- [Reservation Jobs](#reservation-jobs)
- [Deposit Jobs](#deposit-jobs)
- [Wallet Jobs](#wallet-jobs)
- [Tournament Jobs](#tournament-jobs)
- [Friend Jobs](#friend-jobs)
- [Config & Settlement Jobs](#config--settlement-jobs)

---

## Reservation Jobs

### POST /api/v1/admin/jobs/reservations/process-deadlines

Trigger xử lý reservation đến deadline: viable / timeout (BR-LOBBY-02).

### Request

- Method: `POST`
- Path: `/api/v1/admin/jobs/reservations/process-deadlines`
- Auth: Admin

### Query Parameters

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `batchSize` | int | 100 | Số lượng xử lý mỗi lần (1-500) |

### Response 200

```json
{
  "statusCode": 200,
  "message": "Đã xử lý 5 reservation đến deadline.",
  "data": {
    "processed": 5
  }
}
```

### Lỗi

| Code | Mô tả |
|------|--------|
| 401 | Thiếu token |
| 403 | Không có quyền Admin |

---

### POST /api/v1/admin/jobs/reservations/process-cafe-approval-expiry

Trigger xử lý lobby `pendingCafeApproval` quá 24h → `expiredByCafe` (BR-NEW-11). Hoàn 100% BVC cho host.

### Request

- Method: `POST`
- Path: `/api/v1/admin/jobs/reservations/process-cafe-approval-expiry`
- Auth: Admin

### Query Parameters

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `batchSize` | int | 100 | Số lượng xử lý (1-500) |

### Response 200

```json
{
  "statusCode": 200,
  "message": "Đã xử lý 2 reservation quá hạn cafe approval.",
  "data": {
    "processed": 2
  }
}
```

---

### POST /api/v1/admin/jobs/reservations/process-no-show

Trigger xử lý no-show sau `scheduledTime + grace` (BR §21A.9). Tịch thu BVC, giảm Karma.

### Request

- Method: `POST`
- Path: `/api/v1/admin/jobs/reservations/process-no-show`
- Auth: Admin

### Query Parameters

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `batchSize` | int | 100 | Số lượng xử lý (1-500) |

### Response 200

```json
{
  "statusCode": 200,
  "message": "Đã đánh no-show 1 reservation.",
  "data": {
    "processed": 1
  }
}
```

---

### POST /api/v1/admin/jobs/reservations/process-bvc-capture-retry

Retry BVC capture cho các session `PAID` nhưng capture thất bại trước đó (GAP-9).

### Request

- Method: `POST`
- Path: `/api/v1/admin/jobs/reservations/process-bvc-capture-retry`
- Auth: Admin

### Query Parameters

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `batchSize` | int | 100 | Số lượng retry (1-500) |

### Response 200

```json
{
  "statusCode": 200,
  "message": "Đã retry 3 BVC capture.",
  "data": {
    "processed": 3
  }
}
```

---

## Deposit Jobs

### POST /api/v1/admin/jobs/deposits/process-expired

Trigger expire booking deposits quá thời gian giữ chỗ (BR-06). Giải phóng ghế, hoàn hoặc tịch thu cọc.

### Request

- Method: `POST`
- Path: `/api/v1/admin/jobs/deposits/process-expired`
- Auth: Admin

### Response 200

```json
{
  "statusCode": 200,
  "message": "Đã trigger expire booking deposits.",
  "data": {
    "processed": true
  }
}
```

---

## Wallet Jobs

### POST /api/v1/admin/jobs/wallet/expire-pending-topups

Trigger expire pending BVC top-ups quá timeout.

### Request

- Method: `POST`
- Path: `/api/v1/admin/jobs/wallet/expire-pending-topups`
- Auth: Admin

### Response 200

```json
{
  "statusCode": 200,
  "message": "Đã expire 2 pending top-up.",
  "data": {
    "processed": 2
  }
}
```

---

## Tournament Jobs

### POST /api/v1/admin/jobs/tournaments/auto-close-expired-registrations

Trigger auto-close tournament registrations quá hạn.

### Request

- Method: `POST`
- Path: `/api/v1/admin/jobs/tournaments/auto-close-expired-registrations`
- Auth: Admin

### Response 200

```json
{
  "statusCode": 200,
  "message": "Đã auto-close 1 tournament registration.",
  "data": {
    "processed": 1
  }
}
```

---

### POST /api/v1/admin/jobs/tournaments/send-reminders

Trigger gửi tournament reminders (48h/24h trước start) (BR-NEW-13).

### Request

- Method: `POST`
- Path: `/api/v1/admin/jobs/tournaments/send-reminders`
- Auth: Admin

### Response 200

```json
{
  "statusCode": 200,
  "message": "Đã gửi 5 tournament reminder.",
  "data": {
    "processed": 5
  }
}
```

---

### POST /api/v1/admin/jobs/tournaments/auto-mark-no-shows

Trigger auto-mark no-show cho tournament participants quá grace period.

### Request

- Method: `POST`
- Path: `/api/v1/admin/jobs/tournaments/auto-mark-no-shows`
- Auth: Admin

### Response 200

```json
{
  "statusCode": 200,
  "message": "Đã mark no-show 2 participants.",
  "data": {
    "totalMarked": 2,
    "totalKarmaPenalty": -10
  }
}
```

---

## Friend Jobs

### POST /api/v1/admin/jobs/friends/expire-old-pending-requests

Trigger expire pending friend requests quá 30 ngày.

### Request

- Method: `POST`
- Path: `/api/v1/admin/jobs/friends/expire-old-pending-requests`
- Auth: Admin

### Response 200

```json
{
  "statusCode": 200,
  "message": "Đã expire 3 friend request.",
  "data": {
    "processed": 3
  }
}
```

---

## Config & Settlement Jobs

### POST /api/v1/admin/jobs/config/invalidate-cache

Invalidate cache system configuration. Dùng khi đổi config qua DB và muốn áp dụng ngay.

### Request

- Method: `POST`
- Path: `/api/v1/admin/jobs/config/invalidate-cache`
- Auth: Admin

### Response 200

```json
{
  "statusCode": 200,
  "message": "Đã invalidate cache system configuration.",
  "data": {
    "cleared": true
  }
}
```

---

### POST /api/v1/admin/jobs/settlement/release-session-deposit

Manual retry SePay transfer cho settlement bị Failed (W-06). Dùng khi settlement retry job không xử lý kịp hoặc admin cần trigger ngay cho 1 settlement cụ thể.

### Request

- Method: `POST`
- Path: `/api/v1/admin/jobs/settlement/release-session-deposit`
- Auth: Admin

### Query Parameters

| Param | Type | Required | Description |
|-------|------|----------|-------------|
| `cafeId` | Guid | Yes | Mã cafe |
| `sessionId` | Guid | Yes | Mã session (ActiveSession.Id) |
| `activeSessionId` | Guid | Yes | Mã ActiveSession (correlation id) |

### Response 200

```json
{
  "statusCode": 200,
  "message": "Đã release settlement. Status=Released.",
  "data": {
    "status": "Released",
    "cafeId": "guid",
    "sessionId": "guid",
    "releasedAt": "2026-08-15T12:00:00Z"
  }
}
```

### Lỗi

| Code | Mô tả |
|------|--------|
| 400 | Session chưa PAID |
| 404 | Không tìm thấy settlement |
| 409 | Cafe chưa config SePay hoặc điều kiện không hợp lệ |
