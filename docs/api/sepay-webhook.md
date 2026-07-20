# SePayWebhookController

**Base route:** `/api/payments/sepay/webhook`  
**Controller:** `SePayWebhookController.cs`  
**Role:** Webhook — gọi bởi SePay, **không yêu cầu JWT**

Endpoint nhận webhook từ cổng thanh toán SePay (server-to-server). Cập nhật trạng thái `BookingDeposit` và `ActiveSession` theo kết quả thanh toán. Tuân thủ Sepay-payment-flow.mdc.

> **Liên quan:** [payment flow rule](../../.cursor/rules/sepay-payment-flow.mdc), [booking.md](./booking.md) (deposit + session payment), [sepay-account.md](./sepay-account.md), [debug-sepay.md](./debug-sepay.md).

---

## Endpoints

| Endpoint | Method | Mô tả | Auth |
|----------|--------|--------|------|
| `/` | POST | Webhook nhận kết quả từ SePay | Webhook (signature) |
| `/return` | GET | Redirect URL cho user sau thanh toán | Public |
| `/mock` | POST | Mock webhook cho dev/test | **Dev only** |

---

## POST /api/payments/sepay/webhook

Nhận webhook từ SePay. Hệ thống xác thực **HMAC-SHA256 signature**, lookup theo `orderId`/`gatewayTransactionId`, cập nhật trạng thái thanh toán.

**Request body:**
```json
{
  "id": "webhook-event-id",
  "order_id": "BV12345678",
  "gateway": "SePay",
  "gateway_transaction_id": "TXN-...",
  "session_id": "optional-guid",
  "amount": 20000,
  "currency": "VND",
  "status": "success",
  "reference_code": "REF-...",
  "signature": "hmac-sha256-base64",
  "paid_at": "2026-07-14T10:00:00Z"
}
```

**Status mapping:**

| Incoming `status` | Action |
|-------------------|--------|
| `success` / `paid` | `MarkAsPaidAsync` (deposit) hoặc `session.Status = PAID` |
| `failed` / `canceled` / `cancelled` | `MarkAsRefundedAsync` (deposit) hoặc log cho session |
| other | ignore + log warning |

**Idempotency:**
- Duplicate webhook cho `BookingDeposit.Paid` hoặc `ActiveSession.Paid` → bỏ qua, không cập nhật lại.
- Amount mismatch → log warning + return.

**Response 200:** `{ "status": "ok" }`

**Response codes:**
- `200` — Webhook xử lý thành công (kể cả duplicate)
- `500` — Lỗi xử lý (SePay sẽ retry)

**Security:**
- Signature phải dùng **cùng field order** với checkout request: `order_amount=...,merchant=...,currency=...,operation=...,order_description=...,order_invoice_number=...,customer_id=...,payment_method=...,success_url=...,error_url=...,cancel_url=...`
- HMAC-SHA256 với `SecretKey`, trả Base64
- Signature invalid → log warning + return (không update state)

---

## GET /api/payments/sepay/webhook/return

URL SePay redirect user về sau khi thanh toán (success/cancel). Hiển thị message đơn giản.

**Query:**

| Param | Mô tả |
|-------|--------|
| `orderId` | Mã đơn hàng |
| `status` | `success` / `failed` / `cancelled` |

**Response 200 (success):**
```json
{ "message": "Thanh toán thành công! Vui lòng quay lại ứng dụng.", "orderId": "BV12345678" }
```

**Response 400 (failed/cancelled):**
```json
{ "message": "Thanh toán thất bại hoặc bị hủy.", "orderId": "BV12345678" }
```

---

## POST /api/payments/sepay/webhook/mock

**Mock webhook cho dev/test** — tạo fake `SePayWebhookDto` rồi gọi `HandleSePayWebhookAsync` giống webhook thật.

**⚠️ Chỉ dev/test — production phải disable** (gate bằng `Development` env hoặc feature flag `EnableMockPayments`).

**Body:**
```json
{
  "orderId": "BV00000001",
  "status": "success",
  "amount": 20000,
  "referenceCode": "REF-MOCK-001",
  "currency": "VND"
}
```

**Response 200:** `{ "status": "ok", "webhook": {...} }`

**Use case test:**

```powershell
# Deposit success
curl.exe -X POST http://localhost:5022/api/payments/sepay/webhook/mock \
  -H "Content-Type: application/json" \
  -d '{"orderId":"BV00000001","status":"success","amount":20000,"referenceCode":"REF-001"}'

# Session payment success
curl.exe -X POST http://localhost:5022/api/payments/sepay/webhook/mock \
  -H "Content-Type: application/json" \
  -d '{"orderId":"BV00000002","sessionId":"<guid>","status":"success","amount":85000}'

# Refund / cancel
curl.exe -X POST http://localhost:5022/api/payments/sepay/webhook/mock \
  -H "Content-Type: application/json" \
  -d '{"orderId":"BV00000001","status":"cancelled","amount":20000}'
```

---

## Lookup order

Webhook handler tìm kiếm theo thứ tự ưu tiên:

1. `GatewayTransactionId`
2. `OrderId`
3. `SessionId` / `OrderId` prefix (cho session payment)