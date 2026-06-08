# ManagerController

**Base route:** `/api/manager`  
**Controller:** `ManagerController.cs`  
**Role:** Manager

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
