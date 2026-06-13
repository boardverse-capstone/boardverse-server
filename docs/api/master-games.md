# MasterGameController

**Base route:** `/api/v1/master-games`  
**Controller:** `MasterGameController.cs`  
**Role:** Manager

Tra cứu **danh mục board game hệ thống** (master data) trước khi nhập vào kho quán.

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/` | GET | Tìm kiếm + lọc + phân trang (kèm components, `alreadyInInventory`) |
| `/{id}` | GET | Chi tiết một game master |

> **Người chơi tra cứu catalog công khai** → dùng [Board Games](./board-games.md) (`/api/v1/board-games`), không cần token.

---

## Cách dùng

API này là **bước 1** trong luồng nhập kho:

```
GET /api/v1/master-games?searchTerm=catan
  → copy data.data[0].id                    → gameTemplateId (POST inventory)
  → copy data.data[0].components[].id       → gameComponentTemplateId (phí phạt)
```

**Yêu cầu:** Login bằng tài khoản **Manager** trước.

### PowerShell mẫu

```powershell
$login = curl.exe -s -X POST http://localhost:5022/api/auth/login `
  -H "Content-Type: application/json" `
  -d '{\"usernameOrEmail\":\"manager@boardverse.dev\",\"password\":\"Manager@123\"}' `
  | ConvertFrom-Json
$token = $login.data.token

# Liệt kê tất cả (trang 1)
curl.exe "http://localhost:5022/api/v1/master-games?pageNumber=1&pageSize=20" `
  -H "Authorization: Bearer $token"

# Tìm theo tên (fuzzy — bỏ dấu, không phân biệt hoa/thường)
curl.exe "http://localhost:5022/api/v1/master-games?searchTerm=wingspan" `
  -H "Authorization: Bearer $token"

# Lọc game chưa có trong kho quán
curl.exe "http://localhost:5022/api/v1/master-games?cafeId=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb&excludeInInventory=true" `
  -H "Authorization: Bearer $token"

# Lọc theo thể loại + số người + thời gian
curl.exe "http://localhost:5022/api/v1/master-games?categoryIds=c1111111-1111-1111-1111-111111111111&playerCount=6&playTimeRanges=ThirtyToSixty" `
  -H "Authorization: Bearer $token"
```

### Đọc response để nhập kho

Từ response, lấy các field sau:

| Field trong response | Dùng cho |
|---------------------|----------|
| `data.data[].id` | `gameTemplateId` khi POST inventory |
| `data.data[].name` | Hiển thị UI |
| `data.data[].components[].id` | `gameComponentTemplateId` trong `componentPenalties` |
| `data.data[].components[].componentName` | Nhãn hiển thị phí phạt |
| `data.data[].components[].defaultQuantity` | Số lượng mặc định mỗi hộp |
| `data.data[].categories[]` | Thể loại game |
| `data.data[].alreadyInInventory` | `true`/`false` khi gọi kèm `cafeId`; `null` nếu không truyền `cafeId` |

---

## GET /api/v1/master-games

**Query:**

| Param | Mô tả | Mặc định |
|-------|--------|----------|
| `searchTerm` | Fuzzy search theo tên (bỏ dấu VN, không phân biệt hoa/thường) | — |
| `categoryIds` | Lọc thể loại (multi-select, GUID) | — |
| `playerCount` | Số người chơi phù hợp (`minPlayers ≤ N ≤ maxPlayers`) | — |
| `playTimeRanges` | Khung thời gian (multi-select): `Under30` / `1`, `ThirtyToSixty` / `2`, `Over60` / `3` | — |
| `cafeId` | ID quán — bật `alreadyInInventory` trên từng game | — |
| `excludeInInventory` | `true` + `cafeId` → chỉ game **chưa** có trong kho quán | `false` |
| `pageNumber` | Trang | 1 |
| `pageSize` | Kích thước | 10 (max 100) |

### Thể loại (`categoryIds`)

| Thể loại | GUID |
|----------|------|
| Ẩn vai | `c1111111-1111-1111-1111-111111111111` |
| Chiến thuật | `c1111111-1111-1111-1111-111111111112` |
| Giải trí | `c1111111-1111-1111-1111-111111111113` |
| Hợp tác | `c1111111-1111-1111-1111-111111111114` |
| Đối kháng | `c1111111-1111-1111-1111-111111111115` |
| Phiêu lưu | `c1111111-1111-1111-1111-111111111116` |

**Ví dụ:**
```http
GET /api/v1/master-games?searchTerm=catan&pageNumber=1&pageSize=10
Authorization: Bearer <manager-token>
```

**Response 200:**
```json
{
  "data": {
    "data": [
      {
        "id": "11111111-1111-1111-1111-111111111111",
        "name": "Catan",
        "thumbnailUrl": "https://example.com/images/catan.jpg",
        "description": "Trò chơi chiến thuật...",
        "minPlayers": 3,
        "maxPlayers": 4,
        "playTime": 60,
        "createdAt": "2024-01-01T00:00:00Z",
        "updatedAt": "2024-01-01T00:00:00Z",
        "alreadyInInventory": null,
        "categories": [
          { "id": "c1111111-1111-1111-1111-111111111112", "name": "Chiến thuật", "slug": "chien-thuat" }
        ],
        "components": [
          {
            "id": "a1111111-1111-1111-1111-111111111119",
            "gameTemplateId": "11111111-1111-1111-1111-111111111111",
            "componentName": "Dice (2 pieces)",
            "defaultQuantity": 2,
            "createdAt": "2024-01-01T00:00:00Z"
          }
        ]
      }
    ],
    "meta": {
      "currentPage": 1,
      "pageSize": 10,
      "totalItems": 16,
      "totalPages": 2,
      "hasPrevious": false,
      "hasNext": true
    }
  }
}
```

**Lỗi:** `401` thiếu/sai token, `403` không phải Manager hoặc tài khoản bị chặn, `500` lỗi hệ thống.

---

## GET /api/v1/master-games/{id}

Chi tiết một game master (components, categories). Tùy chọn `cafeId` để set `alreadyInInventory`.

```http
GET /api/v1/master-games/66666666-6666-6666-6666-666666666666?cafeId=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb
Authorization: Bearer <manager-token>
```

**Lỗi:** `404` không tìm thấy game.

Danh mục thể loại cho UI: `GET /api/v1/board-games/categories` (public).

---

## Master data có sẵn (sau seed)

### Game SQL seed (có category + components đầy đủ)

| Game | Gợi ý searchTerm | ID |
|------|------------------|-----|
| Catan | `catan` | `11111111-1111-1111-1111-111111111111` |
| Monopoly | `monopoly` | `22222222-2222-2222-2222-222222222222` |
| The Resistance: Avalon | `avalon` | `66666666-6666-6666-6666-666666666666` |
| Codenames | `codenames` | `77777777-7777-7777-7777-777777777777` |
| Pandemic | `pandemic` | `88888888-8888-8888-8888-888888888888` |

### Game catalog seed (thêm từ SeedDevData)

Ticket to Ride, Carcassonne, Wingspan, Azul, Splendor, Terraforming Mars, Gloomhaven, …

**Nạp dữ liệu:**

```powershell
# SQL idempotent (schema + 8 game + categories)
dotnet run --project tools/ExecSql -- BoardVerse.Data/update-all-entities.sql

# Dev seed đầy đủ
dotnet run --project tools/SeedDevData/SeedDevData.csproj
```

---

## Tiếp theo

Sau khi có `gameTemplateId` → [Cafe Inventory — POST](./cafe-inventory.md#post-apicafescafeidinventory)

Player tra cứu catalog → [Board Games](./board-games.md)
