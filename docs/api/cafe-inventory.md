# CafeInventoryController

**Base route:** `/api/cafes/{cafeId}/inventory`  
**Controller:** `CafeInventoryController.cs`

| Endpoint | Method | Ai được dùng |
|----------|--------|--------------|
| `/` | GET | **Public** / Player (browse), CafeStaff + Manager (full) |
| `/deleted` | GET | Manager (chủ quán) |
| `/{inventoryId}` | GET | **Public** / Player (browse), CafeStaff + Manager (full) |
| `/` | POST | Manager (chủ quán) |
| `/{inventoryId}` | PUT | Manager (chủ quán) |
| `/{inventoryId}/restore` | POST | Manager (chủ quán) |
| `/{inventoryId}/sync-penalties` | POST | Manager (chủ quán) |
| `/{inventoryId}` | DELETE | Manager (chủ quán) |

---

## Quyền xem kho (GET)

| Viewer | Response |
|--------|----------|
| Không login / Player | `CafeInventoryBrowseDto` — game info, **không** có phí phạt |
| CafeStaff (đã gắn quán) | Full — kèm `componentPenalties` |
| Manager (chủ quán) | Full — kèm `componentPenalties` |

Staff POS: login CafeStaff → gọi GET với token → thấy phí phạt linh kiện.

Player: không cần token → browse game tại quán.

**Browse response** gồm `description`, `categories[]`, `components[]` (không có phí phạt).

**Search kho quán:** `searchTerm` hỗ trợ fuzzy search (bỏ dấu + alias) giống `/api/v1/board-games`.

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
| `gameTemplateId` | `id` từ `GET /api/v1/master-games` (Manager) hoặc `GET /api/v1/board-games` (catalog) |
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

**Query:**

| Param | Mô tả | Mặc định |
|-------|--------|----------|
| `searchTerm` | Lọc theo tên game (contains) | — |
| `status` | Lọc theo trạng thái (`Available`, `InUse`, …) | — |
| `sortBy` | `UpdatedAt`, `Name`, `BoxQuantity`, `Status` | `UpdatedAt` |
| `sortDescending` | `true` = giảm dần | `true` |
| `pageNumber` | Trang | 1 |
| `pageSize` | Kích thước trang | 10 |

Public browse — **không cần token**. Manager login → response full kèm `componentPenalties`, `description`, `minPlayers`, …

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
        "description": "Trò chơi kinh doanh bất động sản...",
        "minPlayers": 2,
        "maxPlayers": 8,
        "playTime": 120,
        "boxQuantity": 2,
        "status": "Available",
        "categories": [{ "id": "...", "name": "Giải trí", "slug": "giai-tri" }],
        "components": [{ "id": "...", "componentName": "Gameboard", "defaultQuantity": 1 }]
      }
    ],
    "meta": { "currentPage": 1, "pageSize": 10, "totalItems": 1, "totalPages": 1 }
  }
}
```

---

## GET /api/cafes/{cafeId}/inventory/{inventoryId}

Chi tiết **một** mục kho. Quyền xem giống GET list:

| Viewer | Response type |
|--------|----------------|
| Public / Player | `CafeInventoryBrowseDto` |
| CafeStaff / Manager (chủ quán) | `CafeInventoryResponseDto` (kèm `description`, `componentPenalties`) |

**Lỗi:** `404` mục kho hoặc cafe không tồn tại.

---

## PUT /api/cafes/{cafeId}/inventory/{inventoryId}

**Role:** Manager (chủ quán). Cập nhật một phần:

```json
{
  "boxQuantity": 3,
  "status": "Maintenance",
  "componentPenalties": [
    { "gameComponentTemplateId": "a2222222-2222-2222-2222-222222222221", "penaltyFee": 750000 }
  ]
}
```

Tất cả field đều optional — chỉ gửi field cần đổi.

---

## DELETE /api/cafes/{cafeId}/inventory/{inventoryId}

**Role:** Manager (chủ quán). Soft delete (`isActive = false`). Khôi phục qua `POST .../restore`.

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

# 6. Manager — game chưa có trong kho
Invoke-RestMethod -Uri "http://localhost:5022/api/v1/master-games?cafeId=$cafeId&excludeInInventory=true" -Headers $h

# 7. Manager — khôi phục mục đã xóa
Invoke-RestMethod -Uri "http://localhost:5022/api/cafes/$cafeId/inventory/{inventoryId}/restore" `
  -Method POST -Headers $h
```

---

## GET /api/cafes/{cafeId}/inventory/deleted

**Role:** Manager (chủ quán). Query giống GET inventory (`searchTerm`, `status`, `sortBy`, …).

Trả về danh sách mục kho đã soft-delete (`isActive: false`).

---

## POST /api/cafes/{cafeId}/inventory/{inventoryId}/restore

Khôi phục mục đã xóa mềm. Trả `409` nếu game đã có bản active khác.

---

## POST /api/cafes/{cafeId}/inventory/{inventoryId}/sync-penalties

Thêm penalty rows cho component mới từ master game (phí mặc định `0`). Không xóa penalty cũ.
