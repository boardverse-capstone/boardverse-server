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

> **Webhook bổ sung cho Split Bill:** Xem `POST /api/payments/sepay/webhook/member-payment` ở [payment.md](./payment.md) §"Member Payment Webhook" — nhận kết quả QR per-member và update `ActiveSessionMember.PaymentStatus`. Idempotent theo `OrderId` (per-member), tách biệt hoàn toàn với deposit / session payment webhook ở đây.

---

## POST /api/payments/sepay/webhook

Nhận webhook từ SePay. Hệ thống xác thực signature **3 mode** theo `SePayAccount.WebhookAuthType`, lookup theo `orderId`/`gatewayTransactionId`, cập nhật trạng thái thanh toán.

> **⚠️ SECURITY UPDATE 2026-08-27:** Trước đây webhook chỉ support Base64-encoded `WebhookToken` đặt trong body field `signature` — sai cả format lẫn vị trí so với spec SePay 2024+. Đã refactor theo đúng spec:
> - Signature đọc từ **HTTP header**, không từ JSON body
> - Hỗ trợ 3 chế độ auth (None / ApiKey / HmacSha256) cấu hình qua `SePayAccount.WebhookAuthType`
> - HMAC-SHA256 dùng format chuẩn `sha256=HMAC(secret, "{timestamp}.{rawBody}")` với anti-replay ±300s

**Request body (JSON, parse từ raw stream):**
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
  "paid_at": "2026-07-14T10:00:00Z"
}
```

**Lưu ý quan trọng:** Webhook controller đọc **raw body TR�ỚC** khi ASP.NET parse JSON, sau đó mới parse JSON từ raw string. Điều này đảm bảo signature HMAC được tính trên chính xác byte sequence SePay gửi, không bị parser reformat (escape, reorder key) làm vỡ hash.

**Request headers (theo `WebhookAuthType`):**

| Mode | Header | Value |
|---|---|---|
| `None` | (không yêu cầu) | Bypass — chỉ dev/test |
| `ApiKey` | `Authorization` | `Apikey <WebhookToken>` (constant-time compare) |
| `HmacSha256` | `X-SePay-Signature` | `sha256=<hex>` của HMAC-SHA256(SecretKey, `"{timestamp}.{rawBody}"`) |
| `HmacSha256` | `X-SePay-Timestamp` | Unix epoch seconds (phải trong ±300s của server time, anti-replay) |

**Status mapping:**

| Incoming `status` | Action |
|-------------------|--------|
| `success` / `paid` | Deposit: `MarkAsPaidAsync`. Session: atomic `Status = PAID` + **lifecycle cleanup** (release table + box + members + close lobby, xem [payment.md](./payment.md) §"Session Payment Lifecycle Cleanup") |
| `failed` / `canceled` / `cancelled` | `MarkAsRefundedAsync` (deposit); log cho session |
| other | ignore + log warning |

**Idempotency:**
- Duplicate webhook cho `BookingDeposit.Paid` hoặc `ActiveSession.Paid` → bỏ qua, không cập nhật lại.
- Amount mismatch (session) → log warning + return **trước khi** đổi trạng thái (không half-commit).
- Cleanup method là idempotent — chạy lại trên state đã-cleaned là no-op.

**Response 200:** `{ "status": "ok" }`

**Response codes:**
- `200` — Webhook xử lý thành công (kể cả duplicate)
- `400` — JSON parse fail
- `401` — Signature verification fail (header thiếu / timestamp skew / constant-time mismatch)
- `500` — Lỗi xử lý (SePay sẽ retry)

**Webhook Signature Verification — 3 mode chi tiết:**

### Mode `None` (default cho tài khoản hiện hữu)

- Bypass hoàn toàn verification — luôn trả `true`.
- **CHỈ** chấp nhận khi `IHostEnvironment.IsDevelopment() == true`.
- **Production reject**: Trong môi trường production-like (non-Development), `VerifyWebhookAsync` trả `false` → controller trả `401`. Log error level.
- **Use case**: dev/test, smoke test local.
- **⚠️ Production BẮT BUỘC upgrade** sang `ApiKey` hoặc `HmacSha256` trước khi go-live.

### Mode `ApiKey`

- SePay gửi `Authorization: Apikey <WebhookToken>`.
- Controller extract phần sau `Apikey ` → so sánh constant-time với `SePayAccount.WebhookToken`.
- Không có timestamp, không có anti-replay — phù hợp khi SePay chỉ cần auth đơn giản.
- **Setup**: Admin set `WebhookAuthType = ApiKey` + đặt `WebhookToken` ngẫu nhiên (32+ chars) qua `PUT /api/sepay-accounts/{id}`.

### Mode `HmacSha256` (SePay khuyến nghị)

SePay gửi 2 header:
- `X-SePay-Signature: sha256=<hex>` (lowercase hex, KHÔNG phải Base64)
- `X-SePay-Timestamp: <unix-seconds>`

Verification flow:
1. Parse `X-SePay-Timestamp` thành `long unixSeconds`. Nếu thiếu hoặc `|now - unixSeconds| > 300` → reject (anti-replay).
2. Reconstruct `expected = "sha256=" + HMAC-SHA256(SecretKey, UTF-8("{unixSeconds}.{rawBody}"))`.
3. So sánh constant-time với header `X-SePay-Signature`.

**Setup**:
- Admin set `WebhookAuthType = HmacSha256`.
- `SecretKey` = shared secret dùng cho cả checkout request và webhook (cùng key).
- SePay dashboard config callback URL + secret trùng `SecretKey` trong DB.

**Anti-replay rationale**: ±300s cho phép clock skew giữa SePay server và BoardVerse server, nhưng chặn replay attack sau khi signature đã lộ.

Xem chi tiết triển khai tại `BoardVerse.Services/Services/Payments/SePayClient.cs` (`VerifyWebhookAsync`, `VerifyApiKey`, `VerifyHmacSha256`). 11 unit test trong `BoardVerse.Tests/Services/SePayClientWebhookVerificationTests.cs` cover cả 3 mode + edge cases (timestamp skew, missing headers, mismatch, replay).

**SePayAccount config** (xem [sepay-account.md](./sepay-account.md)):
- Column `WebhookAuthType` (int) trên `SePayAccounts` table — migration `20260826203024_AddSePayWebhookAuthType`.
- Default = `None` (backward compat cho account cũ).
- Production: admin update từng account sang `ApiKey` hoặc `HmacSha256` qua API.

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

1. `SePayTransactionId`
2. `OrderId`
3. `SessionId` / `OrderId` prefix (cho session payment)

---

## Known Issues & Bug History

### 5. Nested-transaction crash (2026-08-18) — **đã fix**

**Symptom:** SePay webhook thành công cho session payment (lobby đã check-in) → server trả `500 Internal Server Error`. SePay retry, vẫn fail. State không được cập nhật (session vẫn `UNPAID`, reservation vẫn `CheckedIn`) nhưng tiền đã được giữ phía SePay → ghost reservation.

**Stack trace:**

```
fail: BoardVerse.API.Controllers.SePayWebhookController[0]
      SePay webhook processing failed.
      System.InvalidOperationException: The connection is already in a transaction
      at BoardVerse.Services.Services.ReservationService.ExecuteCompleteAndCaptureTransactionAsync(...)
      at BoardVerse.Services.Services.ReservationService.CompleteAndCaptureAsync(...)
      at BoardVerse.Services.Services.ActiveSessionService.PaySessionCoreAsync(...)
      at BoardVerse.Services.Services.PaymentService.ProcessSessionPaymentWebhookAsync(...)
