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
| `/daily-summary` | GET | Tổng hợp giải ngân theo NGÀY (admin dashboard "Hôm nay chuyển bao nhiêu cho quán nào") |
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

## GET /api/v1/admin/settlements/daily-summary

W-07: Bảng tổng hợp giải ngân theo **ngày** (mặc định hôm nay theo giờ VN UTC+7).

Dùng cho màn hình admin: **"Hôm nay BoardVerse chuyển bao nhiêu tiền cho quán nào"**. Trả về:

- **Tổng quan**: số quán, tổng settlement, tổng tiền cần chuyển / đã chuyển / failed.
- **Chi tiết từng quán**: `TotalToTransfer` + breakdown theo `ByStatus` (Pending/Succeeded/Failed/Retrying/Overridden) + SePay bank info + danh sách `SettlementIds`.

### Quy tắc gom settlement theo ngày

| Status | Điều kiện thuộc ngày X |
|---|---|
| `Succeeded` / `Overridden` | `TransferredAt` thuộc ngày X (fallback `CreatedAt`) |
| `Pending` / `Retrying` / `Failed` | `CreatedAt` thuộc ngày X |

### Query Parameters

| Name | Type | Required | Description |
|---|---|---|---|
| `date` | string | No | Ngày cần xem, định dạng `yyyy-MM-dd` theo giờ VN. Mặc định = hôm nay (timezone `Asia/Ho_Chi_Minh`). |

### Response 200 — `SettlementDailySummaryDto`

```json
{
  "statusCode": 200,
  "message": "Lấy tổng hợp settlement theo ngày thành công.",
  "data": {
    "date": "2026-10-03",
    "timezone": "Asia/Ho_Chi_Minh",
    "queryStartUtc": "2026-10-02T17:00:00Z",
    "queryEndUtc": "2026-10-03T17:00:00Z",
    "cafeCount": 3,
    "totalSettlementCount": 12,
    "grandTotalToTransfer": 450000,
    "grandTotalTransferred": 320000,
    "grandTotalDeposit": 480000,
    "grandTotalFailed": 30000,
    "cafes": [
      {
        "cafeId": "<guid>",
        "cafeName": "BoardGame Cafe A",
        "cafeManagerId": "<guid>",
        "sePayBankCode": "MBBank",
        "sePayAccountNumber": "****7890",
        "totalDepositAmount": 250000,
        "totalToTransfer": 220000,
        "totalTransferred": 200000,
        "totalPending": 20000,
        "totalFailed": 0,
        "totalOverridden": 0,
        "totalCount": 6,
        "latestActivityAt": "2026-10-03T08:42:00Z",
        "byStatus": [
          { "status": "Pending",   "totalAmount": 20000,  "totalDepositAmount": 25000,  "totalNetTransferAmount": 20000,  "count": 1, "latestAt": "2026-10-03T08:42:00Z" },
          { "status": "Succeeded", "totalAmount": 200000, "totalDepositAmount": 225000, "totalNetTransferAmount": 200000, "count": 5, "latestAt": "2026-10-03T07:30:00Z" },
          { "status": "Failed",    "totalAmount": 0,      "totalDepositAmount": 0,      "totalNetTransferAmount": 0,      "count": 0, "latestAt": null },
          { "status": "Retrying",  "totalAmount": 0,      "totalDepositAmount": 0,      "totalNetTransferAmount": 0,      "count": 0, "latestAt": null },
          { "status": "Overridden","totalAmount": 0,      "totalDepositAmount": 0,      "totalNetTransferAmount": 0,      "count": 0, "latestAt": null }
        ],
        "settlementIds": ["<guid-1>", "<guid-2>", "..."]
      }
    ]
  }
}
```

### Sort

`Cafes[]` sắp xếp theo `TotalToTransfer DESC` — quán lớn lên đầu. Mỗi `ByStatus[]` LUÔN chứa đủ 5 status (kể cả `count=0`).

### Error Codes

| Status | Description |
|--------|-------------|
| `400` | `date` không đúng định dạng `yyyy-MM-dd` |
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `500` | Lỗi hệ thống |

### Use case

1. Admin mở dashboard "Settlement hôm nay".
2. Frontend gọi `GET /api/v1/admin/settlements/daily-summary` (không truyền `date` → lấy hôm nay).
3. Hiển thị overview card + danh sách cafe (sort theo tiền lớn nhất).
4. Bấm vào 1 cafe → drill-down `GET /api/v1/admin/settlements?cafeId={id}&date=...` để xem chi tiết.
5. Cafe có `totalFailed > 0` → bấm "Xem Failed" → `GET /api/v1/admin/settlements/failed?cafeId={id}` → retry hoặc override.

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
