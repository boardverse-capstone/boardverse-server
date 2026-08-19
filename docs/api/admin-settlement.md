# AdminSettlementController

**Base route:** `/api/v1/admin/settlements`
**Controller:** `AdminSettlementController.cs`
**Role:** Admin

API Admin override settlement khi automatic settlement thất bại sau khi đã retry hết lần.

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/{settlementId}/override` | POST | Override settlement thất bại |

**Header:** `Authorization: Bearer <admin-token>`

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

---

## Liên quan

- [settlement.md](./settlement.md) — Settlement flow chung
- [cafe-pos.md](./cafe-pos.md) — POS session payment trigger settlement
