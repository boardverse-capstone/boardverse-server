# MasterGameController

**Base route:** `/api/v1/master-games`  
**Controller:** `MasterGameController.cs`  
**Role:** Manager

Tra cứu **danh mục board game hệ thống** (master data) trước khi nhập vào kho quán.

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/` | GET | Tìm kiếm + phân trang |

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

# Tìm theo tên
curl.exe "http://localhost:5022/api/v1/master-games?searchTerm=wingspan" `
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

---

## GET /api/v1/master-games

**Query:**

| Param | Mô tả | Mặc định |
|-------|--------|----------|
| `searchTerm` | Lọc theo tên (contains, không phân biệt hoa thường) | — (trả tất cả) |
| `pageNumber` | Trang | 1 |
| `pageSize` | Kích thước | 10 (max 100) |

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
        "id": "guid",
        "bggGameId": 13,
        "name": "Catan",
        "thumbnailUrl": null,
        "description": "A strategy board game...",
        "minPlayers": 3,
        "maxPlayers": 4,
        "playTime": 60,
        "createdAt": "2026-06-08T12:00:00Z",
        "updatedAt": "2026-06-08T12:00:00Z",
        "components": [
          {
            "id": "component-guid",
            "gameTemplateId": "guid",
            "componentName": "Dice",
            "defaultQuantity": 2,
            "createdAt": "2026-06-08T12:00:00Z"
          }
        ]
      }
    ],
    "meta": {
      "currentPage": 1,
      "pageSize": 10,
      "totalItems": 10,
      "totalPages": 1
    }
  }
}
```

**Lỗi:** `401` thiếu/sai token, `403` không phải Manager hoặc tài khoản bị chặn, `500` lỗi hệ thống.

---

## Master data có sẵn (sau seed)

10 game phổ biến:

| Game | Gợi ý searchTerm |
|------|------------------|
| Catan | `catan` |
| Ticket to Ride | `ticket` |
| Carcassonne | `carcassonne` |
| Pandemic | `pandemic` |
| Wingspan | `wingspan` |
| Azul | `azul` |
| Splendor | `splendor` |
| Terraforming Mars | `terraforming` |
| Gloomhaven | `gloomhaven` |
| Codenames | `codenames` |

Seed: `dotnet run --project tools/SeedDevData/SeedDevData.csproj`

---

## Tiếp theo

Sau khi có `gameTemplateId` → [Cafe Inventory — POST](./cafe-inventory.md#post-apicafescafeidinventory)