```

**Root cause:** `ActiveSessionService.PaySessionCoreAsync` (outer method) đã mở 1 transaction bằng `await using var dbTx = await TryBeginTransactionAsync()` để wrap billing + status update + cleanup + capture. Bên trong, gọi `_reservationService.CompleteAndCaptureAsync(...)` → method đó gọi `ExecuteCompleteAndCaptureTransactionAsync` → lại gọi `_db.Database.BeginTransactionAsync(...)`. Trên cùng `BoardVerseDbContext` (singleton per scope), connection đã ở trong transaction → EF Core ném `InvalidOperationException`.

**Trigger condition:** Session có `LobbyId` trỏ tới lobby đã check-in (Reservation ở `CheckedIn`). Khi đó `CompleteAndCaptureAsync` được gọi từ `PaySessionCoreAsync` (đã có transaction). Session walk-in (không có Lobby) không trigger bug này.

**Fix:** Detect ambient transaction qua `_db.Database.CurrentTransaction` tại `ReservationService.ExecuteCompleteAndCaptureTransactionAsync`:
- Nếu đã có ambient tx → reuse (không gọi `BeginTransactionAsync`).
- Nếu chưa có → mở mới (giữ behavior cũ cho background jobs standalone).

**Verify:** Replay payload `BV-0A88CA2AC8164A2C` lên Neon testing branch → `200 OK`. Caveat: chưa có integration test thật trigger đầy đủ code path; cần viết test regression trước khi deploy production.

Xem chi tiết pattern ambient transaction tại [payment.md](./payment.md) §"Ambient Transaction Pattern".

---

## Liên quan

- [payment.md](./payment.md) — controller chính, có §"Ambient Transaction Pattern".
- [sepay-account.md](./sepay-account.md) — cấu hình master + cafe SePay.
- [debug-sepay.md](./debug-sepay.md) — endpoint dev/test QR + mock webhook.
- [active-session.md](./active-session.md) — controller POS (đã deprecated, gộp vào [cafe-pos.md](./cafe-pos.md)).