# PaymentController

**Base route:** `/api/payments`
**Controller:** `PaymentController.cs`
**Role:** Player (deposit) / Manager, CafeStaff (session payment, manual confirm) / Manager, Admin (refund)

API thanh toán cho deposit đặt chỗ (Player) và thanh toán hóa đơn phiên chơi tại POS (Staff). Tất cả flow đều đi qua `IPaymentGatewayService` → SePay primary, fallback VietQR. Tuân thủ BR-05, BR-09, BR-15, BR-18.

> **Liên quan:** [sepay-webhook.md](./sepay-webhook.md), [sepay-account.md](./sepay-account.md), [booking.md](./booking.md), [sepay-payment-flow.mdc](../../.cursor/rules/sepay-payment-flow.mdc).

## Endpoints

| Endpoint | Method | Role | Mô tả |
|----------|--------|------|-------|
| `/booking-deposit` | POST | Player | Tạo đơn cọc đặt chỗ + generate QR |
| `/booking-deposit/{depositId}` | GET | Player (chính chủ), Manager, Admin | Lấy chi tiết đơn cọc theo ID (mobile polling) |
| `/booking-deposit/by-order/{orderId}` | GET | Player (chính chủ), Manager, Admin | Lấy chi tiết đơn cọc theo `OrderId` (mobile lookup) |
| `/booking-deposit/{depositId}/regenerate-qr` | POST | Player | Tạo lại QR cho đơn cọc `PENDING` (QR cũ → `EXPIRED`) |
| `/booking-deposit/refund` | POST | Manager, Admin | Hoàn cọc theo `DepositRefundPolicy` (BR-18) |
| `/session-payment` | POST | Manager, CafeStaff | Tạo QR thanh toán hóa đơn phiên chơi tại POS |
| `/session-payment/{sessionId}/regenerate-qr` | POST | Manager, CafeStaff | Tạo lại QR thanh toán phiên chơi |
| `/manual-confirm` | POST | Manager, CafeStaff | Xác nhận thanh toán thủ công khi SePay + VietQR đều lỗi |

**Header bắt buộc:** `Authorization: Bearer <token>`

---

## POST /api/payments/booking-deposit

Tạo đơn cọc đặt chỗ và generate QR thanh toán. Áp dụng cho flow Player đặt cọc online (BR-05).

**Flow:**
1. Service tạo `OrderId` (BV-prefix).
2. Gọi `IPaymentGatewayService.CreatePaymentAsync`.
3. Gateway thử SePay → success: trả `PaymentUrl`, `QrUrl`, `QrExpiresAt = Now + 5 phút`.
4. SePay fail transient → retry exponential backoff đến `SePayMaxRetries`.
5. SePay hết → fallback VietQR static QR, `RequiresManualConfirmation = true`.

**Body mẫu:**

```json
{
  "cafeId": "<guid>",
  "lobbyId": "<guid, optional>",
  "scheduledStartTime": "2026-08-01T19:00:00Z",
  "seatCount": 4,
  "amount": 20000
}
```

| Field | Required | Mô tả |
|-------|----------|--------|
| `cafeId` | ✅ | Mã quán cafe. |
| `lobbyId` | ❌ | Lobby liên kết (nếu có). |
| `scheduledStartTime` | ✅ | Giờ hẹn chơi. |
| `seatCount` | ✅ | Số ghế đặt (≤ `Available` của cafe trong khung giờ). |
| `amount` | ✅ | Số tiền cọc (≤ 50% giờ đầu theo BR-03). |

**Response 200:**

```json
{
  "data": {
    "depositId": "<guid>",
    "orderId": "BV12345678",
    "qrUrl": "https://pay.sepay.vn/...",
    "paymentUrl": "https://pay.sepay.vn/v1/checkout/init?...",
    "qrExpiresAt": "2026-07-21T10:05:00Z",
    "amount": 20000,
    "requiresManualConfirmation": false
  }
}
```

