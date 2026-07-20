# PaymentMasterAccountController

**Base route:** `/api/admin/payment-master-accounts`  
**Controller:** `PaymentMasterAccountController.cs`  
**Role:** Admin

API quản lý **tài khoản master** dùng để nhận và tạm giữ tiền cọc deposit. Tách bạch với `SePayAccount` (controller kia quản lý cả master + cafe + environment switching).

> **Liên quan:** [settlement.md](./settlement.md), [sepay-account.md](./sepay-account.md).

---

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/` | GET | Danh sách master account |
| `/{id}` | GET | Chi tiết master account |
| `/` | POST | Tạo master account mới |
| `/{id}` | PUT | Cập nhật |
| `/{id}` | DELETE | Xóa (soft delete) |

**Header:** `Authorization: Bearer <admin-token>`

---

## POST /api/admin/payment-master-accounts

Tạo master account dùng để nhận và tạm giữ deposit.

**Body:**
```json
{
  "provider": "SePay",
  "accountHolder": "BOARDVERSE MASTER",
  "bankCode": "VCB",
  "maskedAccountNumber": "0901234567",
  "virtualAccountNumber": "0901234567",
  "qrContent": "https://qr.sepay.vn/...",
  "webhookSecret": "sepay_webhook_secret"
}
```

| Field | Ràng buộc |
|-------|-----------|
| `provider` | `SePay` / `VietQR` (mặc định: `SePay`) |
| `accountHolder` | Tên chủ tài khoản |
| `bankCode` | Mã ngân hàng (VCB, ACB, …) |
| `maskedAccountNumber` | Số tài khoản masked (vd: `****5678`) |
| `virtualAccountNumber` | VA cho từng booking |
| `qrContent` | QR content (URL/text) |
| `webhookSecret` | Secret verify HMAC |

**Response 201:** `{ "id": "<master-account-id>" }`

**Response codes:**
- `201` — Tạo thành công
- `400` — Thiếu field bắt buộc
- `401` — Thiếu/sai token
- `403` — Không phải Admin
- `500` — Lỗi hệ thống

---

## GET /api/admin/payment-master-accounts

**Response 200:**
```json
{
  "data": {
    "data": [
      {
        "id": "guid",
        "provider": "SePay",
        "accountHolder": "BOARDVERSE MASTER",
        "bankCode": "VCB",
        "maskedAccountNumber": "****5678",
        "virtualAccountNumber": "VA-001",
        "qrContent": "https://qr.sepay.vn/...",
        "isActive": true,
        "createdAt": "2026-07-01T..."
      }
    ]
  }
}
```

---

## GET /api/admin/payment-master-accounts/{id}

**Response codes:**
- `200` — `PaymentMasterAccountDto`
- `404` — Không tìm thấy

---

## PUT /api/admin/payment-master-accounts/{id}

Partial update: `provider`, `accountHolder`, `bankCode`, `maskedAccountNumber`, `virtualAccountNumber`, `qrContent`, `isActive`.

**Lưu ý:** `webhookSecret` **không** update qua endpoint này (chỉ set khi tạo).

**Response codes:**
- `200` — Cập nhật thành công
- `404` — Không tìm thấy

---

## DELETE /api/admin/payment-master-accounts/{id}

Soft delete — đặt `isActive = false` hoặc xóa hẳn tùy implementation. Kiểm tra `IsActive` ở service layer trước khi xóa cứng.

**Response codes:**
- `200` — Xóa thành công
- `404` — Không tìm thấy

---

## Luồng sử dụng

```powershell
# 1. Login Admin
$login = curl.exe -s -X POST http://localhost:5022/api/auth/login `
  -H "Content-Type: application/json" `
  -d '{"usernameOrEmail":"admin@example.com","password":"..."}' | ConvertFrom-Json
$token = $login.data.token

# 2. Tạo master account
curl.exe -X POST http://localhost:5022/api/admin/payment-master-accounts `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -d '{
    "provider":"SePay",
    "accountHolder":"BOARDVERSE MASTER",
    "bankCode":"VCB",
    "maskedAccountNumber":"****5678",
    "virtualAccountNumber":"VA-001",
    "qrContent":"https://qr.sepay.vn/...",
    "webhookSecret":"sepay_webhook_secret"
  }'

# 3. List
curl.exe http://localhost:5022/api/admin/payment-master-accounts `
  -H "Authorization: Bearer $token"
```