# SePayAccountController

**Base route:** `/api/sepay-accounts`  
**Controller:** `SePayAccountController.cs`  
**Role:** Admin (mặc định); một số endpoint cho Manager

Quản lý tài khoản SePay (Master Account cho BoardVerse + Cafe Account cho từng quán). Phục vụ cho flow thanh toán SePay (xem [sepay-payment-flow.mdc](../../.cursor/rules/sepay-payment-flow.mdc)).

> **Phân biệt:** `SePayAccount` ≠ `PaymentMasterAccount` (đã DEPRECATED). `SePayAccount` quản lý cả master + cafe + environment switching trong một entity duy nhất.

---

## ⚡ QUAN TRỌNG: Manager KHÔNG cần đăng ký SePay

Theo chốt với khách hàng, **Manager cafe KHÔNG CẦN đăng ký tài khoản SePay merchant**. Manager chỉ cần cung cấp **4 field** (TK ngân hàng thật của cafe):

```json
{
  "bankCode": "MBBank",
  "accountNumber": "1234567890",
  "accountHolder": "NGUYEN VAN A"
}
```

Sau khi Manager tạo xong, **admin BoardVerse** vào SePay dashboard → link TK ngân hàng này vào SePay company master (1 lần, 2 phút). Từ đó SePay webhook (bank_mode=all) tự động phát hiện giao dịch vào TK cafe.

Chi tiết 3 cách tích hợp xem [sepay-payment-flow.mdc §I.4](../../.cursor/rules/sepay-payment-flow.mdc).

---

## Endpoints — Admin

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/` | GET | Danh sách SePay accounts (filter: AccountType, CafeId, IsActive) |
| `/{id}` | GET | Chi tiết account |
| `/master` | GET | Master account (BoardVerse central) |
| `/` | POST | **[ADMIN]** Tạo SePay account đầy đủ (full field, có thể set SePay credentials) |
| `/{id}` | PUT | Cập nhật account |
| `/{id}` | DELETE | Xóa account |
| `/{id}/environment` | PUT | Chuyển đổi môi trường (Test ↔ Production) |

## Endpoints — Manager

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/my-cafe` | GET | SePay account của cafe mình |
| `/my-cafe` | POST | **[MANAGER]** Tạo payment account cho cafe (chỉ cần 4 field) |
| `/my-cafe` | PUT | Cập nhật SePay account của cafe mình |
| `/my-cafe/environment` | PUT | Chuyển môi trường cho cafe mình |
| `/my-cafe/qr-preview` | GET | **[MANAGER — TEST]** Gen QR 10k để verify SePay webhook hoạt động (không tạo payment) |

**Header:** `Authorization: Bearer <admin-or-manager-token>`

---

## Account types

| Type | Ai tạo | Mục đích |
|------|--------|----------|
| `Master` | Admin (1 record duy nhất) | BoardVerse central — nhận deposit payment từ player |
| `Cafe` | Manager (1 record / cafe) | Quán nhận session payment từ khách tại quán |

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

### POST /api/sepay-accounts (Admin only)

**Quyền:** Admin. Dùng để tạo Master Account hoặc override cafe account với SePay credentials đầy đủ.

**Body — `CreateSePayAccountRequestDto`:**
```json
{
  "accountType": "Master",
  "cafeId": null,
  "environment": "Production",
  "merchantId": "SP-XXXX-XXXX",
  "apiKey": "...",
  "secretKey": "...",
  "webhookToken": "...",
  "apiBaseUrl": "https://pgapi.sepay.vn",
  "bankCode": "VCB",
  "accountNumber": "...",
  "accountHolder": "BOARDVERSE JSC",
  "returnUrl": "https://api.boardverse.vn/api/payments/sepay/return"
}
```

| Field | Bắt buộc | Mô tả |
|-------|----------|--------|
| `accountType` | ✅ | `Master` hoặc `Cafe` |
| `cafeId` | Nếu `accountType=Cafe` | Guid của cafe |
| `merchantId` | ❌ | Chỉ cần cho SePay Transfer API (settlement) |
| `apiKey` / `secretKey` / `webhookToken` | ❌ | SePay credentials — chỉ cho settlement/verify webhook |
| `apiBaseUrl` | ❌ | Default: `https://pgapi.sepay.vn` |
| `bankCode` / `accountNumber` / `accountHolder` | ⚠️ | Bắt buộc cho VietQR generation |
| `environment` | ❌ | `Test` hoặc `Production`. Default: `Production` |

**Response codes:**
- `201` — Tạo thành công, trả `SePayAccountDto`
- `400` — Dữ liệu không hợp lệ (vd: thiếu `cafeId` cho `accountType=Cafe`)
- `409` — Master/Cafe account đã tồn tại

### PUT /api/sepay-accounts/{id}

Partial update: tất cả field optional. Chỉ field nào có trong body mới được cập nhật.

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
- `404` — Cafe chưa cấu hình payment account

### POST /api/sepay-accounts/my-cafe **[Manager — recommended]`

**Quyền:** Manager. Đây là endpoint **DUY NHẤT** Manager cần gọi để bắt đầu nhận thanh toán.

**Body — `CreateCafePaymentAccountRequestDto` (chỉ 4 field, không cần SePay credentials):**
```json
{
  "bankCode": "MBBank",
  "accountNumber": "1234567890",
  "accountHolder": "NGUYEN VAN A",
  "environment": "Production"
}
```

