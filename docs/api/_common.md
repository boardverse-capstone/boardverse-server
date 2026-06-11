# Common — Response, Auth & Roles

## Bắt đầu nhanh (Local)

### 1. Chạy API

```powershell
cd C:\Users\ASUS\source\repos\BoardVerse
dotnet run --project BoardVerse.API\BoardVerse.API.csproj
```

API chạy tại `http://localhost:5022`. Swagger UI: `http://localhost:5022/swagger`

### 2. Seed dữ liệu dev (nếu DB trống)

```powershell
dotnet run --project tools\SeedDevData\SeedDevData.csproj
```

Sau seed có sẵn:
- Manager: `manager@boardverse.dev` / `Manager@123`
- Cafe ID: `bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb`
- 10 board game master data

### 3. Kiểm tra API hoạt động

```powershell
curl.exe http://localhost:5022/api/health/ping
```

Kỳ vọng: `"message": "pong"`

---

## Response envelope

Mọi API trả về cùng format:

```json
{
  "statusCode": 200,
  "message": "OK",
  "data": {},
  "timestamp": "2026-06-08T12:00:00Z",
  "path": "/api/auth/login"
}
```

- `data` có thể `null`
- `timestamp` là UTC
- Lỗi cũng dùng envelope này

### Response lỗi mẫu

```json
{
  "statusCode": 401,
  "message": "Invalid credentials.",
  "data": null,
  "timestamp": "2026-06-08T12:00:00Z",
  "path": "/api/auth/login"
}
```

---

## Base URL

| Môi trường | URL |
|------------|-----|
| Local | `http://localhost:5022` |
| Production | URL deploy (ví dụ Render) |

---

## Authentication — cách dùng token

### Bước 1: Đăng nhập

```http
POST /api/auth/login
Content-Type: application/json

{
  "usernameOrEmail": "manager@boardverse.dev",
  "password": "Manager@123"
}
```

Lấy `data.token` (JWT access token) và `data.refreshToken`.

### Bước 2: Gọi API protected

```http
GET /api/userprofile
Authorization: Bearer <data.token>
Content-Type: application/json
```

> Luôn gửi header `Content-Type: application/json` khi có body JSON.

### Bước 3: Gia hạn token (khi access token hết hạn)

```http
POST /api/auth/refresh-token
Content-Type: application/json

{ "refreshToken": "<data.refreshToken>" }
```

Trả về `token` và `refreshToken` mới.

### Bước 4: Đăng xuất

```http
POST /api/auth/logout
Content-Type: application/json

{ "refreshToken": "<data.refreshToken>" }
```

### PowerShell — lưu token vào biến

```powershell
$login = curl.exe -s -X POST http://localhost:5022/api/auth/login `
  -H "Content-Type: application/json" `
  -d '{\"usernameOrEmail\":\"manager@boardverse.dev\",\"password\":\"Manager@123\"}' `
  | ConvertFrom-Json

$token = $login.data.token

# Gọi API protected
curl.exe http://localhost:5022/api/userprofile `
  -H "Authorization: Bearer $token"
```

> Trên Windows dùng `curl.exe` (không dùng alias `curl` của PowerShell).

---

## Dùng Swagger UI

1. Mở `http://localhost:5022/swagger`
2. Gọi `POST /api/auth/login` → copy `data.token`
3. Bấm **Authorize** (góc trên phải)
4. Nhập: `Bearer <token>` (có chữ `Bearer` + khoảng trắng)
5. Gọi các endpoint protected — Swagger tự gắn header

---

## Roles

| Role | Mô tả |
|------|--------|
| **Public** | Không cần token |
| **Player** | Người chơi (role mặc định khi đăng ký) |
| **Manager** | Chủ quán — quản lý staff, kho game |
| **CafeStaff** | Nhân viên quán |
| **Admin** | Quản trị hệ thống |

### Quyền theo nhóm API

| Nhóm | Role yêu cầu |
|------|----------------|
| [Health](./health.md) | Public |
| [Auth](./auth.md) | Public (trừ change-password) |
| [Protected](./protected.md) | Đã đăng nhập (mọi role) |
| [User Profile](./user-profile.md) | Đã đăng nhập (mọi role) |
| [User Management](./user-management.md) | Admin |
| [Master Games](./master-games.md) | Manager |
| [Manager](./manager.md) | Manager |
| [Cafe](./cafe.md) | Public (GET) / Manager + chủ quán (PUT, staff) |
| [Cafe Inventory](./cafe-inventory.md) | Public/Player (GET browse) / Staff+Manager (GET full) / Manager (mutations) |
| [Staff](./staff.md) | CafeStaff |

> **Manager + chủ quán:** Token role `Manager` **và** `Cafe.ManagerId` phải trùng user đang gọi API. Dùng cafe ID từ seed hoặc cafe do chính manager đó tạo.

---

## Phân trang (Pagination)

Các API danh sách trả về dạng:

```json
{
  "data": {
    "data": [ /* items */ ],
    "meta": {
      "currentPage": 1,
      "pageSize": 10,
      "totalItems": 42,
      "totalPages": 5
    }
  }
}
```

| Param | Mặc định | Ghi chú |
|-------|----------|---------|
| `pageNumber` | 1 | Cafe, Inventory, Master Games |
| `page` | 1 | User Management (Admin) |
| `pageSize` | 10 | Max thường là 100 |

---

## HTTP status thường gặp

| Code | Ý nghĩa | Cách xử lý |
|------|---------|------------|
| 200 / 201 | Thành công | Đọc `data` |
| 400 | Validation / dữ liệu không hợp lệ | Kiểm tra body, field bắt buộc |
| 401 | Thiếu hoặc token không hợp lệ | Login lại hoặc refresh token |
| 403 | Không đủ quyền | Kiểm tra role hoặc quyền chủ quán |
| 404 | Không tìm thấy | Kiểm tra ID / email |
| 409 | Trùng dữ liệu (conflict) | Dùng PUT thay POST, hoặc đổi email/username |
| 429 | Quá nhiều request (rate limit login) | Đợi rồi thử lại |
| 500 | Lỗi server | Xem log API, kiểm tra DB |

---

## Email (Mailjet)

Toàn bộ email transactional dùng **Mailjet API** (`MailjetEmailService`):

| API | Email gửi |
|-----|-----------|
| `POST /api/auth/send-email-verification` | Mã xác minh 6 số |
| `POST /api/auth/request-password-reset` | Mã reset mật khẩu |
| Cafe partner workflow | Thông báo trạng thái đơn, credentials Manager |

Config: `Mailjet:ApiKey`, `Mailjet:SecretKey`, `Mailjet:SenderEmail`, `Mailjet:SenderName`. Chi tiết: [auth.md](./auth.md).

---

## Dev seed (test local)

```text
Manager email  : manager@boardverse.dev
Manager password: Manager@123
Manager user ID : aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa
Cafe ID         : bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb
Cafe name       : BoardVerse Demo Cafe
```

Chạy seed: `dotnet run --project tools/SeedDevData/SeedDevData.csproj`
