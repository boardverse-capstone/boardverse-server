# AdminReservationController

**Base route:** `/api/v1/admin/reservations`
**Controller:** `AdminReservationController.cs`
**Role:** Admin

Endpoints quản trị cho Reservation — hiện tại có `override-refund` (BR-REFUND-07).

> **Liên quan:**
> - [reservation.md](./reservation.md) — Reservation API chuẩn
> - [admin-moderation.md](./admin-moderation.md) — Admin moderation chung (BR-RISK-05)
> - [lobby-merge.md](./lobby-merge.md) — Ghép nhóm lobby (Lobby Merge)

---

## Mục lục

- [POST /{reservationId}/override-refund](#post-idreservationidoverride-refund)
- [Business rules áp dụng](#business-rules-áp-dụng)

---

## POST /api/v1/admin/reservations/{reservationId}/override-refund

Admin override số BVC refund cho reservation đã completed (BR-REFUND-07).

Cho phép hoàn một phần hoặc toàn bộ số BVC đã capture. Ghi audit log `PlayerActionHistory` (BR-RISK-05).

**Role:** Admin

**Idempotency:** Header `Idempotency-Key` bắt buộc. Retry với cùng key trả cùng kết quả.

### Request

```http
POST /api/v1/admin/reservations/{reservationId}/override-refund
Authorization: Bearer <admin-token>
Idempotency-Key: admin-refund-override-abc123
Content-Type: application/json
```

```json
{
  "refundAmountBvc": 50000,
  "reason": "Khách hàng dispute — board game thiếu linh kiện do lỗi đóng gói của nhà cung cấp"
}
```

| Field | Type | Required | Mô tả |
|---|---|---|---|
| `refundAmountBvc` | long | ✅ | Số BVC cho hoàn (0 = không hoàn gì). Phải ≤ `OriginalDepositAmount`. |
| `reason` | string | ✅ | Ghi chú lý do override. 5–2000 ký tự. Bắt buộc cho audit (BR-RISK-05). |

### Validation

- `refundAmountBvc` phải ≥ 0 và ≤ `DepositAmount` đã capture
- `reason` phải từ 5–2000 ký tự
- Reservation phải ở trạng thái `Completed` hoặc `CancelledByPlayer` / `CancelledByCafe`
- Admin phải đăng nhập với role `Admin`

### Response 200

```json
{
  "statusCode": 200,
  "message": "Đã override refund 50000 BVC cho reservation '...'.",
  "data": {
    "reservationId": "guid",
    "userId": "user-guid",
    "originalDepositAmount": 120000,
    "previouslyCapturedAmount": 120000,
    "previouslyRefundedAmount": 0,
    "newRefundAmount": 50000,
    "actualRefundAmount": 50000,
    "adminUserId": "admin-guid",
    "processedAt": "2026-09-23T12:00:00Z"
  }
}
```

### Response fields

| Field | Mô tả |
|---|---|
| `reservationId` | Reservation đã xử lý |
| `userId` | User nhận refund |
| `originalDepositAmount` | Tổng deposit ban đầu (BVC) |
| `previouslyCapturedAmount` | Số đã capture trước override (BVC) |
| `previouslyRefundedAmount` | Số đã refund trước override (BVC) |
| `newRefundAmount` | Số BVC admin yêu cầu hoàn |
| `actualRefundAmount` | Số thực tế hoàn (sau khi tính toán) |
| `adminUserId` | Admin thực hiện |
| `processedAt` | Thời điểm xử lý |

### Lỗi

| Code | Khi nào | Message |
|---|---|---|
| `400` | Thiếu `Idempotency-Key` | `IdempotencyKeyRequired` |
| `400` | `refundAmountBvc` < 0 hoặc > deposit | `ValidationFailed` |
| `400` | `reason` < 5 ký tự | `ValidationFailed` |
| `401` | Thiếu token | `Unauthorized` |
| `403` | Không phải Admin | `Forbidden` |
| `404` | Reservation không tìm thấy | `ReservationNotFound` |
| `409` | Reservation không ở trạng thái Completed | `ReservationNotCompleted` |
| `500` | Lỗi hệ thống | `InternalServerError` |

### Side effects

1. Ghi `LedgerEntryType.AdminCredit` — cộng `refundAmountBvc` vào `Wallet.AvailableBalance` của user
2. Ghi `PlayerActionHistory` với `ActionType = AdminOverrideRefund` (BR-RISK-05)
3. Cập nhật `Reservation.RefundedAmount` (nếu entity có field này)

---

## Business rules áp dụng

| BR | Áp dụng |
|---|---|
| **BR-REFUND-07** | Admin override refund trong trường hợp đặc biệt |
| **BR-RISK-05** | Mọi admin action ghi audit log vĩnh viễn vào `PlayerActionHistory` |
