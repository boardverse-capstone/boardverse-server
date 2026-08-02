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
| `GET` | `/api/v1/wallet/transactions` | Player | Lịch sử ledger phân trang |

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
