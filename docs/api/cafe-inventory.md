# CafeInventoryController

**Base route:** `/api/cafes/{cafeId}/inventory`  
**Controller:** `CafeInventoryController.cs`

| Endpoint | Method | Ai được dùng |
|----------|--------|--------------|
| `/` | GET | **Public** / User (browse), CafeStaff + Manager (full) |
| `/{inventoryId}` | GET | **Public** / User (browse), CafeStaff + Manager (full) |
| `/` | POST | Manager (chủ quán) |
| `/{inventoryId}` | PUT | Manager (chủ quán) |
| `/{inventoryId}` | DELETE | Manager (chủ quán) |

---

## Quyền xem kho (GET)

| Viewer | Response |
|--------|----------|
| Không login / User | `CafeInventoryBrowseDto` — game info, **không** có phí phạt |
| CafeStaff (đã gắn quán) | Full — kèm `componentPenalties` |
| Manager (chủ quán) | Full — kèm `componentPenalties` |

Staff POS: login CafeStaff → gọi GET với token → thấy phí phạt linh kiện.

Player: không cần token → browse game tại quán.

---

## POST /api/cafes/{cafeId}/inventory

**Ví dụ Monopoly:**
```json
{
  "gameTemplateId": "22222222-2222-2222-2222-222222222222",
  "boxQuantity": 2,
  "status": "Available",
  "componentPenalties": [
    {
      "gameComponentTemplateId": "a2222222-2222-2222-2222-222222222221",
      "penaltyFee": 500000
    }
  ]
}
```

| Field | Mô tả |
|-------|--------|
| `gameTemplateId` | `id` từ `GET /api/v1/master-games` |
| `boxQuantity` | 1–1000 |
| `status` | `"Available"`, `"InUse"`, `"Damaged"`, `"Maintenance"`, `"Retired"` (string hoặc số) |
| `componentPenalties` | Tuỳ chọn — game không có components vẫn nhập được |

### Status enum

| Giá trị | Ý nghĩa |
|---------|---------|
| `Available` | Sẵn sàng |
| `InUse` | Đang dùng |
| `Damaged` | Hỏng |
| `Maintenance` | Bảo trì |
| `Retired` | Ngừng |

---

## GET /api/cafes/{cafeId}/inventory

**Query:** `pageNumber`, `pageSize` — **không cần token** (public browse).

**Browse response (public):**
```json
{
  "data": {
    "data": [
      {
        "id": "guid",
        "gameTemplateId": "22222222-2222-2222-2222-222222222222",
        "gameName": "Monopoly",
        "thumbnailUrl": "https://...",
        "bggGameId": null,
        "minPlayers": 2,
        "maxPlayers": 8,
        "playTime": 120,
        "boxQuantity": 2,
        "status": "Available"
      }
    ],
    "meta": { "currentPage": 1, "pageSize": 10, "totalItems": 1, "totalPages": 1 }
  }
}
```

---

## Luồng test đầy đủ

```powershell
# 1. Login Manager
$login = Invoke-RestMethod -Uri "http://localhost:5022/api/auth/login" `
  -Method POST -ContentType "application/json" `
  -Body (@{ usernameOrEmail = "manager@boardverse.dev"; password = "Manager@123" } | ConvertTo-Json)
$token = $login.data.token
$h = @{ Authorization = "Bearer $token" }

# 2. Lấy cafeId
$cafes = Invoke-RestMethod -Uri "http://localhost:5022/api/manager/my-cafes" -Headers $h
$cafeId = $cafes.data[0].id

# 3. Tìm game
$games = Invoke-RestMethod -Uri "http://localhost:5022/api/v1/master-games?searchTerm=monopoly" -Headers $h

# 4. Nhập kho
$body = @{
  gameTemplateId = $games.data.data[0].id
  boxQuantity = 2
  status = "Available"
} | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:5022/api/cafes/$cafeId/inventory" `
  -Method POST -Headers $h -ContentType "application/json" -Body $body

# 5. Public browse (không token)
Invoke-RestMethod -Uri "http://localhost:5022/api/cafes/$cafeId/inventory"
```
