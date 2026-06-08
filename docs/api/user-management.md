# UserManagementController

**Base route:** `/api/usermanagement`  
**Controller:** `UserManagementController.cs`  
**Role:** Admin

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/users` | GET | Danh sách user (filter + phân trang) |
| `/{id}` | GET | Chi tiết user |
| `/` | POST | Tạo user |
| `/{id}` | PUT | Cập nhật user |
| `/{id}` | DELETE | Vô hiệu hóa user |
| `/users/{id}/block` | POST | Chặn user |
| `/users/{id}/unblock` | POST | Gỡ chặn |
| `/users/{id}/role` | PUT | Đổi role |

**Header:** `Authorization: Bearer <admin-token>`

---

## Cách dùng — luồng Admin

```
1. POST /api/auth/login              (tài khoản role Admin)
2. GET  /api/usermanagement/users    (xem danh sách)
3. GET  /api/usermanagement/{id}     (chi tiết 1 user)
4. PUT  /api/usermanagement/users/{id}/role   (nâng role Manager, ...)
```

> Cần tài khoản **Admin** trong DB. Dev seed mặc định chỉ có Manager — tạo Admin qua DB hoặc seed bổ sung.

### PowerShell mẫu

```powershell
$login = curl.exe -s -X POST http://localhost:5022/api/auth/login `
  -H "Content-Type: application/json" `
  -d '{\"usernameOrEmail\":\"admin@example.com\",\"password\":\"...\"}' `
  | ConvertFrom-Json
$token = $login.data.token

curl.exe "http://localhost:5022/api/usermanagement/users?page=1&pageSize=10" `
  -H "Authorization: Bearer $token"
```

---

## GET /api/usermanagement/users

**Query:**

| Param | Mô tả | Ví dụ |
|-------|--------|-------|
| `search` | Tìm username/email | `alice` |
| `role` | Lọc theo role | `User`, `Manager`, `CafeStaff`, `Admin` |
| `isActive` | Trạng thái active | `true` / `false` |
| `isBlocked` | Trạng thái block | `true` / `false` |
| `page` | Trang (mặc định 1) | `1` |
| `pageSize` | Kích thước (1–100) | `10` |

**Ví dụ:**
```http
GET /api/usermanagement/users?search=alice&role=User&page=1&pageSize=10
Authorization: Bearer <admin-token>
```

**Response 200:**
```json
{
  "data": {
    "data": [
      {
        "id": "guid",
        "username": "alice",
        "email": "alice@example.com",
        "role": "User",
        "isActive": true,
        "isBlocked": false
      }
    ],
    "meta": {
      "currentPage": 1,
      "pageSize": 10,
      "totalItems": 1,
      "totalPages": 1
    }
  }
}
```

**Lỗi:** `400` role filter không hợp lệ, `401`, `403` (không phải Admin).

---

## GET /api/usermanagement/{id}

Lấy chi tiết một user theo GUID.

**Lỗi:** `401`, `403`, `404` không tìm thấy.

---

## POST /api/usermanagement

Tạo user mới (Admin tạo thay vì user tự register).

**Body:**
```json
{
  "username": "bob",
  "email": "bob@example.com",
  "password": "P@ssw0rd!",
  "role": "Manager"
}
```

| Field | Ràng buộc |
|-------|-----------|
| `username` | Bắt buộc, max 100 |
| `email` | Bắt buộc, email hợp lệ |
| `password` | Tuỳ chọn, min 8 ký tự |
| `role` | Mặc định `User` |

**Response 201:** User đã tạo.

**Lỗi:** `400` dữ liệu/role không hợp lệ, `409` trùng email/username.

---

## PUT /api/usermanagement/{id}

Cập nhật thông tin user — chỉ gửi field cần đổi.

**Lỗi:** `400` role không hợp lệ, `404` user không tồn tại, `409` username/email đã được dùng bởi tài khoản khác.

**Body (optional fields):**
```json
{
  "username": "bob2",
  "email": "bob2@example.com",
  "role": "Manager",
  "isActive": true,
  "password": "NewP@ssw0rd!"
}
```

---

## DELETE /api/usermanagement/{id}

Soft-disable user (không xóa cứng).

**Response 200:** `data: null`

---

## POST /api/usermanagement/users/{id}/block

**Body:**
```json
{ "reason": "Spam / vi phạm điều khoản" }
```

**Lỗi:** `400` lý do trống, `404` user không tồn tại.

---

## POST /api/usermanagement/users/{id}/unblock

Gỡ chặn user — không cần body.

---

## PUT /api/usermanagement/users/{id}/role

**Body:**
```json
{ "role": "Manager" }
```

Roles hợp lệ: `User`, `Admin`, `Manager`, `CafeStaff`.

**Dùng khi:** Nâng user thường lên Manager, hoặc gán CafeStaff.
