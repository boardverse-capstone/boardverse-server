# DebugSePayController

**Base route:** `/api/debug/sepay`  
**Controller:** `DebugSePayController.cs`  
**Role:** Dev/Test only — **tự động 404** khi không ở `Development` env hoặc `ENABLE_DEBUG=true`

Debug/test endpoints cho payment flow SePay. Dùng để:
- Test VietQR generation
- Tạo đơn cọc test
- Mock webhook nhận thanh toán
- Xem HTML page tương tác để test end-to-end

> ⚠️ **KHÔNG dùng trên production.** `IsDebugEnabled()` sẽ trả `NotFound()` ở mọi môi trường khác Development. Production tuyệt đối không bật `ENABLE_DEBUG=true`.

> **Liên quan:** [sepay-webhook.md](./sepay-webhook.md), [sepay-account.md](./sepay-account.md).

---

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/checkout` | GET | Tạo VietQR test, trả QR URL + settings |
| `/health` | GET | Health check — xem config hiện tại từ DB |
| `/test-deposit` | POST | Tạo đơn cọc test + generate VietQR |
| `/mock-webhook` | POST | Simulate webhook nhận thanh toán |
| `/test-page` | GET | HTML page tương tác để test end-to-end |
| `/generate-signature` | POST | Sinh HMAC-SHA256 signature cho SePay checkout request |
| `/preview-checkout` | POST | Preview VietQR checkout (QR URL + bank info echo) |

---

## GET /api/debug/sepay/checkout

Tạo VietQR thanh toán test. Trả JSON với QR URL + settings.

**Query:**

| Param | Mặc định | Mô tả |
|-------|----------|--------|
| `orderId` | `TEST-{timestamp}` | Mã đơn test |
| `amount` | `100` | Số tiền (VND) |

**Response 200:**
```json
{
  "orderId": "TEST-20260719120000",
  "amount": 100,
  "gateway": "VietQr",
  "isSuccess": true,
  "paymentUrl": "https://qr.sepay.vn/...",
  "qrImageUrl": "https://qr.sepay.vn/...",
  "requiresManualConfirmation": true,
  "message": "Quét mã QR để thanh toán.",
  "settings": {
    "environment": "Test",
    "merchantId": "...",
    "bankCode": "VCB",
    "accountNumber": "****5678",
    "accountHolder": "BOARDVERSE MASTER",
    "webhookTokenSet": true
  }
}
```

---

## GET /api/debug/sepay/health

Health check — kiểm tra config hiện tại từ DB.

**Response 200:**
```json
{
  "environment": "Test",
  "merchantId": "...",
  "webhookTokenSet": true,
  "apiBaseUrl": "https://pgapi.sepay.vn",
  "bankCode": "VCB",
  "accountNumber": "****5678",
  "accountHolder": "BOARDVERSE MASTER",
  "paymentMode": "VietQr_Static"
}
```

---

## POST /api/debug/sepay/test-deposit

Tạo đơn cọc test + generate VietQR ngay. Insert trực tiếp vào DB (hardcode `depositId = 11111111-1111-1111-1111-111111111111`).

**Query:**

| Param | Mặc định | Mô tả |
|-------|----------|--------|
| `amount` | `100` | Số tiền cọc |

**Response 200:**
```json
{
  "depositId": "11111111-1111-1111-1111-111111111111",
  "orderId": "BV-D-20260719120000",
  "amount": 100,
  "cafeId": "<demo-cafe-id>",
  "cafeName": "BoardVerse Demo Cafe",
  "basePrice": 100000,
  "transferContent": "BV-11111111111111111111111111111111",
  "status": "Pending",
  "gateway": "VietQr",
  "isSuccess": true,
  "paymentUrl": "https://qr.sepay.vn/...",
  "qrImageUrl": "https://qr.sepay.vn/...",
  "requiresManualConfirmation": true,
  "nextStep": "Quét QR để thanh toán, sau đó gọi POST /api/debug/sepay/mock-webhook"
}
```

> Lưu ý: Endpoint này `ALTER TABLE ... ALTER COLUMN QrUrl TYPE varchar(2000)` để VietQR URL không bị truncate. Tự động `DELETE` các record cũ trước khi insert.

---

## POST /api/debug/sepay/mock-webhook

Simulate webhook nhận thanh toán từ SePay. Lookup `BookingDeposit` theo `orderId` rồi set status.

**Body:**
```json
{
  "orderId": "BV-D-20260719120000",
  "status": "success"
}
```

| Field | Ràng buộc |
|-------|-----------|
| `orderId` | Bắt buộc |
| `status` | `success` → mark `Paid`; `cancelled`/`failed` → mark `Refunded` |

**Response 200:**
```json
{ "status": "deposit_marked_paid", "orderId": "BV-D-20260719120000" }
```

> Khác với `/api/payments/sepay/webhook/mock`: endpoint này **route qua service layer** (`IBookingDepositService.MarkAsPaidAsync` / `MarkAsRefundedAsync`) để đảm bảo state validation + idempotency + transaction wrapping. Trước đây gán trực tiếp `deposit.Status = ...` → bypass service logic; đã refactor 2026-08.

**Trả `200` (không `409`) cho idempotent replay:** Nếu service throw `ConflictException` (vd: deposit đã Paid), controller log warning + trả `200` với `status: "already_processed"` — tránh retry vĩnh viễn từ caller.

---

## GET /api/debug/sepay/test-page

**HTML page tương tác** — mở trong browser để test full flow:

1. Tự động tạo deposit test (hardcode `depositId = 22222222-2222-2222-2222-222222222222`)
2. Hiển thị QR + transfer content + amount
3. Có nút "✓ Đã thanh toán thật (Mock Webhook)" → gọi `/mock-webhook` để simulate
4. Có nút "✗ Hủy thanh toán" → mark cancelled

**Query:** `amount` (optional).

**Response:** HTML page với QR inline + buttons.

---

## POST /api/debug/sepay/generate-signature

Sinh HMAC-SHA256 signature cho SePay checkout request. Dùng để debug/test — preview signature trước khi gọi SePay API thật, hoặc so sánh với signature SePay tự sinh ra để verify logic signing.

**Spec §VI.1 (sepay-payment-flow.mdc):** dùng **CÙNG field order + skip-empty logic** với `SePayClient.GenerateSignature`. Nếu 2 bên lệch nhau → BoardVerse reject checkout + SePay mismatch → 401.

**Body — `DebugSePayGenerateSignatureRequestDto`:**

| Field | Required | Mô tả |
|---|---|---|
| `orderInvoiceNumber` | ✅ | Mã đơn (vd `BV12345678`). |
| `orderAmount` | ✅ | Số tiền VND. |
| `orderDescription` | No | Mô tả đơn. |
| `customerId` | No | UserId khác hàng (boardverse internal). |

**Response 200:**
```json
{
  "orderInvoiceNumber": "BV12345678",
  "signingString": "order_amount=20000&merchant=SP-XXXX&...",
  "signature": "abc123base64==",
  "orderAmount": 20000,
  "currency": "VND",
  "paymentMethod": "qr_vietqr",
  "operation": "payment"
}
```

| Field | Mô tả |
|---|---|
| `signingString` | Chuỗi canonical đã build (in ra để debug). |
| `signature` | Base64 của HMAC-SHA256(SecretKey, signingString). |

**Response codes:**
- `200` — Signature hợp lệ
- `400` — Thiếu `orderInvoiceNumber` hoặc amount ≤ 0
- `404` — Debug endpoint không khả dụng (non-Development env)

> **Tip debug:** Khi SePay báo "Invalid signature" → copy `signingString` ở response này, paste vào SePay debug tool để so sánh với chuỗi SePay tự build. Sai 1 field (order, encoding, separator) là fail.

---

## POST /api/debug/sepay/preview-checkout

Preview VietQR checkout: sinh QR URL từ bank info của master account, KHÔNG tạo `BookingDeposit` / không gọi SePay. Dùng để kiểm tra bank info config đúng (bank code, account number, account holder) trước khi chạy payment flow thật.

**Body — `DebugSePayPreviewCheckoutRequestDto`:**

| Field | Required | Mô tả |
|---|---|---|
| `amount` | ✅ | Số tiền VND preview (> 0). |
| `description` | No | Mô tả hiển thị trên QR (mặc định `BoardVerse preview`). |

**Response 200:**
```json
{
  "amount": 20000,
  "currency": "VND",
  "description": "BoardVerse preview",
  "gateway": "VietQr",
  "qrImageUrl": "https://vietqr.app/img?bank=MBBank&acc=...",
  "paymentUrl": "https://vietqr.app/img?bank=MBBank&acc=...",
  "bankCode": "MBBank",
  "accountNumber": "****7890",
  "accountHolder": "NGUYEN VAN A"
}
```

> `accountNumber` trong response bị **mask** (chỉ 4 số cuối).

**Response codes:**
- `200` — Preview thành công
- `400` — `amount <= 0` hoặc master account chưa config
- `404` — Debug endpoint không khả dụng (non-Development env)

---

## Debug guard

```csharp
private bool IsDebugEnabled()
{
    // C9 (đã fix): gate CHỈ theo env Development.
    // KHÔNG dùng env var ENABLE_DEBUG — vì có thể bị bật nhầm trong production
    // qua runtime config hoặc secret injection.
    return _env.IsDevelopment();
}
```

Mọi endpoint đều check `IsDebugEnabled()` ở đầu method và trả `NotFound()` (404) nếu không ở Development. Production tuyệt đối không thể bật các endpoint này qua env var.

---

## Use case

```bash
# 1. Mở browser
http://localhost:5022/api/debug/sepay/test-page

# 2. Trang web hiển thị QR — quét bằng app ngân hàng (VietQR)

# 3. Bấm nút "Đã thanh toán thật" → deposit.Paid = true

# 4. Verify qua /api/cafes/{cafeId}/bookings/{bookingId}
```

Hoặc dùng API trực tiếp:

```powershell
# Tạo deposit test
curl.exe -X POST http://localhost:5022/api/debug/sepay/test-deposit?amount=20000

# Confirm payment (mock webhook)
curl.exe -X POST http://localhost:5022/api/debug/sepay/mock-webhook `
  -H "Content-Type: application/json" `
  -d '{"orderId":"BV-D-20260719120000","status":"success"}'

# Cancel
curl.exe -X POST http://localhost:5022/api/debug/sepay/mock-webhook `
  -H "Content-Type: application/json" `
  -d '{"orderId":"BV-D-20260719120000","status":"cancelled"}'
```