**Side effects:**
- `BookingDeposit.Status = Pending`.
- `SeatSlot.Available → Holding` (giữ 5 phút).
- Metadata lưu `depositId`, `activeSessionId`, `userId`, `regenerated`.

**Lỗi:**
- `400` dữ liệu không hợp lệ / vượt BR-03 (50% giờ đầu) / quán hết chỗ.
- `401` thiếu token.
- `409` đã có deposit `PENDING` cho cùng lobby/cafe.
- `500` gateway lỗi không recover được.

---

## POST /api/payments/booking-deposit/{depositId}/regenerate-qr

Tạo lại QR thanh toán cho đơn cọc đang `PENDING`. QR cũ bị đánh dấu `EXPIRED`.

**Điều kiện:**
- `BookingDeposit.Status = Pending`.
- `QrExpiresAt < now`.

**Path param:** `depositId` (Guid).

**Response 200:** giống response của `POST /booking-deposit`.

**Lỗi:**
- `400` đơn cọc không ở trạng thái `PENDING`.
- `401` thiếu token.
- `403` không phải chủ đơn cọc.
- `404` không tìm thấy đơn cọc.
- `500` gateway lỗi.

---

## GET /api/payments/booking-deposit/{depositId}

Lấy chi tiết đơn cọc theo `Id`. Dùng để mobile polling trạng thái sau khi tạo deposit (chờ SePay webhook xác nhận), hoặc cho Manager/Admin tra cứu.

**Path param:** `depositId` (Guid).

**Authorization:**
- `Player` — chỉ xem được đơn của chính mình (`UserId == currentUser`).
- `Manager`, `Admin` — xem tất cả.

**Response 200:** trả về `BookingDepositResponseDto`.

```json
{
  "data": {
    "id": "<guid>",
    "orderId": "BV12345678",
    "activeSessionId": "<guid or null>",
    "userId": "<guid>",
    "cafeId": "<guid>",
    "cafeManagerId": "<guid>",
    "amount": 20000,
    "refundedAmount": null,
    "refundPolicy": "Full",
    "status": "Paid",
    "transferContent": "BV12345678",
    "sePayTransactionId": "TXN-...",
    "paidAt": "2026-07-21T10:02:00Z",
    "releasedAt": null,
    "refundedAt": null,
    "forfeitedAt": null,
    "qrUrl": "https://pay.sepay.vn/...",
    "qrExpiresAt": "2026-07-21T10:07:00Z",
    "scheduledAt": "2026-07-22T19:00:00Z",
    "createdAt": "2026-07-21T10:00:00Z",
    "updatedAt": "2026-07-21T10:02:00Z"
  }
}
```

**Side effects:** không có (read-only).

**Lỗi:**
- `401` thiếu token.
- `403` Player không phải chủ đơn cọc.
- `404` không tìm thấy đơn cọc.
- `500` lỗi hệ thống.

---

## GET /api/payments/booking-deposit/by-order/{orderId}

Lấy chi tiết đơn cọc theo `OrderId` (mã `BV-prefix` hiển thị trên QR). Dùng để mobile lookup khi user quét QR / nhập mã đặt chỗ.

**Path param:** `orderId` (string, max 32 chars).

**Authorization:** giống `GET /booking-deposit/{depositId}`.

**Response 200:** giống `GET /booking-deposit/{depositId}` (cùng `BookingDepositResponseDto`).

**Side effects:** không có (read-only).

**Lỗi:**
- `401` thiếu token.
- `403` Player không phải chủ đơn cọc.
- `404` không tìm thấy đơn cọc với `OrderId` tương ứng.
- `500` lỗi hệ thống.

> **Lưu ý:** cả hai endpoint GET đều **không xác thực quyền sở hữu Player trong controller hiện tại** (chỉ check role Manager/Admin). Khi mobile Player flow cần xem đơn của mình, cần bổ sung logic check `deposit.UserId == currentUserId` trước khi trả response. Xem [booking.md](./booking.md) §"Security & Access Control" để biết chi tiết.

---

