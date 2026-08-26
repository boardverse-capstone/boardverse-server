# CafeSettlementController

**Base route:** `/api/cafes/{cafeId}/settlements`
**Controller:** `CafeSettlementController.cs`
**Role:** Manager, CafeStaff

API cho quản lý giải ngân deposit và settlement tại quán.

---

## Mục lục

- [GET /pending](#get-pending)

---

## GET /pending

Lấy danh sách giải ngân deposit đang chờ xử lý.

### Request

- Method: `GET`
- Path: `/api/cafes/{cafeId}/settlements/pending`
- Auth: Manager, CafeStaff

### Path Parameters

| Param | Type | Description |
|-------|------|-------------|
| `cafeId` | Guid | Mã quán |

### Response 200

```json
{
  "status": 200,
  "message": "Lấy danh sách settlement đang chờ thành công.",
  "data": {
    "items": [
      {
        "id": "guid",
        "reservationId": "guid",
        "hostUserId": "guid",
        "hostUsername": "player_a",
        "cafeId": "guid",
        "cafeName": "Board Game Cafe",
        "gameName": "Catan",
        "playDate": "2026-08-12",
        "timeSlot": "evening",
        "playerCount": 4,
        "depositAmount": 50000,
        "capturedAmount": 50000,
        "status": "Pending",
        "createdAt": "2026-08-12T19:00:00Z",
        "capturedAt": null
      }
    ],
    "totalCount": 1
  }
}
```

### Response 401

Unauthorized — thiếu token hoặc token không hợp lệ.

### Response 403

Forbidden — không có quyền vận hành quán.

### Response 404

Quán không tìm thành.

---

## Settlement Status Flow

```
Pending → Processing → Completed
                ↓
              Failed
```

| Status | Description |
|--------|-------------|
| `Pending` | Chờ xử lý |
| `Processing` | Đang xử lý |
| `Completed` | Đã giải ngân thành công |
| `Failed` | Giải ngân thất bại |

---

## Business Rules

- **BR-REVENUE-01**: Tiền cọc thuộc 100% về quán khi reservation đủ điều kiện ghi nhận.
- **BR-REVENUE-02**: Quán nhận cọc khi player check-in hoặc khi quá hạn cho phép hủy.
- Admin/platform không thu phần trăm.

---

## Related Endpoints

- [Reservation End](./reservation.md) — POS endpoint để kết thúc reservation và capture deposit.
- [Admin Settlement](../admin-settlement.md) — Admin endpoint để xem và xử lý settlement.
