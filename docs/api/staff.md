# StaffController

**Base route:** `/api/staff`  
**Controller:** `StaffController.cs`  
**Role:** CafeStaff

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/my-cafes` | GET | Quán đang làm việc |

**Header:** `Authorization: Bearer <staff-token>`

---

## Cách dùng — luồng nhân viên

Nhân viên **không tự đăng ký quán** — Manager thêm qua [Cafe API](./cafe.md):

```
Manager (user mới):     POST /api/cafes/{cafeId}/staff          { email, username, password? }
Manager (user có sẵn):  POST /api/cafes/{cafeId}/staff/promote  { email, username?, password? }
Staff:   POST /api/auth/login             (email + password manager đã set)
  ↓
Staff:   GET  /api/staff/my-cafes         (xem quán được gán)
```

### PowerShell mẫu

```powershell
# Staff login (sau khi manager đã thêm email vào quán)
$login = curl.exe -s -X POST http://localhost:5022/api/auth/login `
  -H "Content-Type: application/json" `
  -d '{\"usernameOrEmail\":\"staff@example.com\",\"password\":\"<password>\"}' `
  | ConvertFrom-Json
$token = $login.data.token

curl.exe http://localhost:5022/api/staff/my-cafes `
  -H "Authorization: Bearer $token"
```

**Lưu ý:**
- Token phải thuộc user role **CafeStaff** — Manager/User gọi endpoint này sẽ lỗi `403`
- Nếu staff chưa được manager thêm → danh sách trả về `[]` (mảng rỗng)

---

## GET /api/staff/my-cafes

Trả danh sách cafe mà nhân viên hiện tại được gán (active).

**Response 200:**
```json
{
  "data": [
    {
      "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "name": "BoardVerse Demo Cafe",
      "address": "123 Board Game Street, Ho Chi Minh City",
      "phoneNumber": "0901234567",
      "description": "Demo cafe for development",
      "createdAt": "2026-06-08T12:00:00Z"
    }
  ]
}
```

| Field | Mô tả |
|-------|--------|
| `id` | Cafe ID — dùng cho các API quán khác (khi có) |
| `name`, `address` | Thông tin hiển thị cho staff |

**Lỗi:**
- `401` — thiếu/sai token
- `403` — user không phải CafeStaff (ví dụ đang login bằng Manager)

---

## Tích hợp frontend (Staff app)

1. Staff login → lưu token
2. Gọi `GET /api/staff/my-cafes` ngay sau login
3. Nếu `data.length === 0` → hiện thông báo "Chưa được gán quán, liên hệ quản lý"
4. Nếu có nhiều quán → cho staff chọn quán làm việc (lưu `cafeId` local)