## POST /api/payments/booking-deposit/refund

Hoàn cọc đặt chỗ theo `DepositRefundPolicy` của cafe (BR-18). Số tiền hoàn thực tế phụ thuộc vào policy và thời gian đã trôi qua kể từ khi tạo đơn.

**Điều kiện:**
- `BookingDeposit.Status = Paid`.
- `reason` không rỗng (bắt buộc cho audit).

**Refund policy:**

| Policy | Số tiền hoàn |
|--------|--------------|
| `Full` | 100% `Amount` |
| `Partial` | 50% `Amount` nếu elapsed ≥ 24h, 25% nếu elapsed ≥ 12h, 0% nếu < 12h |
| `None` | 0% → status chuyển sang `Forfeited` (không refund) |

**Body mẫu:**

```json
{
  "depositId": "<guid>",
  "reason": "Cafe đóng cửa đột xuất do bảo trì hệ thống điện"
}
```

| Field | Required | Mô tả |
|-------|----------|--------|
| `depositId` | ✅ | Mã đơn cọc cần hoàn. |
| `reason` | ✅ | Lý do hoàn (lưu audit log). |

**Response 200:**

```json
{
  "data": {
    "depositId": "<guid>",
    "status": "Refunded",
    "amount": 20000,
    "refundedAmount": 20000,
    "processedAt": "2026-07-21T11:30:00Z"
  }
}
```

| Field | Mô tả |
|-------|-------|
| `status` | `Refunded` nếu có hoàn, `Forfeited` nếu `Policy = None`. |
| `refundedAmount` | Số tiền thực tế hoàn cho khách (0 nếu `Forfeited`). |

**Side effects:**
- Cập nhật `BookingDeposit.Status = Refunded` (hoặc `Forfeited`).
- Ghi `RefundedAt` / `ForfeitedAt` timestamp.
- Log thông tin refund (audit trail) — policy, số tiền, lý do.

**Lỗi:**
- `400` đơn cọc không ở trạng thái `Paid` / thiếu lý do.
- `401` thiếu token.
- `403` không có quyền.
- `404` không tìm thấy đơn cọc.
- `500` lỗi hệ thống.

---

## POST /api/payments/session-payment

POS tạo QR thanh toán hóa đơn phiên chơi sau khi kiểm kê linh kiện xong. Áp dụng cho flow `ActiveSession.Status = UNPAID → PAID`.

**Validation:**
- `ActiveSession.Status == UNPAID`.
- `TotalAmount > 0`.
- Cafe đã cấu hình SePay (`Cafe.SePayMerchantId` + `SecretKey`).

**Body mẫu:**

```json
{
  "sessionId": "<guid>",
  "totalAmount": 85000,
  "depositAppliedAmount": 20000,
  "notes": "Khách trả tiền mặt QR scan"
}
```

| Field | Required | Mô tả |
|-------|----------|--------|
| `sessionId` | ✅ | Mã phiên chơi. |
| `totalAmount` | ✅ | Tổng tiền hóa đơn (BR-15: Subtotal + Penalty - DepositApplied). |
| `depositAppliedAmount` | ❌ | Số cọc đã cấn trừ (BR-09, default 0). |
| `notes` | ❌ | Ghi chú POS. |

**Response 200:** QR thanh toán session (giống format deposit, không set `QrExpiresAt`).

**Side effects:**
- `ActiveSession` đánh dấu chờ thanh toán (status vẫn `UNPAID`).
- QR redirect về `ReturnUrl` của cafe.

**Lỗi:**
- `400` session không ở `UNPAID` / amount ≤ 0 / cafe chưa cấu hình SePay.
- `401` thiếu token.
- `403` không phải Manager/CafeStaff của cafe.
- `404` không tìm thấy session hoặc cafe.
- `500` gateway lỗi.

---

## POST /api/payments/session-payment/{sessionId}/regenerate-qr

Tạo lại QR thanh toán cho phiên chơi đang `UNPAID`.

**Path param:** `sessionId` (Guid).