| Field | Bắt buộc | Mô tả |
|-------|----------|--------|
| `bankCode` | ✅ | Mã ngân hàng. Một số giá trị hợp lệ: `VCB`, `MBBank`, `VietinBank`, `BIDV`, `ACB`, `Techcombank`, `TPBank`, `Sacombank`, `VPBank`, `OCB` |
| `accountNumber` | ✅ | Số tài khoản ngân hàng thật của cafe |
| `accountHolder` | ✅ | Tên chủ TK (in hoa, không dấu) — hiển thị trên VietQR |
| `environment` | ❌ | `Test` hoặc `Production`. Default `Production` |

**Flow sau khi Manager tạo thành công:**
```
1. BoardVerse lưu bank info của cafe.
2. Admin BoardVerse vào SePay dashboard → link TK ngân hàng trên vào SePay company.
3. Từ giờ mỗi CK vào TK cafe → SePay webhook gửi về BoardVerse.
4. BoardVerse parse content (chứa OrderId) → tự động mark PAID.
5. Player quét VietQR → CK vào TK cafe thật → tự động confirm.
```

**Response codes:**
- `201` — Tạo thành công, trả `SePayAccountDto` (đã mask số TK)
- `400` — Thiếu `bankCode`/`accountNumber`/`accountHolder`
- `404` — Manager không quản lý cafe nào
- `409` — Cafe đã có payment account. Dùng `PUT /my-cafe` để cập nhật

**Response 200 example:**
```json
{
  "id": "...",
  "accountType": "Cafe",
  "cafeId": "...",
  "cafeName": "Catan Board Game Cafe",
  "bankCode": "MBBank",
  "maskedAccountNumber": "****7890",
  "accountHolder": "NGUYEN VAN A",
  "environment": "Production",
  "isActive": true,
  "createdAt": "2026-08-08T..."
}
```

> ⚠️ `merchantId`, `secretKey`, `webhookToken` KHÔNG xuất hiện trong response và KHÔNG cần Manager nhập. Manager chỉ cần bank info.

### GET /api/sepay-accounts/my-cafe/qr-preview **[Manager — test payment account]`

**Quyền:** Manager. Endpoint debug để verify QR + SePay webhook hoạt động **mà KHÔNG cần tạo booking/session thật**.

**Body:** Không cần.

**Response 200 — `CafePaymentQrPreviewDto`:**
```json
{
  "qrUrl": "https://vietqr.app/img?bank=MBBank&acc=1234567890&template=compact&amount=10000&showinfo=true&fullacc=true&des=BV-TEST-A3K9P2M7X&holder=NGUYEN%20VAN%20A",
  "testAmount": 10000,
  "testTransferContent": "BV-TEST-A3K9P2M7X",
  "bankCode": "MBBank",
  "maskedAccountNumber": "******7890",
  "accountHolder": "NGUYEN VAN A",
  "instructions": "1. Mở app ngân hàng và quét QR trên.\n2. Xác nhận số tiền 10.000 VND và nội dung CK đúng như hiển thị.\n3. Sau khi CK thành công, SePay sẽ gửi webhook về BoardVerse trong vòng 1-2 phút.\n4. Nếu SePay KHÔNG detect được (không thấy log webhook), liên hệ admin để kiểm tra TK đã được link vào SePay company chưa."
}
```

**Workflow test (Manager dùng sau khi tạo payment account):**

```
1. Manager vào trang "Cài đặt thanh toán" → nhấn "Test QR"
2. App gọi GET /api/sepay-accounts/my-cafe/qr-preview
3. Hiển thị QR + nội dung CK "BV-TEST-XXXX"
4. Manager mở app ngân hàng → quét QR → CK 10k với nội dung đúng
5. Đợi 1-2 phút → SePay webhook gửi về BoardVerse
6. Manager kiểm tra log webhook trên admin dashboard (hoặc hỏi admin)
7. Nếu KHÔNG có webhook → admin BoardVerse chưa link TK vào SePay company
   → Manager liên hệ admin, admin fix trong 5 phút
```

**Lưu ý quan trọng:**
- QR preview **chỉ để test** VietQR render + SePay webhook. KHÔNG tạo payment record trong DB.
- Số tiền cố định 10.000 VND để Manager không quên test với số lớn.
- Transfer content unique mỗi lần gọi (`BV-TEST-XXXX`) để dễ filter giao dịch test trên SePay dashboard.
- Sau khi test thành công, Manager XÓA giao dịch test trên app ngân hàng để tránh nhầm với giao dịch thật.

**Response codes:**
- `200` — Trả QR URL + transfer content + hướng dẫn
- `404` — Manager không quản lý cafe, hoặc cafe chưa có payment account
- `409` — Bank info trong DB thiếu (lỗi data integrity — báo admin)

### PUT /api/sepay-accounts/my-cafe

Cập nhật SePay account của cafe mình (cập nhật bank info nếu cafe đổi TK).

### PUT /api/sepay-accounts/my-cafe/environment

Chuyển đổi môi trường (Test ↔ Production) cho cafe mình.

---

## Security

- `secretKey` / `apiKey` / `webhookToken` **không bao giờ** xuất hiện trong response — chỉ lưu DB và dùng nội bộ (PaymentService, SePayClient).
- `accountNumber` trả về qua response bị **mask** (chỉ hiện 4 số cuối).
- Webhook verification dùng `secretKey` để verify HMAC-SHA256 signature.
- Mỗi cafe chỉ có **1** SePay account active.

---

## Liên quan

- **[sepay-payment-flow.mdc](../../.cursor/rules/sepay-payment-flow.mdc)** — Business rules
- [sepay-webhook.md](./sepay-webhook.md) — Webhook receiver
- [debug-sepay.md](./debug-sepay.md) — Debug endpoints (dev only)
- [payment.md](./payment.md) — Tạo QR session payment