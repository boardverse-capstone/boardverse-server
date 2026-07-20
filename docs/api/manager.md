# ManagerController + ManagerCafeProfileController

**Base routes:**
- `/api/manager` — `ManagerController.cs`
- `/api/manager/cafes/me` — `ManagerCafeProfileController.cs`

**Role:** Manager

## ManagerController

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/my-cafes` | GET | Quán manager đang sở hữu |

**Header:** `Authorization: Bearer <manager-token>`

---

## GET /api/manager/my-cafes

Trả danh sách cafe mà manager hiện tại là chủ (`Cafe.ManagerId`).

**Response 200:**
```json
{
  "data": [
    {
      "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "name": "BoardVerse Demo Cafe",
      "address": "123 Board Game Street, Ho Chi Minh City",
      "phoneNumber": "0901234567",
      "description": "...",
      "createdAt": "2026-06-08T12:00:00Z"
    }
  ]
}
```

Dùng `id` từ response cho các API `/api/cafes/{cafeId}/...`.

```powershell
$login = Invoke-RestMethod -Uri "http://localhost:5022/api/auth/login" `
  -Method POST -ContentType "application/json" `
  -Body (@{ usernameOrEmail = "manager@boardverse.dev"; password = "Manager@123" } | ConvertTo-Json)

Invoke-RestMethod -Uri "http://localhost:5022/api/manager/my-cafes" `
  -Headers @{ Authorization = "Bearer $($login.data.token)" }
```

---

## ManagerCafeProfileController

Quản lý **hồ sơ vận hành** của cafe mà manager sở hữu (Phase 2 sau khi đã được admin duyệt Phase 1). Tài liệu chi tiết: [cafe-partner.md](./cafe-partner.md).

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/api/manager/cafes/me` | GET | Hồ sơ quán đối tác |
| `/api/manager/cafes/me/operational-profile` | PUT | Cập nhật giờ mở cửa + hạ tầng + catalog |
| `/api/manager/cafes/me/activate` | POST | Kích hoạt quán (DATA_BLANK → ACTIVE) |
| `/api/manager/cafes/me/deactivate` | POST | Tạm dừng (ACTIVE → DATA_BLANK) |
| `/api/manager/cafes/me/close` | POST | Ngừng kinh doanh vĩnh viễn (→ INACTIVE) |
| `/api/manager/cafes/me/reopen` | POST | Mở lại sau khi close |

### Trạng thái vận hành

| Trạng thái | Ý nghĩa |
|------------|---------|
| `DATA_BLANK` | Đã cấp tài khoản, chưa kích hoạt |
| `ACTIVE` | Hiển thị trên Mobile App |
| `INACTIVE` | Ngừng kinh doanh (không thể reopen) |
| `BANNED` | Admin cấm |

> Admin có thêm quyền **`BANNED`** qua [admin-cafe.md](./admin-cafe.md).

### Ví dụ

```powershell
# Lấy hồ sơ
curl.exe http://localhost:5022/api/manager/cafes/me \
  -H "Authorization: Bearer $token"

# Cập nhật operational profile (giờ mở cửa + billing)
curl.exe -X PUT http://localhost:5022/api/manager/cafes/me/operational-profile \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{
    "workingHours":{"weekdayStart":"09:00","weekdayEnd":"22:00","weekendStart":"10:00","weekendEnd":"23:00"},
    "numberOfTables":8, "numberOfPrivateRooms":2,
    "spaceImageUrls":["https://..."],
    "numberOfGamesOwned":45,
    "billingModel":"TIME_BASED", "basePrice":50000,
    "tieredBlockRate":3000, "tieredBlockMinutes":15
  }'

# Activate
curl.exe -X POST http://localhost:5022/api/manager/cafes/me/activate \
  -H "Authorization: Bearer $token"

# Deactivate
curl.exe -X POST http://localhost:5022/api/manager/cafes/me/deactivate \
  -H "Authorization: Bearer $token"
```

> Đầy đủ chi tiết (validation, ràng buộc khi activate, billing model): xem [cafe-partner.md](./cafe-partner.md).