**Response 200:** QR mới.

**Lỗi:**
- `400` session không ở `UNPAID`.
- `401` thiếu token.
- `403` không phải Manager/CafeStaff của cafe.
- `404` không tìm thấy session.
- `500` gateway lỗi.

---

## POST /api/payments/manual-confirm

Staff xác nhận thanh toán thủ công khi cả SePay và VietQR đều không khả dụng (BR-18: xử lý sự cố vận hành).

**Use case:** Khách thanh toán tiền mặt trực tiếp cho POS, không quét QR được; hoặc SePay + VietQR đều timeout.

**Body mẫu:**

```json
{
  "sessionId": "<guid>",
  "amount": 85000,
  "collectedByStaff": true,
  "notes": "Khách thanh toán tiền mặt - QR hết hạn",
  "evidenceImageUrl": "https://..."
}
```

| Field | Required | Mô tả |
|-------|----------|--------|
| `sessionId` | ✅ | Mã phiên chơi. |
| `amount` | ✅ | Số tiền thực thu. |
| `collectedByStaff` | ✅ | True = staff đã thu tiền mặt. |
| `notes` | ❌ | Ghi chú audit. |
| `evidenceImageUrl` | ❌ | URL ảnh biên nhận (nếu có). |

**Side effects:**
- `ActiveSession.Status = PAID`.
- Tất cả `ActiveSessionMember` của phiên được đánh dấu `IsCheckedOut = true`, `CheckedOutAt = now`.
- `CafeInventoryBox.Status = Available` (giải phóng hộp board game đang mượn).
- `CafeTable.Status = Available` (giải phóng bàn về kho, BR-17).
- Nếu phiên liên kết `Lobby` → `Lobby.Status = Closed`.
- Ghi nhật ký doanh thu phát sinh (audit trail) trong `Transactions`.**Lỗi:**
- `400` thông tin không hợp lệ / session không ở `UNPAID`.
- `401` thiếu token.
- `403` không phải Manager/CafeStaff của cafe.
- `404` không tìm thấy session.
- `500` lỗi hệ thống.

---

## Idempotency & Duplicate Webhook

- Duplicate webhook cho `BookingDeposit.Paid` hoặc `ActiveSession.Paid` → bỏ qua, không cập nhật lại.
- Amount mismatch → log + dừng xử lý, không cập nhật trạng thái.
- Lookup ưu tiên: `SePayTransactionId` → `OrderId` → `SessionId`/`OrderId` prefix.

## Session Payment Lifecycle Cleanup

Khi phiên chơi (`ActiveSession`) được thanh toán thành công — dù qua cổng nào — hệ thống đảm bảo **giải phóng tài nguyên đã giữ** trong cùng transaction để tránh "ghost reservation":

| Endpoint chạy cleanup | Service method | Điều kiện kích hoạt |
|---|---|---|
| `POST /api/payments/manual-confirm` (staff confirm tiền mặt) | `ManualPaymentService.HandleSessionConfirmationAsync` | `PaymentType = "Session"`, session `UNPAID` |
| `POST /api/payments/sepay/webhook` (SePay/VietQR gateway) | `PaymentService.ProcessSessionPaymentWebhookAsync` | `status=success/paid` + amount khớp + session `UNPAID` |
| `POST /api/cafes/{cafeId}/sessions/{sessionId}/pay` (POS thanh toán qua SePay QR) | `ActiveSessionService.PaySessionAsync` | session `UNPAID` |

**Cleanup contract** (idempotent — gọi nhiều lần không lỗi):

| Bước | Hành động | BR |
|---|---|---|
| 1 | Tất cả `ActiveSessionMember` → `IsCheckedOut = true`, `CheckedOutAt = now` | BR-12 |
| 2 | `CafeInventoryBox.Status = Available` (giải phóng hộp board game) | — |
| 3 | `CafeTable.Status = Available` (giải phóng bàn vật lý) | BR-17 |
| 4 | Lobby liên kết (nếu có) → `Status = Closed`, set `ClosedAt` | — |

