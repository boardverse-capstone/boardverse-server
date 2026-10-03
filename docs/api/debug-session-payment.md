# DebugSessionPaymentController

**Base route:** `/api/debug/session-payment`
**Controller:** `DebugSessionPaymentController.cs`
**Role:** Dev/Test only — **tự động 404** khi không ở `Development` env

Debug/test endpoints cho **session payment flow** (manager thanh toán hóa đơn phiên chơi tại POS). Dùng để:

- Seed nhanh 1 `ActiveSession` ở trạng thái `Unpaid` để test QR flow end-to-end.
- Bypass JWT/role gọi thẳng `PaymentService.CreateSessionPaymentAsync` để lấy `paymentUrl`/`qrImageUrl`.
- Mock SePay webhook success (route qua `PaymentService.HandleSePayWebhookAsync` giống flow thật 100%).
- Xem nhanh trạng thái session (bypass auth).

> ⚠️ **KHÔNG dùng trên production.** `IsDebugEnabled()` sẽ trả `NotFound()` ở mọi môi trường khác Development. Gate CHỈ theo `IHostEnvironment.IsDevelopment()` — không có env var override (C9 trong sepay-payment-flow.mdc).

> **Liên quan:** [payment.md](./payment.md), [sepay-webhook.md](./sepay-webhook.md), [sepay-account.md](./sepay-account.md), [cafe-pos.md](./cafe-pos.md).

