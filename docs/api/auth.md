# AuthController

**Base route:** `/api/auth`  
**Controller:** `AuthController.cs`

| Endpoint | Method | Role |
|----------|--------|------|
| `/register` | POST | Public |
| `/login` | POST | Public |
| `/google-login` | POST | Public |
| `/refresh-token` | POST | Public |
| `/logout` | POST | Public |
| `/send-email-verification` | POST | Public |
| `/verify-email` | POST | Public |
| `/request-password-reset` | POST | Public |
| `/reset-password` | POST | Public |
| `/change-password` | POST | Player, Manager, CafeStaff, Admin |
| `/link-google` | POST | Public |

---

## Luồng đăng ký & đăng nhập

```
POST /api/auth/register
  → POST /api/auth/login              (nhận token + refreshToken)
  → Gọi API protected với Authorization: Bearer <token>
  → (tuỳ chọn) POST /api/auth/send-email-verification
  → (tuỳ chọn) POST /api/auth/verify-email
```

### PowerShell — login và lưu token

```powershell
$response = curl.exe -s -X POST http://localhost:5022/api/auth/login `
  -H "Content-Type: application/json" `
  -d '{\"usernameOrEmail\":\"manager@boardverse.dev\",\"password\":\"Manager@123\"}' `
  | ConvertFrom-Json

$token = $response.data.token
$refresh = $response.data.refreshToken
Write-Host "Token: $token"
```

---

## POST /api/auth/register

Đăng ký tài khoản mới (role mặc định: `Player`).

**Body:**
```json
{
  "username": "alice",
  "email": "alice@example.com",
  "phoneNumber": "0123456789",
  "password": "P@ssw0rd!"
}
```

| Field | Ràng buộc |
|-------|-----------|
| `username` | Bắt buộc, 3–100 ký tự |
| `email` | Bắt buộc, email hợp lệ |
| `password` | Bắt buộc, 8–100 ký tự |
| `phoneNumber` | Tuỳ chọn |

**Response 200:** `data` chứa thông báo đăng ký thành công.

**Lỗi:** `400` dữ liệu không hợp lệ, `409` trùng email/username, `500` server.

```powershell
curl.exe -X POST http://localhost:5022/api/auth/register `
  -H "Content-Type: application/json" `
  -d '{\"username\":\"alice\",\"email\":\"alice@example.com\",\"password\":\"P@ssw0rd!\"}'
```

---

## POST /api/auth/login

Đăng nhập, nhận JWT + refresh token.

**Body:**
```json
{
  "usernameOrEmail": "manager@boardverse.dev",
  "password": "Manager@123"
}
```

| Field | Mô tả |
|-------|--------|
| `usernameOrEmail` | Nhập **username** hoặc **email** đều được |

**Response 200:**
```json
{
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIs...",
    "refreshToken": "base64-refresh-token..."
  }
}
```

**Cách dùng token:**
- `token` → header `Authorization: Bearer <token>` cho mọi API protected
- `refreshToken` → lưu an toàn, dùng khi access token hết hạn

**Lỗi:** `401` sai credential, `403` tài khoản bị chặn, `429` quá nhiều lần thử (rate limit).

---

## POST /api/auth/google-login

Đăng nhập / tự tạo tài khoản qua Google ID token (từ Google Sign-In SDK).

**Body:**
```json
{ "idToken": "google-id-token-from-client" }
```

**Response 200:** Cùng format `token` + `refreshToken` như login thường.

**Lỗi:** `401` token Google không hợp lệ, `403` tài khoản bị chặn.

---

## POST /api/auth/refresh-token

Gia hạn access token khi nhận `401` từ API protected.

**Body:**
```json
{ "refreshToken": "..." }
```

**Response 200:**
```json
{
  "data": {
    "token": "new-access-token",
    "refreshToken": "new-refresh-token"
  }
}
```

> Sau refresh, **thay cả hai token** — refresh token cũ bị vô hiệu.

**Lỗi:** `401` token hết hạn/không hợp lệ, `403` tài khoản bị chặn, `404` user không tồn tại.

---

## POST /api/auth/logout

Thu hồi refresh token, kết thúc phiên.

**Body:**
```json
{ "refreshToken": "..." }
```

**Response 200:** `data: null`

---

## Luồng xác minh email

```
POST /api/auth/send-email-verification   { "email": "alice@example.com" }
  → User nhận mã 6 số qua email
  → POST /api/auth/verify-email          { "token": "123456" }
```

> **Email:** **Brevo** REST API (`Brevo:ApiKey`, `Brevo:SenderEmail`, `Brevo:SenderName`).  
> Render env: `Brevo__ApiKey`, `Brevo__SenderEmail`, `Brevo__SenderName`.  
> Sender phải được verify trong Brevo (Senders); tài khoản Brevo phải hoàn tất profile validation.

**Lỗi send:** `400` email không hợp lệ, `403` tài khoản bị chặn, `404` email không tồn tại, `500` gửi mail thất bại.  
**Lỗi verify:** `401` mã sai/hết hạn, `403` tài khoản bị chặn.

---

## Luồng quên mật khẩu

```
POST /api/auth/request-password-reset    { "email": "alice@example.com" }
  → User nhận mã reset
  → POST /api/auth/reset-password
       { "token": "123456", "newPassword": "N3wP@ssw0rd!" }
  → POST /api/auth/login                 (đăng nhập bằng mật khẩu mới)
```

**Lỗi request:** `400` email không hợp lệ, `403` email chưa verify hoặc tài khoản bị chặn, `404` không tìm thấy user, `500` gửi mail thất bại.  
**Lỗi reset:** `400` mật khẩu mới không hợp lệ, `401` mã sai/hết hạn, `403` tài khoản bị chặn.

---

## Luồng đổi mật khẩu

Dùng khi user **đã đăng nhập**, muốn đổi mật khẩu mà không cần email reset.

**Header:** `Authorization: Bearer <token>`

**Body:**
```json
{
  "currentPassword": "OldP@ssw0rd!",
  "newPassword": "N3wP@ssw0rd!"
}
```

**Response 200:** `data: null`, message "Password has been changed"

**Lỗi:** `400` mật khẩu mới trùng cũ hoặc tài khoản Google-only, `401` sai mật khẩu hiện tại hoặc token không hợp lệ, `403` tài khoản bị chặn, `404` user không tồn tại.

---

## POST /api/auth/link-google

Liên kết Google với tài khoản local đã có (không tạo tài khoản mới).

**Body:**
```json
{ "idToken": "google-id-token" }
```

**Lỗi:** `401` token Google không hợp lệ, `403` tài khoản bị chặn, `404` không tìm thấy tài khoản local.

---

## Mẹo tích hợp

| Tình huống | Hành động |
|------------|-----------|
| API trả `401` | Thử `refresh-token` trước, nếu vẫn lỗi → redirect login |
| Đăng xuất app | Gọi `logout` + xóa token local |
| Test nhanh Manager | `manager@boardverse.dev` / `Manager@123` (sau seed) |
| Test trên Windows | Dùng `curl.exe`, escape `\"` trong JSON |
