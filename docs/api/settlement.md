# Settlement Controller

**Base route:** `/api/cafes/{cafeId}/settlements`  
**Controller:** `CafeSettlementController.cs`  
**Role:** Manager hoặc CafeStaff

API theo dõi và xử lý giải ngân deposit từ master account về tài khoản ngân hàng của cafe manager.

---

## GET /api/cafes/{cafeId}/settlements/pending

Lấy danh sách bản ghi giải ngân deposit đang chờ xử lý.

**Response 200:** danh sách `CafeSettlement` - `status = Pending`, `depositAmount`, `netTransferAmount`, `createdAt`.

**Lỗi:** `401` thiếu token; `403` không có quyền vận hành quán; `404` không tìm thấy quán; `500` lỗi hệ thống.

---

## Luồng tích hợp

1. POS thanh toán hóa đơn tổng → `POST /api/cafes/{cafeId}/sessions/{sessionId}/pay`
2. Hệ thống tự động tạo `CafeSettlement` nếu phiên có `DepositAppliedAmount > 0`
3. Trạng thái settlement:
   - `Pending`: chờ SePay transfer thành công
   - `Succeeded`: đã chuyển tiền về manager
   - `Failed`: cần retry hoặc xử lý thủ công
   - `Retrying`: đang thử lại lần nữa
4. Nhân viên có thể xem danh sách chờ giải ngân qua endpoint này.

---

## Luồng test

```powershell
# 1. Login staff/manager
# 2. POST .../sessions/{sessionId}/pay với session có deposit
# 3. GET .../settlements/pending
#    → thấy settlement mới với status = Pending
```

---

# Payment Master Account Controller

**Base route:** `/api/admin/payment-master-accounts`  
**Controller:** `PaymentMasterAccountController.cs`  
**Role:** Admin

API quản lý tài khoản master dùng để nhận và tạm giữ tiền cọc.

---

## POST /api/admin/payment-master-accounts

Tạo master account mới.

**Body mẫu:**

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

**Response 201:** `{ "id": "master-account-id" }`

**Lỗi:** `400` thiếu field bắt buộc; `401` thiếu token; `403` không phải Admin; `500` lỗi hệ thống.

---

## Luồng test

```powershell
# 1. Login Admin
# 2. POST /api/admin/payment-master-accounts
# 3. GET /api/admin/payment-master-accounts (nếu có endpoint list)
```
