# Wallet API

**Controller:** `WalletController.cs` (`/api/v1/wallet`)
**Role:** Player — đã đăng nhập (JWT)

Quản lý ví BVC (BoardVerse Coin) + sổ cái ledger. Phase 1 theo `lobby-booking-deposit-bvc.mdc` § XXI-G.

> **Liên quan:**
> - `.cursor/rules/lobby-booking-deposit-bvc.mdc` (rule chính)
> - `docs/api/payment.md` (luồng BookingDeposit SePay VND cũ, vẫn giữ)
> - `docs/api/reservation.md` (Phase 3 sẽ dùng ledger hold/capture/release/forfeit)

---

## Mục lục

1. [Khái niệm BVC](#khái-niệm-bvc)
2. [End-to-end flow](#end-to-end-flow)
3. [Endpoints](#endpoints)
4. [State machine](#state-machine)
5. [Idempotency](#idempotency)
6. [Quy tắc BR áp dụng](#quy-tắc-br-áp-dụng)

---

## Khái niệm BVC

| Thuật ngữ | Định nghĩa |
|---|---|
| **BVC** | BoardVerse Coin. 1 BVC = 1.000 VND cố định. |
| `availableBalance` | BVC có thể dùng để đặt cọc. |
| `heldBalance` | BVC đang bị giữ cho reservation/lobby (Phase 3+). |
| `riskLevel` | low / medium / high / critical — chỉ enum, không lộ điểm (BR-RISK-09). |
| `accountStatus` | active / warning / restricted / suspended / banned. |

---

## End-to-end flow

```
Top-up:
  1. POST /api/v1/wallet/topup { amountVnd: 100000, idempotencyKey: "abc" }
     → Backend mở SePay master
     → Trả { paymentUrl, qrUrl, orderId, expectedBvc: 100, expiresAt }
     → Lưu BvcTopUpRequest(status=Pending) để tracking webhook
  2. Khách quét QR / mở SePay → thanh toán 100.000 VND.
  3. SePay gửi webhook → PaymentService.HandleSePayWebhookAsync
     → OrderId prefix "BVC-" → WalletService.HandleTopUpWebhookAsync
     → Lookup BvcTopUpRequest theo OrderId (cluster-safe, idempotent theo IdempotencyKey)
     → success: cộng availableBalance, ghi ledger TopUp, mark TopUpRequest.Status=Paid.
     → failed/cancelled: chỉ mark TopUpRequest.Status=Failed (không cộng ví).
  4. Mobile poll GET /api/v1/wallet để kiểm tra balance đã cộng chưa.

Đổi số tiền top-up (player nhập lộn):
  PATCH /api/v1/wallet/topup/{topUpId} { amountVnd: 50000, idempotencyKey: "new-key" }
  → Đơn cũ: status = Cancelled (local flag, webhook SePay tới sẽ tự reject)
  → Đơn mới: tạo BvcTopUpRequest mới + SePay PaymentUrl mới
  → Trả { paymentUrl, qrUrl, orderId, expectedBvc, expiresAt } cho đơn mới

Hủy top-up (player đổi ý):
  DELETE /api/v1/wallet/topup/{topUpId}
  → Đơn: status = Cancelled (local flag)
  → Webhook SePay tới sau sẽ bị reject tự động (status != Pending)
  → Nếu player đã lỡ chuyển khoản → admin support xử lý refund thủ công

Admin cộng/trừ thủ công:
  POST /api/v1/admin/wallet/adjust { targetUserId, amountBvc, isCredit, reason, idempotencyKey }
  [Role: Admin]
  → WalletService.AdminAdjustBalanceAsync
  → Ledger AdminCredit (+) / AdminDebit (-), KHÔNG qua SePay.
  → Idempotent theo idempotencyKey.
```

---

## Endpoints

| Method | Path | Role | Mô tả |
|--------|------|------|--------|
| `GET` | `/api/v1/wallet` | Player | Lấy ví (auto-create nếu chưa có) |
| `POST` | `/api/v1/wallet/topup` | Player | Tạo đơn top-up BVC từ VND |
| `PATCH` | `/api/v1/wallet/topup/{topUpId}` | Player | Đổi số tiền đơn top-up đang Pending (chưa thanh toán) |
| `DELETE` | `/api/v1/wallet/topup/{topUpId}` | Player | Hủy đơn top-up đang Pending (chưa thanh toán) |
| `GET` | `/api/v1/wallet/transactions` | Player | Lịch sử ledger phân trang |
| `POST` | `/api/v1/wallet/refund-requests` | Player | Gửi yêu cầu hoàn BVC (liên kết ledger entry) |
| `GET` | `/api/v1/wallet/refund-requests` | Player | Lịch sử yêu cầu hoàn BVC của player (phân trang) |
| `DELETE` | `/api/v1/wallet/refund-requests/{requestId}` | Player | Hủy yêu cầu hoàn đang Pending do player tạo |

**Header bắt buộc:** `Authorization: Bearer <token>`

---

## GET `/api/v1/wallet`

Lấy ví BVC của player đang đăng nhập. **Auto-create ví rỗng** nếu user lần đầu truy cập.

### Query

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `includeHeld` | bool | No | `true` để trả `heldBalance`. Mặc định `false`. |

### Response `200`

```json
{
  "userId": "<guid>",
  "availableBalance": 0,
  "riskLevel": "Low",
  "isCoolingOff": false,
  "accountStatus": "Active"
}
```

Nếu `includeHeld=true`:

```json
{
  "userId": "<guid>",
  "availableBalance": 80,
  "heldBalance": 120,
  "riskLevel": "Low",
  "isCoolingOff": false,
  "accountStatus": "Active"
}
```

### Error

| Status | Mô tả |
|--------|--------|
| `401` | Thiếu token / token hết hạn. |
| `500` | Lỗi hệ thống. |

---

## POST `/api/v1/wallet/topup`

Tạo đơn top-up BVC qua SePay master account.

### Body

```json
{
  "amountVnd": 100000,
  "idempotencyKey": "uuid-v4"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `amountVnd` | long | ✅ | Số tiền VND. ≥ 10.000 và chia hết cho 1.000. BVC nhận = `amountVnd / 1000`. |
| `idempotencyKey` | string | ✅ | 8–128 ký tự. UNIQUE. Dùng để chống double-tap. |

**Validate:**
- `amountVnd < 10.000` → 400
- `amountVnd % 1.000 != 0` → 400
- Tài khoản `suspended` / `banned` → 403
- SePay gateway fail → 500

### Response `200`

```json
{
  "paymentUrl": "https://pay.sepay.vn/...",
  "qrUrl": "https://qr.sepay.vn/...",
  "orderId": "BVC-A1B2C3D4E5",
  "expectedBvc": 100,
  "expiresAt": "2026-08-02T17:30:00Z",
  "idempotencyKey": "uuid-v4"
}
```

### Error

| Status | Mô tả |
|--------|--------|
| `400` | amount dưới min / không hợp lệ. |
| `401` | Thiếu token. |
| `403` | Tài khoản bị hạn chế. |
| `500` | Lỗi hệ thống / SePay gateway fail. |

---

## PATCH `/api/v1/wallet/topup/{topUpId}`

Đổi số tiền đơn top-up BVC đang Pending (chưa thanh toán). Dùng khi player nhập lộn `amountVnd` và muốn chọn lại số tiền trước khi quét QR.

**Hành vi:**
1. Validate `amountVnd` (min 10.000, bội số 1.000) — trả 400 nếu sai.
2. Tìm `BvcTopUpRequest` theo `topUpId` — 404 nếu không tồn tại.
3. Check ownership — 403 nếu không phải chủ đơn.
4. Check status — 409 nếu đơn không ở Pending (đã Paid/Expired/Failed/Cancelled).
5. Check idempotency key mới không trùng đơn khác — 409 nếu trùng.
6. Set đơn cũ = `Cancelled` (webhook SePay sau sẽ tự reject).
7. Tạo đơn mới với SePay PaymentUrl + OrderId mới.
8. Trả `TopUpResponseDto` của đơn mới.

### Path

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `topUpId` | Guid | ✅ | Id của `BvcTopUpRequest` cần đổi. |

### Body

```json
{
  "amountVnd": 50000,
  "idempotencyKey": "uuid-v4-moi"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `amountVnd` | long | ✅ | Số tiền VND mới. ≥ 10.000 và chia hết cho 1.000. |
| `idempotencyKey` | string | ✅ | 8–128 ký tự. KHÁC với key của đơn cũ. UNIQUE. |

**Validate:**
- `amountVnd < 10.000` → 400
- `amountVnd % 1.000 != 0` → 400
- `idempotencyKey` rỗng / thiếu → 400
- `idempotencyKey` đã dùng cho đơn khác → 409

### Response `200`

```json
{
  "paymentUrl": "https://pay.sepay.vn/...",
  "qrUrl": "https://qr.sepay.vn/...",
  "orderId": "BVC-F6G7H8I9J0",
  "expectedBvc": 50,
  "expiresAt": "2026-08-02T17:30:00Z",
  "idempotencyKey": "uuid-v4-moi"
}
```

### Error

| Status | Mô tả |
|--------|--------|
| `400` | `amountVnd` dưới min / không hợp lệ. |
| `401` | Thiếu token. |
| `403` | Không phải chủ đơn. |
| `404` | Không tìm thấy đơn top-up. |
| `409` | Đơn không ở Pending, hoặc idempotency key đã dùng. |
| `500` | Lỗi hệ thống / SePay gateway fail. |

---

## DELETE `/api/v1/wallet/topup/{topUpId}`

Hủy đơn top-up BVC đang Pending (chưa thanh toán). Player dùng khi đổi ý không muốn nạp nữa.

**Hành vi:**
1. Tìm `BvcTopUpRequest` theo `topUpId` — 404 nếu không tồn tại.
2. Check ownership — 403 nếu không phải chủ đơn.
3. Check status — 409 nếu đơn đã terminal (Paid/Expired/Failed/Cancelled).
4. Set `Status = Cancelled` (local flag).
5. Webhook SePay tới sau sẽ tự động bị reject (logic `status != Pending → skip` ở `HandleTopUpWebhookAsync`).

**Lưu ý quan trọng:**
- Tại thời điểm cancel, QR SePay vẫn **chưa hết hạn trên SePay master** (vì SePay client hiện không có endpoint cancel order). Nếu player đã lỡ chuyển khoản, webhook sẽ bị reject vì status mismatch — player phải liên hệ admin support để manual refund.
- Backend chỉ set local flag `Status = Cancelled`. Tiền VND chưa được chuyển vào BoardVerse cho đến khi SePay webhook tới.

### Path

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `topUpId` | Guid | ✅ | Id của `BvcTopUpRequest` cần hủy. |

### Response `200`

```json
{
  "code": 200,
  "message": "Hủy đơn top-up BVC thành công.",
  "data": null
}
```

### Error

| Status | Mô tả |
|--------|--------|
| `401` | Thiếu token. |
| `403` | Không phải chủ đơn. |
| `404` | Không tìm thấy đơn top-up. |
| `409` | Đơn không ở Pending (đã Paid/Expired/Failed/Cancelled). |
| `500` | Lỗi hệ thống. |

---

## GET `/api/v1/wallet/transactions`

Lịch sử ledger BVC của player đang đăng nhập. Sắp xếp mới nhất trước. Phân trang.

### Query

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `page` | int | No | Số trang (≥ 1). Mặc định 1. |
| `pageSize` | int | No | Số entry / trang (1-100). Mặc định 20. |

### Response `200`

```json
{
  "items": [
    {
      "id": "<guid>",
      "type": "TopUp",
      "amount": 100,
      "relatedLobbyId": null,
      "relatedBookingId": null,
      "relatedPaymentRef": "BVC-A1B2C3D4E5",
      "balanceSnapshot": 150,
      "note": null,
      "createdAt": "2026-08-02T15:30:00Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalItems": 1,
  "hasMore": false
}
```

**Field `type` (Phase 1 chỉ dùng `TopUp`):**

| Value | Ý nghĩa |
|-------|--------|
| `TopUp` | Nạp tiền thật → BVC. |
| `DepositHold` | (Phase 3+) Giữ cọc reservation. |
| `DepositRelease` | (Phase 3+) Hoàn cọc do timeout/hủy. |
| `DepositCapture` | (Phase 3+) Capture cọc sau check-in. |
| `DepositForfeit` | (Phase 3+) Tịch thu cọc do no-show. |
| `Adjustment` | Sửa sai — chỉ do admin. |
| `AdminCredit` | Admin cộng BVC thủ công (compensation). |
| `AdminDebit` | Admin trừ BVC thủ công (penalty / manual refund). |

---

## State machine

**BvcTopUpRequest.Status** lifecycle:

```
Pending → Paid (webhook success) → terminal
Pending → Expired (qua 10 phút, cron ExpiredPendingTopUpsAsync) → terminal
Pending → Failed (webhook failed/cancelled) → terminal
Pending → Cancelled (player chủ động DELETE /api/v1/wallet/topup/{id}) → terminal
```

Webhook SePay tới luôn check `Status != Pending → skip` (idempotency), nên **mọi status terminal (Paid/Expired/Failed/Cancelled) đều tự bị reject khỏi xử lý tiền**.

**Wallet.AvailableBalance** thay đổi theo ledger entry:

| Ledger type | availableBalance | heldBalance |
|-------------|------------------|-------------|
| `TopUp` | `+= amount` | — |
| `DepositHold` | `-= amount` | `+= amount` |
| `DepositRelease` | `+= amount` | `-= amount` |
| `DepositCapture` | — | `-= amount` |
| `DepositForfeit` | — | `-= amount` |
| `Adjustment` | tùy dấu admin | tùy dấu admin |

**Ledger bất biến** — không UPDATE/DELETE dòng đã ghi (BR § III.3).

---

## Idempotency

Theo BR § XVII.1, mọi request quan trọng (top-up / confirm / cancel / refund) đều có `idempotencyKey`. Cơ chế:

- Nếu `IdempotencyKey` đã có trong `BvcLedgerEntries` → backend trả response cũ.
- Unique constraint ở DB `UX_BvcLedgerEntries_IdempotencyKey` đảm bảo double-tap không ghi entry trùng.
- Client khuyến nghị dùng UUID v4 sinh mỗi lần user bấm nút.

---

## Quy tắc BR áp dụng

| BR | Áp dụng ở Wallet |
|----|---|
| BR § II.1 | Tỷ lệ 1 BVC = 1.000 VND cố định. |
| BR § II.2 | Min top-up 10.000 VND, bội số 1.000. |
| BR § II.2 (no bonus) | 100.000 VND → 100 BVC (không cộng thêm). |
| BR § III.1 | 2 số dư `availableBalance` + `heldBalance`. |
| BR § III.2 | Ledger entry types: TopUp, DepositHold, … |
| BR § III.3 | Append-only; có `balanceSnapshot`; UNIQUE `idempotencyKey`. |
| BR-RISK-04 | Validate `accountStatus` trước top-up. |
| BR-RISK-09 | User chỉ thấy `riskLevel`, không thấy `riskScore`. |
| BR § XVII.1 | Idempotency key cho mọi request quan trọng. |

---

## Còn lại (Phase 2+)

| Phase | Bổ sung |
|-------|---------|
| Phase 2 | Reservation Quote (chưa liên quan trực tiếp wallet). |
| Phase 3 | `POST /api/v1/reservations/confirm` → atomic transaction gồm `DepositHold` + reservation + lobby. |
| Phase 5 | Cancel / no-show → `DepositRelease` / `DepositForfeit`. |
| Phase 6 | POS check-in → `DepositCapture`. |
| Phase 7 | Admin reset / cooling-off → `Adjustment` + cập nhật `accountStatus`. |

---

## BVC Refund Request — Player

**Controller:** `WalletController.cs` (`/api/v1/wallet/refund-requests`)
**Role:** Player — đã đăng nhập (JWT)

Player gửi yêu cầu hoàn BVC cho một ledger entry (vd. top-up nhầm số tiền, hold cọc không cần thiết...). Admin sẽ xét duyệt thủ công. Đảm bảo BR-RISK-05 (mọi admin resolve đều ghi `PlayerActionHistory` vĩnh viễn) + BR § III.3 (ledger append-only).

**Scope giới hạn (MVP):**
- Chỉ áp dụng cho ledger entry do player sở hữu.
- Số BVC player yêu cầu **không vượt** `Amount` của ledger entry liên kết (nếu vượt → admin điều chỉnh `ApprovedAmountBvc` khi duyệt).
- Player chỉ tạo tối đa 1 yêu cầu `Pending` cho mỗi ledger entry (mỗi ledger entry có thể có nhiều yêu cầu theo thời gian, nhưng chỉ 1 đang chờ).
- Idempotency theo `Idempotency-Key` header (BR § XVII.1).

---

### POST `/api/v1/wallet/refund-requests`

Gửi yêu cầu hoàn BVC mới. Trỏ tới 1 ledger entry của player (vd. `BvcTopUpRequest` đã paid, hoặc `DepositHold` không cần giữ nữa).

**Header bắt buộc:** `Idempotency-Key: <string 8-128 ký tự>`.

**Body:**

```json
{
  "relatedLedgerEntryId": "<guid>",
  "requestedAmountBvc": 50000,
  "playerReason": "Tôi top-up nhầm số tiền 200k thay vì 100k, xin admin hoàn lại 100k thừa."
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `relatedLedgerEntryId` | Guid | ✅ | Id của `BvcLedgerEntry` player muốn hoàn. Phải thuộc user hiện tại. |
| `requestedAmountBvc` | long | ✅ | Số BVC player yêu cầu hoàn. 1 → 10.000.000. |
| `playerReason` | string | ✅ | Lý do, 20-2000 ký tự. Bắt buộc đủ dài để admin có đủ thông tin xét duyệt. |

**Validate:**
- Thiếu `Idempotency-Key` → 400
- `requestedAmountBvc <= 0` → 400
- `playerReason` < 20 ký tự → 400
- Ledger entry không tồn tại → 404
- Ledger entry không thuộc player hiện tại → 403

**Response `201`:**

```json
{
  "id": "<guid>",
  "userId": "<guid>",
  "relatedLedgerEntryId": "<guid>",
  "requestedAmountBvc": 50000,
  "approvedAmountBvc": null,
  "playerReason": "Tôi top-up nhầm số tiền 200k...",
  "adminNote": null,
  "status": "Pending",
  "resolvedByAdminId": null,
  "resolvedAt": null,
  "resultLedgerEntryId": null,
  "createdAt": "2026-08-08T15:00:00Z",
  "updatedAt": "2026-08-08T15:00:00Z"
}
```

**Idempotency:**
- Cùng `Idempotency-Key` + cùng `userId` → trả request cũ (no double-create).
- Cùng `Idempotency-Key` + khác `userId` → 409 (conflict).
- Player có thể tạo lại sau khi request cũ ở terminal state (Approved/Rejected/Cancelled).

**Error:**

| Status | Mô tả |
|--------|--------|
| `400` | Thiếu `Idempotency-Key`, `requestedAmountBvc <= 0`, hoặc `playerReason` quá ngắn. |
| `401` | Thiếu token. |
| `403` | Ledger entry không thuộc player. |
| `404` | Ledger entry không tồn tại. |
| `409` | `Idempotency-Key` đã dùng bởi user khác. |
| `500` | Lỗi hệ thống. |

---

### GET `/api/v1/wallet/refund-requests`

Lấy danh sách yêu cầu hoàn BVC của player hiện tại. Sắp xếp mới nhất trước.

**Query:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `page` | int | ❌ | 1 | Số trang (≥ 1). |
| `pageSize` | int | ❌ | 20 | Số item / trang (1-100). |

**Response `200`:**

```json
{
  "items": [
    {
      "id": "<guid>",
      "userId": "<guid>",
      "relatedLedgerEntryId": "<guid>",
      "requestedAmountBvc": 50000,
      "approvedAmountBvc": 50000,
      "playerReason": "...",
      "adminNote": "Đã xác nhận top-up nhầm, hoàn 50k",
      "status": "Approved",
      "resolvedByAdminId": "<admin-guid>",
      "resolvedAt": "2026-08-08T16:00:00Z",
      "resultLedgerEntryId": "<ledger-guid>",
      "createdAt": "2026-08-08T15:00:00Z",
      "updatedAt": "2026-08-08T16:00:00Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalItems": 1,
  "totalPages": 1
}
```

---

### DELETE `/api/v1/wallet/refund-requests/{requestId}`

Player tự hủy yêu cầu hoàn đang Pending (do player tạo). Sau khi admin resolve thì không hủy được nữa.

**Path:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `requestId` | Guid | ✅ | Id của `BvcRefundRequest` cần hủy. |

**Validate:**
- Request không tồn tại → 404
- Không phải chủ request → 403
- Status ≠ `Pending` (đã Approved/Rejected/Cancelled) → 409

**Response `200`:** trả `RefundRequestResponseDto` với `Status = Cancelled`.

**Error:**

| Status | Mô tả |
|--------|--------|
| `401` | Thiếu token. |
| `403` | Không phải chủ request. |
| `404` | Request không tồn tại. |
| `409` | Request đã ở terminal state. |

---

## Admin — Wallet Adjust

**Controller:** `AdminWalletController.cs` (`/api/v1/admin/wallet`)
**Role:** Admin (theo BR-RISK-07).

### POST `/api/v1/admin/wallet/adjust`

Cộng/trừ BVC thủ công cho một user. Dùng cho compensation, manual refund, support adjustment.
KHÔNG qua SePay — ghi thẳng ledger `AdminCredit` (+) hoặc `AdminDebit` (-).
Idempotent theo `IdempotencyKey` (BR § XVII.1).

**Audit:** mỗi entry ghi `Note = "[Admin:{adminUserId}] ±{amount} BVC — {reason}"` để truy vết sau này.

### Body

```json
{
  "targetUserId": "<guid>",
  "amountBvc": 500,
  "isCredit": true,
  "reason": "Compensation cho user VIP bị nhầm suspend",
  "idempotencyKey": "uuid-v4"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `targetUserId` | Guid | ✅ | UserId được điều chỉnh ví. |
| `amountBvc` | long | ✅ | Số BVC (1 → 10.000.000), luôn dương. |
| `isCredit` | bool | ✅ | `true` = cộng, `false` = trừ. |
| `reason` | string | ✅ | Lý do (5-512 ký tự) — bắt buộc cho audit. |
| `idempotencyKey` | string | ✅ | 8-128 ký tự. UNIQUE. |

### Response `200`

```json
{
  "ledgerEntryId": "<guid>",
  "newAvailableBalance": 600,
  "newHeldBalance": 0,
  "balanceSnapshot": 600,
  "wasIdempotentReplay": false
}
```

### Error

| Status | Mô tả |
|--------|--------|
| `400` | `amountBvc ≤ 0` hoặc `reason` rỗng. |
| `401` | Thiếu token. |
| `403` | Không phải Admin. |
| `500` | Lỗi hệ thống. |

**Lưu ý:**
- `AdminDebit` với số dư không đủ → 400 (không cho phép âm).
- Auto-create ví rỗng cho user chưa có ví trước khi cộng/trừ.
- Nếu idempotencyKey đã tồn tại → trả kết quả cũ (no double-mutate).

---

## Admin — Wallet Listing

**Controller:** `AdminWalletController.cs` (`/api/v1/admin/wallet`)
**Role:** Admin (theo BR-RISK-07).
**Auth:** `[Authorize(Roles = "Admin")]`.

### GET `/api/v1/admin/wallet`

Lấy danh sách tất cả wallets (phân trang, filter).

**Query Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `page` | int | ❌ | `1` | Số trang. |
| `pageSize` | int | ❌ | `20` | Số item / trang (max `100`). |
| `searchTerm` | string | ❌ | `null` | Tìm theo email, full name, hoặc userId. |
| `statusFilter` | `AccountStatus` | ❌ | `null` | Lọc theo trạng thái tài khoản. |
| `riskLevelFilter` | `RiskLevel` | ❌ | `null` | Lọc theo mức rủi ro. |

**Enum values:**

- `AccountStatus`: `Active` (0), `Warning` (1), `Restricted` (2), `Suspended` (3), `Banned` (4).
- `RiskLevel`: `Low` (0), `Medium` (1), `High` (2), `Critical` (3).

**Response `200`:**

```json
{
  "items": [
    {
      "userId": "<guid>",
      "userEmail": "player@example.com",
      "availableBalance": 1500,
      "heldBalance": 200,
      "totalActiveDeposit": 200,
      "riskMultiplier": 1.0,
      "riskLevel": "Low",
      "isCoolingOff": false,
      "accountStatus": "Active",
      "createdAt": "2026-07-15T10:00:00Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalItems": 47,
  "totalPages": 3
}
```

**Error:**

| Status | Mô tả |
|--------|--------|
| `401` | Thiếu token. |
| `403` | Không phải Admin. |

---

### GET `/api/v1/admin/wallet/{userId}`

Lấy chi tiết wallet của 1 user (bao gồm thông tin user + risk profile).

**Path Parameters:**

| Name | Type | Description |
|------|------|-------------|
| `userId` | Guid | UserId cần xem. |

**Response `200`:**

```json
{
  "userId": "<guid>",
  "userEmail": "player@example.com",
  "userPhoneNumber": "+84xxxxxxxxx",
  "availableBalance": 1500,
  "heldBalance": 200,
  "totalActiveDeposit": 200,
  "riskMultiplier": 1.0,
  "riskScore": 25,
  "riskLevel": "Low",
  "isCoolingOff": false,
  "coolingOffExpiresAt": null,
  "accountStatus": "Active",
  "createdAt": "2026-07-15T10:00:00Z",
  "updatedAt": "2026-08-01T14:30:00Z"
}
```

**Error:**

| Status | Mô tả |
|--------|--------|
| `404` | User không có ví BVC. |

---

### GET `/api/v1/admin/wallet/{userId}/transactions`

Lấy lịch sử ledger entries của 1 user (phân trang). Dùng để đối soát, kiểm tra hành vi user.

**Path Parameters:**

| Name | Type | Description |
|------|------|-------------|
| `userId` | Guid | UserId cần xem lịch sử. |

**Query Parameters:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `page` | int | ❌ | `1` | Số trang. |
| `pageSize` | int | ❌ | `20` | Số item / trang (max `100`). |

**Response `200`:**

```json
{
  "userId": "<guid>",
  "userDisplayName": "player@example.com",
  "items": [
    {
      "id": "<guid>",
      "type": "TopUp",
      "amount": 100,
      "balanceSnapshot": 600,
      "relatedPaymentRef": "BVC-...",
      "createdAt": "2026-08-01T14:30:00Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalItems": 12,
  "totalPages": 1
}
```

---

### POST `/api/v1/admin/wallet/set-status`

Thay đổi `AccountStatus` của 1 user (Active / Warning / Restricted / Suspended / Banned).
**Ghi `PlayerActionHistory` audit log vĩnh viễn** (BR-RISK-05).

**Body:**

```json
{
  "targetUserId": "<guid>",
  "newStatus": "Suspended",
  "reason": "Risk score vượt 85 do spam lobby liên tục trong 7 ngày",
  "expiresAt": "2026-08-10T14:30:00Z",
  "idempotencyKey": "uuid-v4"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `targetUserId` | Guid | ✅ | UserId của player. |
| `newStatus` | `AccountStatus` | ✅ | Trạng thái mới (xem enum ở GET list). |
| `reason` | string | ✅ | Lý do — 5-512 ký tự, **bắt buộc** cho audit (BR-RISK-05). |
| `expiresAt` | DateTime? | ❌ | Áp dụng khi `newStatus = Suspended`. Null = khóa vĩnh viễn (chỉ Senior Admin mới được). |
| `idempotencyKey` | string | ✅ | 8-128 ký tự. UNIQUE. |

**Response `200`:**

```json
{
  "targetUserId": "<guid>",
  "previousStatus": "Active",
  "newStatus": "Suspended",
  "expiresAt": "2026-08-10T14:30:00Z",
  "changedAt": "2026-08-03T22:45:00Z"
}
```

**Error:**

| Status | Mô tả |
|--------|--------|
| `400` | `reason` rỗng / quá ngắn; `expiresAt` không hợp lệ. |
| `404` | User không tồn tại. |

**Hành vi sau khi đổi status (BR-RISK-04):**

| `AccountStatus` | Tạo lobby | Join lobby | Top-up | Login |
|-----------------|-----------|------------|--------|-------|
| `Active` | ✅ | ✅ | ✅ | ✅ |
| `Warning` | ✅ (cọc ×2) | ✅ | ✅ | ✅ |
| `Restricted` | ❌ | ✅ | ✅ | ✅ |
| `Suspended` | ❌ | ❌ | ❌ | ✅ |
| `Banned` | ❌ | ❌ | ❌ | ❌ |

**Lưu ý:**
- `Suspended` với `expiresAt` → tự mở khóa khi đến hạn (BR-RISK-06, scheduler `suspension_expiry_check`).
- `Banned` không tự hết hạn — chỉ Senior Admin (BR-RISK-07) mới có quyền set.
- Mọi action đều ghi `PlayerActionHistory` với `actionBy`, `reason`, `metadata`, `expiresAt` — audit log vĩnh viễn.

---

## Admin — Refund Request Review

**Controller:** `AdminWalletController.cs` (`/api/v1/admin/wallet/refund-requests`)
**Role:** Admin (theo BR-RISK-07). Mỗi quyết định đều ghi `PlayerActionHistory` vĩnh viễn.

Admin xem và giải quyết yêu cầu hoàn BVC do player gửi. Khi approve:
1. Tạo ledger entry mới loại `AdminCredit` cho user (BR § III.2, append-only).
2. `BvcRefundRequest.Status = Approved`, `approvedAmountBvc = X`, `resultLedgerEntryId = <id>`.
3. Ghi `PlayerActionHistory(actionType=resolve_refund, actionBy=admin, reason=adminNote, metadata={requestedAmount, approvedAmount, ledgerEntryId})`.

Khi reject: chỉ cập nhật status + admin note, không tạo ledger entry.

---

### GET `/api/v1/admin/wallet/refund-requests`

Lấy danh sách yêu cầu hoàn BVC (toàn hệ thống). Hỗ trợ filter theo status, user.

**Query:**

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `status` | string | ❌ | (all) | Một trong: `Pending`, `Approved`, `Rejected`, `Cancelled`. |
| `userId` | Guid | ❌ | - | Filter theo user cụ thể. |
| `page` | int | ❌ | 1 | Số trang (≥ 1). |
| `pageSize` | int | ❌ | 50 | Số item / trang (1-100). |

**Response `200`:** cùng shape `RefundRequestListDto` như player endpoint, nhưng có thêm filter `userId` ở server-side.

---

### GET `/api/v1/admin/wallet/refund-requests/{requestId}`

Lấy chi tiết 1 yêu cầu hoàn. Trả về `RefundRequestResponseDto` với đầy đủ metadata.

**Error:**

| Status | Mô tả |
|--------|--------|
| `404` | Request không tồn tại. |

---

### POST `/api/v1/admin/wallet/refund-requests/{requestId}/resolve`

Giải quyết yêu cầu hoàn. Duyệt (approve) hoặc từ chối (reject).

**Body:**

```json
{
  "approve": true,
  "approvedAmountBvc": 50000,
  "adminNote": "Đã xác nhận top-up nhầm qua lịch sử SePay, hoàn 50k BVC."
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `approve` | bool | ✅ | `true` = duyệt, `false` = từ chối. |
| `approvedAmountBvc` | long | ✅ nếu approve | Số BVC thực sự hoàn (1 → 10.000.000). Có thể nhỏ hơn `requestedAmountBvc`. Bỏ qua khi reject. |
| `adminNote` | string | ✅ | Ghi chú của admin (10-2000 ký tự). Bắt buộc cho cả approve và reject. |

**Validate:**
- `requestId` không tồn tại → 404
- Request không ở `Pending` → 409 (chỉ resolve khi đang chờ)
- `approve=true` mà thiếu `approvedAmountBvc` hoặc `approvedAmountBvc <= 0` → 400
- `adminNote` < 10 ký tự → 400

**Response `200`:**

```json
{
  "id": "<guid>",
  "userId": "<guid>",
  "relatedLedgerEntryId": "<guid>",
  "requestedAmountBvc": 50000,
  "approvedAmountBvc": 50000,
  "playerReason": "...",
  "adminNote": "Đã xác nhận top-up nhầm, hoàn 50k BVC.",
  "status": "Approved",
  "resolvedByAdminId": "<admin-guid>",
  "resolvedAt": "2026-08-08T16:00:00Z",
  "resultLedgerEntryId": "<new-ledger-guid>",
  "createdAt": "2026-08-08T15:00:00Z",
  "updatedAt": "2026-08-08T16:00:00Z"
}
```

**Hành vi phía backend khi approve:**
1. Bắt đầu DB transaction.
2. Lock `BvcRefundRequest` (SELECT FOR UPDATE) tránh race.
3. Insert `BvcLedgerEntry(Type=AdminCredit, Amount=approvedAmountBvc, UserId=player.Id, balanceSnapshot=player.AvailableBalance + approvedAmountBvc)`.
4. Update `Wallet.AvailableBalance += approvedAmountBvc` (atomic).
5. Update `BvcRefundRequest(Status=Approved, approvedAmountBvc, adminNote, resolvedByAdminId=admin, resolvedAt, resultLedgerEntryId=newLedger.Id)`.
6. Insert `PlayerActionHistory(actionType=resolve_refund_approve, ...)`.
7. Commit. Mọi bước trong 1 transaction — fail → rollback.

**Hành vi phía backend khi reject:**
1. Lock `BvcRefundRequest` (SELECT FOR UPDATE).
2. Update `Status=Rejected, adminNote, resolvedByAdminId, resolvedAt`.
3. Insert `PlayerActionHistory(actionType=resolve_refund_reject, ...)`.
4. Commit.

**Idempotency:**
- Mỗi request chỉ resolve được 1 lần (Status phải = Pending). Double-click → 409.
- Không có `Idempotency-Key` (admin endpoint, single-click đủ).

**Error:**

| Status | Mô tả |
|--------|--------|
| `400` | Thiếu field, `approvedAmountBvc <= 0`, hoặc `adminNote` quá ngắn. |
| `401` | Thiếu token. |
| `403` | Không đủ quyền admin. |
| `404` | Request không tồn tại. |
| `409` | Request đã ở terminal state. |
| `500` | Lỗi hệ thống. |
