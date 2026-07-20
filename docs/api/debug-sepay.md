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

> Khác với `/api/payments/sepay/webhook/mock`: endpoint này **trực tiếp update DB** thay vì qua `HandleSePayWebhookAsync`. Dùng cho dev test nhanh.

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

## Debug guard

```csharp
private bool IsDebugEnabled()
{
    return _env.IsDevelopment()
        || string.Equals(Environment.GetEnvironmentVariable("ENABLE_DEBUG"), "true", StringComparison.OrdinalIgnoreCase);
}
```

Mọi endpoint đều check `IsDebugEnabled()` ở đầu. Production tuyệt đối không bật `ENABLE_DEBUG=true`.

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