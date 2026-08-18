# AdminSettlementController

**Base route:** `/api/v1/admin/settlements`
**Controller:** `AdminSettlementController.cs`
**Role:** Admin

API Admin quản lý settlement — bao gồm xem danh sách (mọi status / chỉ Failed) và override sau khi retry exhausted.

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/` | GET | Lấy danh sách settlement có phân trang + filter |
| `/failed` | GET | Lấy danh sách settlement bị lỗi (Status=Failed) |
| `/{settlementId}/override` | POST | Override settlement thất bại |

**Header:** `Authorization: Bearer <admin-token>`

---

## GET /api/v1/admin/settlements

W-06: Lấy danh sách settlement có phân trang + filter. Dùng khi admin muốn xem tổng quan
mọi trạng thái (Pending/Retrying/Succeeded/Overridden/Failed) hoặc filter status cụ thể.

### Query Parameters

| Name | Type | Required | Description |
|---|---|---|---|
| `status` | string | No | Filter theo `CafeSettlementStatus` enum (Pending, Succeeded, Failed, Retrying, Overridden). |
| `cafeId` | guid | No | Filter theo cafe. |
| `cafeManagerId` | guid | No | Filter theo cafe manager. |
| `fromUtc` | datetime | No | Mốc bắt đầu (filter `CreatedAt >= fromUtc`). |
| `toUtc` | datetime | No | Mốc kết thúc (filter `CreatedAt <= toUtc`). |
| `pageNumber` | int | No | Trang (mặc định 1). |
| `pageSize` | int | No | Kích thước trang (mặc định 20, max 100). |

### Sort

Mặc định `UpdatedAt DESC` — Failed mới nhất (sau retry) nằm trên cùng.

### Response 200

```json
{
  "statusCode": 200,
  "message": "Lấy danh sách settlement thành công.",
  "data": {
    "data": [
      {
        "id": "guid",
        "cafeId": "guid",
        "cafeName": "BoardGame Cafe A",
        "cafeManagerId": "guid",
        "activeSessionId": "guid",
        "bookingDepositId": "guid",
        "depositAmount": 50000,
        "feeAmount": 0,
        "netTransferAmount": 50000,
        "sePayTransferId": null,
        "status": "Failed",
        "failureReason": "SePay timeout sau 5 retry",
        "retryCount": 5,
        "nextRetryAt": null,
        "transferredAt": null,
        "overrideBy": null,
        "overrideAt": null,
        "createdAt": "2026-08-18T10:00:00Z",
        "updatedAt": "2026-08-18T11:30:00Z"
      }
    ],
    "meta": {
      "currentPage": 1,
      "pageSize": 20,
      "totalItems": 1,
      "totalPages": 1,
      "hasPrevious": false,
      "hasNext": false
    }
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `400` | `status` không phải enum hợp lệ |
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `500` | Lỗi hệ thống |

---

## GET /api/v1/admin/settlements/failed

W-06: Endpoint **chính** cho admin tìm `SettlementId` để retry hoặc override. Trả về đầy đủ
`SettlementId` + `CafeName` + `Amount` + `FailureReason` để admin xác nhận đúng settlement —
không thể nhầm với `reservationId`/`sessionId`.

### Query Parameters

| Name | Type | Required | Description |
|---|---|---|---|
| `cafeId` | guid | No | Filter theo cafe. |
| `cafeManagerId` | guid | No | Filter theo cafe manager. |
| `fromUtc` | datetime | No | Mốc bắt đầu (filter `CreatedAt >= fromUtc`). |
| `toUtc` | datetime | No | Mốc kết thúc (filter `CreatedAt <= toUtc`). |
| `pageNumber` | int | No | Trang (mặc định 1). |
| `pageSize` | int | No | Kích thước trang (mặc định 20, max 100). |

### Sort

Mặc định `UpdatedAt DESC` — Failed mới nhất (sau retry) nằm trên cùng. Được tối ưu bởi
partial index `IX_CafeSettlements_Status_UpdatedAt` (xem migration `20260818102336_AddSettlementFailedIndex`).

### Response 200

Cùng shape với `GET /api/v1/admin/settlements`, `status` luôn là `Failed`.

### Error Codes

| Status | Description |
|--------|-------------|
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `500` | Lỗi hệ thống |

### Use case

1. SePay transfer fail → settlement `Status = Failed`, retry bởi `SettlementRetryJob` mỗi 5 phút.
2. Sau 5 retry, settlement vẫn `Failed`, hết `NextRetryAt`.
3. Admin mở dashboard → gọi `GET /api/v1/admin/settlements/failed` → lấy `id` (SettlementId) + `cafeName` + `failureReason`.
4. Admin chọn 1 trong 2:
 - **Retry qua AdminJobs**: gọi `POST /api/v1/admin/jobs/settlement/release-session-deposit?cafeId=...&sessionId=...&activeSessionId=...` để trigger SePay transfer thủ công.
 - **Override**: gọi `POST /api/v1/admin/settlements/{settlementId}/override` để đánh dấu đã xử lý thủ công bên ngoài.

---

## POST /api/v1/admin/settlements/{settlementId}/override

W-06: Admin manually override a failed settlement after retry exhaustion.

### Business Rules

- Settlement phải có `Status = Failed`.
- Settlement chưa được override trước đó.
- Admin phải ghi log lý do override.

### Response 200

```json
{
  "statusCode": 200,
  "message": "Settlement 'guid' đã được override bởi admin.",
  "data": {
    "id": "guid",
    "status": "Overridden",
    "overrideBy": "admin-user-id",
    "overrideAt": "2026-08-07T15:00:00Z",
    "previousStatus": "Failed",
    "settlementAmount": 30000,
    "cafeId": "guid",
    "cafeName": "BoardGame Cafe A",
    "bookingId": "guid"
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `404` | Settlement không tồn tại |
| `409` | Settlement đã được override trước đó |
| `500` | Lỗi hệ thống |

---

## Settlement Status Flow

```
Pending
  ↓ Auto process
Failed (retry exhausted)
  ↓ Admin override
Overridden
  ↓
Processed (manual)
```

---

## Settlement Retry Policy

| Attempt | Delay | Description |
|---------|-------|-------------|
| 1 | Immediate | First attempt |
| 2 | 5 minutes | After 5 min |
| 3 | 30 minutes | After 30 min |
| 4 | 2 hours | After 2 hours |
| 5 | 24 hours | Final attempt |

Sau 5 lần retry thất bại → `Failed`, chờ Admin override.

### Admin retry thủ công

Ngoài override, admin có thể chủ động trigger SePay transfer cho 1 settlement bất kỳ qua
AdminJobs endpoint — không cần chờ `SettlementRetryJob` chạy:

```
POST /api/v1/admin/jobs/settlement/release-session-deposit
 ?cafeId=<cafe-guid>
 &sessionId=<active-session-guid>
 &activeSessionId=<active-session-guid>
```

Endpoint này gọi `ISettlementService.ReleaseSessionDepositAsync` trực tiếp. Nếu SePay succeed →
settlement chuyển `Succeeded`. Nếu fail → vẫn `Failed` (job sẽ tự retry lại).

---

## Liên quan

- [settlement.md](./settlement.md) — Settlement flow chung
- [cafe-pos.md](./cafe-pos.md) — POS session payment trigger settlement
