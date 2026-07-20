# SePayAccountController

**Base route:** `/api/sepay-accounts`  
**Controller:** `SePayAccountController.cs`  
**Role:** Admin (mặc định); một số endpoint cho Manager

Quản lý tài khoản SePay (Master Account cho BoardVerse + Cafe Account cho từng quán). Phục vụ cho flow thanh toán SePay (xem [sepay-payment-flow.mdc](../../.cursor/rules/sepay-payment-flow.mdc)).

> **Phân biệt:** SePayAccount ≠ PaymentMasterAccount. `PaymentMasterAccount` chỉ là tài khoản tổng (master), còn `SePayAccount` quản lý cả master + cafe + environment switching.

---

## Endpoints — Admin

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/` | GET | Danh sách SePay accounts (filter: AccountType, CafeId, IsActive) |
| `/{id}` | GET | Chi tiết account |
| `/master` | GET | Master account (BoardVerse central) |
| `/` | POST | Tạo SePay account mới |
| `/{id}` | PUT | Cập nhật account |
| `/{id}` | DELETE | Xóa account |
| `/{id}/environment` | PUT | Chuyển đổi môi trường (Test ↔ Production) |

## Endpoints — Manager

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/my-cafe` | GET | SePay account của cafe mình |
| `/my-cafe` | PUT | Cập nhật SePay account của cafe mình |
| `/my-cafe/environment` | PUT | Chuyển môi trường cho cafe mình |

**Header:** `Authorization: Bearer <admin-or-manager-token>`

---

## Account types

| Type | Ai tạo | Mục đích |
|------|--------|----------|
| `Master` | Admin | BoardVerse central — nhận deposit payment |
| `Cafe` | Manager (tự cấu hình) | Quán nhận session payment |

---

## Admin endpoints

### GET /api/sepay-accounts

**Query:**

| Param | Mô tả |
|-------|--------|
| `accountType` | `Master` / `Cafe` |
| `cafeId` | Lọc theo cafe |
| `isActive` | true / false |

**Response 200:** danh sách `SePayAccountDto`.

### GET /api/sepay-accounts/{id}

**Response codes:**
- `200` — `SePayAccountDto`
- `404` — Không tìm thấy

### GET /api/sepay-accounts/master

Lấy master account (BoardVerse central).

**Response codes:**
- `200` — `SePayAccountDto`
- `404` — Master account chưa được tạo

### POST /api/sepay-accounts

**Body:**
```json
{
  "accountType": "Cafe",
  "cafeId": "guid",
  "environment": "Test",
  "merchantId": "...",
  "secretKey": "...",
  "bankCode": "VCB",
  "accountNumber": "...",
  "returnUrl": "https://app.boardverse.vn/sepay/return",
  "cancelUrl": "https://app.boardverse.vn/sepay/cancel"
}
```

**Response codes:**
- `201` — Tạo thành công
- `400` — Dữ liệu không hợp lệ
- `409` — Master/Cafe account đã tồn tại

### PUT /api/sepay-accounts/{id}

Partial update: `merchantId`, `secretKey`, `bankCode`, `accountNumber`, `returnUrl`, `cancelUrl`, `isActive`.

### DELETE /api/sepay-accounts/{id}

Xóa account. Lưu ý: ảnh hưởng payment flow của các booking/session đang pending.

### PUT /api/sepay-accounts/{id}/environment

Chuyển đổi `Test ↔ Production`.

**Body:** `{ "environment": "Production" }`

**Response codes:**
- `200` — Cập nhật thành công
- `400` — Môi trường không hợp lệ
- `404` — Không tìm thấy

---

## Manager endpoints

### GET /api/sepay-accounts/my-cafe

Lấy SePay account của cafe mà manager hiện đang sở hữu.

**Response codes:**
- `200` — `SePayAccountDto`
- `404` — Cafe chưa cấu hình SePay

### PUT /api/sepay-accounts/my-cafe

Cập nhật SePay account của cafe mình.

### PUT /api/sepay-accounts/my-cafe/environment

Chuyển đổi môi trường (Test ↔ Production) cho cafe mình.

---

## Security

- `secretKey` chỉ hiển thị khi tạo/sửa — response chỉ trả metadata + masked version
- Webhook verification dùng `secretKey` để verify HMAC-SHA256 signature
- Mỗi cafe chỉ có **1** SePay account active

---

## Liên quan

- **Sepay-payment-flow.mdc** — Business rules
- [sepay-webhook.md](./sepay-webhook.md) — Webhook receiver
- [debug-sepay.md](./debug-sepay.md) — Debug endpoints (dev only)