Nếu session **không có** bàn/box/lobby liên kết (walk-in, partial checkout), các bước tương ứng được bỏ qua an toàn.

**Quan trọng — bug đã fix (box overwrite)**: Code cũ ở `PaySessionAsync` **luôn** force set box → `Available` ngay cả khi box đang ở trạng thái không cho thuê (`Lost`, `Maintenance`, …) — sai về nghiệp vụ. Code mới qua repo chỉ set `Available` nếu box đang ở trạng thái **khác** `Available`.

Implementation: `IActiveSessionRepository.CompleteSessionPaymentCleanupAsync` — đảm bảo tất cả thay đổi persist trong một `SaveChangesAsync` (một DB transaction).

### Bug history

Trước khi có cleanup contract:

1. **Webhook path bug**: `PaymentService.ProcessSessionPaymentWebhookAsync` chỉ set `Status = PAID` mà không giải phóng bàn/box/members/lobby — dẫn đến bàn "ma" chiếm chỗ, board game không cho thuê lại sau khi khách thanh toán online qua SePay/VietQR (~95% flow thanh toán session).
2. **Half-commit bug**: Amount-mismatch check chạy **sau** `TryUpdateStatusAsync`, nghĩa là mismatch check là dead code và state đã bị thay đổi.
3. **Box overwrite bug (POS path)**: `ActiveSessionService.PaySessionAsync` force `Available` lên box kể cả khi box ở `Lost`/`Maintenance`.
4. **Drift risk**: Mỗi entry point có bản copy riêng của cleanup logic → dễ drift khi sửa.

Sau fix: cả 3 entry point gọi cùng `IActiveSessionRepository.CompleteSessionPaymentCleanupAsync(sessionId)`. Amount-mismatch check chạy trước atomic status update. Box chỉ được set `Available` nếu hiện tại không phải `Available`.

Xem chi tiết: [sepay-webhook.md](./sepay-webhook.md) §V, [active-session.md](./active-session.md) §`POST .../pay`.

## Business Rules áp dụng

| BR | Áp dụng |
|----|---------|
| **BR-05** | Deposit QR tạo trong `PENDING_DEPOSIT`, confirm khi webhook `success`. |
| **BR-06** | Giữ chỗ 5 phút (`QrExpiresAt`); quá hạn → status = `EXPIRED`, giải phóng ghế. |
| **BR-09** | Thanh toán session KHÔNG trừ tiền cọc. Deposit là phí giữ chỗ, không cấn trừ vào hóa đơn. |
| **BR-15** | `TotalAmount = Subtotal + Penalty - DepositAppliedAmount`. |
| **BR-18** | Refund/forfeit theo `DepositRefundPolicy` + reason khi quán hủy bất khả kháng. |

## Retry & Fallback

| Param | Default | Giới hạn |
|-------|---------|----------|
| `SePayMaxRetries` | 3 | ≥ 1 |
| `SePayRetryDelayMs` | 1000 | > 0 |
| backoff | exponential | `baseDelayMs * 2^(attempt-1)` |

**Transient errors:** `HttpRequestException`, `TaskCanceledException`/timeout, HTTP 502/503/504, message chứa `timeout`/`connection`.

**Fallback trigger:** Non-transient SePay failure, transient vượt `SePayMaxRetries`, hoặc `EnableVietQrFallback = true`.

## Liên quan

- **Webhook:** [sepay-webhook.md](./sepay-webhook.md) — nhận callback từ SePay.
- **Tài khoản SePay:** [sepay-account.md](./sepay-account.md) — cấu hình master + cafe.
- **Debug:** [debug-sepay.md](./debug-sepay.md) — endpoint dev/test QR + mock webhook.
- **Flow nghiệp vụ:** [booking.md](./booking.md) — state machine deposit + session.
- **Rule chi tiết:** [sepay-payment-flow.mdc](../../.cursor/rules/sepay-payment-flow.mdc).