---

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/ping` | GET | Health check — verify endpoint khả dụng + đọc env name |
| `/seed` | POST | Seed `ActiveSession` status=Unpaid (idempotent: trả session cũ nếu có) |
| `/create` | POST | Gọi `PaymentService.CreateSessionPaymentAsync` (bypass JWT/role) — trả QR |
| `/mock-success` | POST | Mock SePay webhook success — flip `Unpaid → Paid` qua handler thật |
| `/status` | GET | Xem nhanh trạng thái session (bypass auth) |

---

## GET /api/debug/session-payment/ping

Health check — trả 200 nếu debug endpoint khả dụng (i.e. đang ở Development env).

**Response 200:**
```json
{
  "enabled": true,
  "env": "Development",
  "timestamp": "2026-10-03T10:00:00Z"
}
```

**Response codes:**
- `200` — Endpoint khả dụng (Development env)
- `404` — Endpoint không khả dụng (Production / Staging env, hoặc route sai)

> **P0 Fix #7:** Trước đây `/ping` không gate theo env → lộ env name ra ngoài ở Production. Đã fix 2026-08.

---

## POST /api/debug/session-payment/seed

Tạo nhanh 1 `ActiveSession` ở trạng thái `Unpaid` gắn vào cafe (mặc định cafe đầu tiên trong DB). Trả `sessionId` để dùng cho các bước test tiếp theo.

**Idempotent:** Nếu đã tồn tại session `Unpaid` cùng `(cafeId, hostId, gameTemplateId)` → **không tạo mới**, cập nhật `TotalAmount = amount` trên session cũ. Trả về cùng `sessionId` với cờ `reused: true`.

**Query parameters:**

| Param | Type | Default | Mô tả |
|---|---|---|---|
| `cafeId` | Guid? | (cafe đầu tiên) | Override cafe cụ thể. Cafe này cần có `SePayAccountId` configured để test QR flow. |
| `amount` | decimal? | `85000` | `TotalAmount` của session. |

**Response 200:**
```json
{
  "reused": false,
  "sessionId": "<guid>",
  "cafeId": "<guid>",
  "cafeName": "BoardVerse Demo Cafe",
  "cafeHasSePayAccount": true,
  "cafeSePayAccountId": "<guid>",
  "hostId": "<guid>",
  "gameTemplateId": "<guid>",
  "amount": 85000,
  "status": "Unpaid"
}
```

**Response codes:**
- `200` — Seed thành công (mới tạo hoặc reuse session cũ)
- `400` — Không tìm thấy Cafe / User / GameTemplate trong DB
- `404` — Debug endpoint không khả dụng

> **Tip:** Chạy endpoint này đầu tiên mỗi session test. Kết quả `sessionId` dùng cho `/create`, `/mock-success`, `/status` ở các bước sau.

---

## POST /api/debug/session-payment/create

Gọi thẳng `IPaymentService.CreateSessionPaymentAsync` — **bypass JWT/role check** bằng cách truyền `userId = Guid.Empty, role = "Admin"`. Trả `paymentUrl` + `qrImageUrl` để test QR flow end-to-end.

**Body — `CreateSessionPaymentRequestDto`:**

| Field | Required | Mô tả |
|---|---|---|
| `sessionId` | ✅ | `ActiveSession.Id` (lấy từ `/seed`). |
| `totalAmount` | ❌ | Override `TotalAmount` nếu muốn test giá trị khác. |
| `depositAppliedAmount` | ❌ | Số cọc đã cấn trừ (BR-09, default 0). |
| `notes` | ❌ | Ghi chú POS. |

**Response 200:**
```json
{
  "sessionId": "<guid>",
  "paymentUrl": "https://vietqr.app/img?bank=MBBank&acc=...&amount=85000",
  "qrImageUrl": "https://vietqr.app/img?bank=MBBank&acc=...&amount=85000",
  "orderId": "BV-S-<session-id-n>",
  "amount": 85000,
  "gateway": "VietQr",
  "requiresManualConfirmation": true,
  "status": "Unpaid"
}
```

**Response codes:**
- `200` — Tạo QR thành công
- `404` — Debug endpoint không khả dụng
- `500` — Lỗi từ service (vd: cafe chưa config `SePayAccountId`, gateway timeout, …). Trả generic message `ApiErrorMessages.Http.Fallback` — KHÔNG lộ internal exception details.

> **P0 Fix #7:** Trước đây controller trả raw `ex.Message` → có thể lộ DB/provider/implementation details. Đã fix: log đầy đủ ở server, trả generic message cho client.

---

## POST /api/debug/session-payment/mock-success

Mock SePay webhook **success** cho session — đẩy status `Unpaid → Paid` qua `PaymentService.HandleSePayWebhookAsync` (giống flow thật 100%).

**GAP-09 Fix:** Trước đây endpoint này **trực tiếp update DB** chỉ set `Status = Paid` → thiếu 6 side-effects (capture BVC, WalkInWindow, release table/box, close lobby, member invoices). Giờ build `SePayWebhookDto` rồi route qua `HandleSePayWebhookAsync` giống gateway thật 100%.

**Query parameters:**

| Param | Type | Required | Mô tả |
|---|---|---|---|
| `sessionId` | Guid | ✅ | `ActiveSession.Id` (lấy từ `/seed`). |

**Điều kiện:**
- Session phải tồn tại.
- `Status == Unpaid` (nếu không → 409 Conflict, không force flip).

**Response 200:**
```json
{
  "sessionId": "<guid>",
  "status": "Paid",
  "paidAt": "2026-10-03T10:05:00Z",
  "totalAmount": 85000,
  "orderId": "BV-S-<session-id-n>",
  "message": "Session đã được mock chuyển sang Paid (qua webhook flow thật)."
}
```

**Response codes:**
- `200` — Mock success thành công, session flipped sang Paid
- `404` — Session không tồn tại HOẶC debug endpoint không khả dụng
- `409` — Session không ở `Unpaid` (đã Paid/Cancelled trước đó)
- `500` — Lỗi xử lý từ handler. Trả generic message + log chi tiết ở server.

> **Workflow test (5 bước):**
> 1. `POST /seed` → lấy `sessionId`
> 2. `POST /create` với `sessionId` → mở `paymentUrl` trong browser, scan QR thật
> 3. CK thật với nội dung `orderId` (SePay detect qua content)
> 4. Đợi webhook từ SePay → tự động flip Paid
> 5. Nếu muốn test mà không cần CK thật → `POST /mock-success` (bước này)
> 6. `GET /status?sessionId=...` để verify

---

## GET /api/debug/session-payment/status

Xem nhanh trạng thái hiện tại của session — bypass auth, chỉ cần biết `sessionId`. Dùng để debug giữa các bước test.

**Query parameters:**

| Param | Type | Required | Mô tả |
|---|---|---|---|
| `sessionId` | Guid | ✅ | `ActiveSession.Id`. |

**Response 200:**
```json
{
  "sessionId": "<guid>",
  "cafeId": "<guid>",
  "hostId": "<guid>",
  "status": "Paid",
  "totalAmount": 85000,
  "subtotal": 85000,
  "orderId": "BV-S-<session-id-n>",
  "transferContent": "BV12345678",
  "qrUrl": "BV12345678",
  "paidAt": "2026-10-03T10:05:00Z",
  "startedAt": "2026-10-03T09:00:00Z",
  "endedAt": "2026-10-03T10:00:00Z",
  "updatedAt": "2026-10-03T10:05:00Z"
}
```

**Response codes:**
- `200` — Trả thông tin session
- `404` — Session không tồn tại HOẶC debug endpoint không khả dụng

---

## Debug guard

```csharp
private bool IsDebugEnabled() => _env.IsDevelopment();
```

Mọi endpoint đều check `IsDebugEnabled()` ở đầu method và trả `NotFound()` (404) nếu không ở Development. Production tuyệt đối không thể bật các endpoint này qua env var.

---

## Use case: full session payment flow test

```powershell
# 1. Seed session (lấy sessionId)
$sessionId = (curl.exe -X POST http://localhost:5022/api/debug/session-payment/seed?amount=85000 | jq -r '.sessionId')

# 2. Tạo QR tờ session đã tạo
$resp = curl.exe -X POST http://localhost:5022/api/debug/session-payment/create `
  -H "Content-Type: application/json" `
  -d "{\"sessionId\": \"$sessionId\"}"
$paymentUrl = ($resp | jq -r '.paymentUrl')

# Mở paymentUrl trong browser, scan QR bằng app ngân hàng, CK thật.

# 3. (Tuỳ chọn) Mock success thay vì CK thật
curl.exe -X POST "http://localhost:5022/api/debug/session-payment/mock-success?sessionId=$sessionId"

# 4. Verify status
curl.exe "http://localhost:5022/api/debug/session-payment/status?sessionId=$sessionId" | jq '.status'
# → "Paid"
```

## Liên quan

- [payment.md](./payment.md) — PaymentController + SePay webhook flow chính
- [sepay-webhook.md](./sepay-webhook.md) — Webhook receiver + mock webhook (gated bởi `PaymentGatewaySettings.EnableMockPayments`)
- [cafe-pos.md](./cafe-pos.md) — POS thanh toán flow production