# ProtectedController

**Base route:** `/api/protected`  
**Controller:** `ProtectedController.cs`  
**Role:** Player, Manager, CafeStaff, Admin — yêu cầu đăng nhập

Endpoint demo để **kiểm tra token** trước khi gọi API phức tạp hơn.

| Endpoint | Method |
|----------|--------|
| `/secret` | GET |

---

## Cách dùng — smoke test token

Sau khi login, gọi endpoint này để xác nhận token hợp lệ:

```powershell
# 1. Login
$login = curl.exe -s -X POST http://localhost:5022/api/auth/login `
  -H "Content-Type: application/json" `
  -d '{\"usernameOrEmail\":\"manager@boardverse.dev\",\"password\":\"Manager@123\"}' `
  | ConvertFrom-Json

# 2. Test token
curl.exe http://localhost:5022/api/protected/secret `
  -H "Authorization: Bearer $($login.data.token)"
```

Nếu trả `200` → token OK, có thể gọi các API khác.  
Nếu trả `401` → token sai/hết hạn → login hoặc refresh.

---

## GET /api/protected/secret

**Header:** `Authorization: Bearer <token>`

**Response 200:**
```json
{
  "data": {
    "userId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "email": "manager@boardverse.dev"
  }
}
```

| Field | Mô tả |
|-------|--------|
| `userId` | GUID user từ JWT claims |
| `email` | Email trong token |

**Lỗi:** `401` thiếu/sai token, hết hạn, token bị thu hồi, tài khoản bị chặn/vô hiệu hóa, hoặc format header sai (thiếu `Bearer`).
