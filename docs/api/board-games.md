# BoardGameController

**Base route:** `/api/v1/board-games`  
**Controller:** `BoardGameController.cs`  
**Role:** Public — không cần đăng nhập

API tra cứu **danh mục board game** dành cho người chơi: tìm kiếm gần đúng, lọc theo thể loại / số người / thời gian, xem chi tiết và danh sách linh kiện trong hộp.

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/categories` | GET | Danh sách thể loại (bộ lọc UI) |
| `/` | GET | Tìm kiếm + lọc + phân trang |
| `/{id}` | GET | Chi tiết game + linh kiện |
| `/{id}/details` | GET | Giống `/{id}` (alias) |
| `/{id}/play-configuration` | GET | Kiểm tra min/max người và chế độ chơi khả dụng |
| `/{id}/play-navigation` | POST | Điều hướng Solo Booking hoặc tạo phòng chờ nhóm |

> **Khác với** [Master Games](./master-games.md): endpoint đó dành cho **Manager** nhập kho quán (`alreadyInInventory`, token bắt buộc). Board Games là catalog công khai cho Player.

---

## Luồng UI gợi ý

```
GET /api/v1/board-games?search=avalon
  → hiển thị danh sách (thumbnail, tên, số người, thể loại)

GET /api/v1/board-games/{id}/details
  → màn chi tiết: mô tả, min/max người, components[]

GET /api/v1/board-games/{id}/play-configuration
  → hiển thị nút "Chơi một mình" / "Chơi nhóm" theo minPlayers

POST /api/v1/board-games/{id}/play-navigation
  → điều hướng SoloBooking hoặc LobbyCreation + giới hạn phòng
```

---

## PowerShell mẫu (không cần token)

```powershell
# Danh sách
curl.exe "http://localhost:5022/api/v1/board-games?pageSize=5"

# Fuzzy search
curl.exe "http://localhost:5022/api/v1/board-games?search=avalon"

# Lọc đa tiêu chí
curl.exe "http://localhost:5022/api/v1/board-games?category_ids=c1111111-1111-1111-1111-111111111111&player_count=6&duration_range=ThirtyToSixty"

# Danh sách thể loại (cho dropdown filter)
curl.exe "http://localhost:5022/api/v1/board-games/categories"

# Search tiếng Việt alias
curl.exe "http://localhost:5022/api/v1/board-games?search=ma%20soi"

# Chi tiết Avalon
curl.exe "http://localhost:5022/api/v1/board-games/66666666-6666-6666-6666-666666666666"

# Format JSON đẹp
curl.exe -s "http://localhost:5022/api/v1/board-games?search=catan" | ConvertFrom-Json | ConvertTo-Json -Depth 6
```

---

## GET /api/v1/board-games

Tìm kiếm và lọc board game (AC 1.1, 1.2).

### Query parameters

| Param | Mô tả | Mặc định |
|-------|--------|----------|
| `search` | Fuzzy search theo tên — bỏ dấu tiếng Việt, không phân biệt hoa/thường | — |
| `category_ids` | Lọc thể loại (multi-select, GUID). Game khớp **ít nhất một** thể loại đã chọn | — |
| `player_count` | Số người chơi — chỉ trả game có `minPlayers ≤ N ≤ maxPlayers` | — |
| `duration_range` | Khung thời gian chơi TB (multi-select): `Under30`, `ThirtyToSixty`, `Over60` | — |
| `pageNumber` | Trang | 1 |
| `pageSize` | Kích thước trang | 10 (max 100) |

### Giá trị `duration_range`

| Enum | Ý nghĩa | Điều kiện `playTime` |
|------|---------|----------------------|
| `Under30` | Dưới 30 phút | `< 30` |
| `ThirtyToSixty` | 30–60 phút | `30 ≤ playTime ≤ 60` |
| `Over60` | Trên 60 phút | `> 60` |

Truyền nhiều giá trị: `duration_range=Under30&duration_range=ThirtyToSixty`

Có thể dùng **tên enum** (`Under30`) hoặc **số** (`1`, `2`, `3`) tương ứng `PlayTimeRange`.

### Thể loại — `GET /api/v1/board-games/categories`

Trả về mảng `CategoryDto` (`id`, `name`, `slug`, `description`, `sortOrder`) — dùng `id` làm `category_ids` khi lọc.

| Thể loại | `category_ids` (seed cố định) |
|----------|----------------|
| Ẩn vai | `c1111111-1111-1111-1111-111111111111` |
| Chiến thuật | `c1111111-1111-1111-1111-111111111112` |
| Giải trí | `c1111111-1111-1111-1111-111111111113` |
| Hợp tác | `c1111111-1111-1111-1111-111111111114` |
| Đối kháng | `c1111111-1111-1111-1111-111111111115` |
| Phiêu lưu | `c1111111-1111-1111-1111-111111111116` |

### Ví dụ request

```http
GET /api/v1/board-games?search=avalon&pageNumber=1&pageSize=10
```

```http
GET /api/v1/board-games?category_ids=c1111111-1111-1111-1111-111111111111&player_count=6&duration_range=ThirtyToSixty
```

### Response 200

```json
{
  "statusCode": 200,
  "message": "Board games retrieved successfully",
  "data": {
    "data": [
      {
        "id": "66666666-6666-6666-6666-666666666666",
        "name": "The Resistance: Avalon",
        "thumbnailUrl": "https://example.com/images/avalon.jpg",
        "description": "Phe Hiệp sĩ phải hoàn thành 3 nhiệm vụ thành công...",
        "minPlayers": 5,
        "maxPlayers": 10,
        "playTime": 30,
        "componentCount": 8,
        "categories": [
          {
            "id": "c1111111-1111-1111-1111-111111111111",
            "name": "Ẩn vai",
            "slug": "an-vai",
            "description": "Trò chơi suy luận vai trò bí mật"
          }
        ]
      }
    ],
    "meta": {
      "currentPage": 1,
      "pageSize": 10,
      "totalItems": 1,
      "totalPages": 1,
      "hasPrevious": false,
      "hasNext": false
    }
  }
}
```

| Field | Ghi chú |
|-------|---------|
| `componentCount` | Số linh kiện — dùng list; chi tiết gọi `/details` |
| `categories` | Thể loại đã gắn; game catalog-only có thể `[]` |

**Lỗi:** `500` lỗi hệ thống.

---

## GET /api/v1/board-games/{id}/details

Lấy toàn bộ thông tin chi tiết và danh sách linh kiện (AC 1.3).

### Path

| Param | Mô tả |
|-------|--------|
| `id` | GUID board game (`GameTemplates.Id`) |

### Ví dụ

```http
GET /api/v1/board-games/66666666-6666-6666-6666-666666666666/details
```

### Response 200

```json
{
  "statusCode": 200,
  "message": "Board game details retrieved successfully",
  "data": {
    "id": "66666666-6666-6666-6666-666666666666",
    "name": "The Resistance: Avalon",
    "thumbnailUrl": "https://example.com/images/avalon.jpg",
    "description": "Phe Hiệp sĩ phải hoàn thành 3 nhiệm vụ thành công...",
    "minPlayers": 5,
    "maxPlayers": 10,
    "playTime": 30,
    "createdAt": "2024-01-01T00:00:00Z",
    "updatedAt": "2024-01-01T00:00:00Z",
    "categories": [
      { "id": "c1111111-1111-1111-1111-111111111111", "name": "Ẩn vai", "slug": "an-vai" }
    ],
    "components": [
      { "id": "a6666666-6666-6666-6666-666666666661", "componentName": "Thẻ nhân vật", "defaultQuantity": 10 },
      { "id": "a6666666-6666-6666-6666-666666666662", "componentName": "Token phiếu bầu (Approve/Reject)", "defaultQuantity": 20 },
      { "id": "a6666666-6666-6666-6666-666666666663", "componentName": "Token thực hiện nhiệm vụ (Success/Fail)", "defaultQuantity": 5 }
    ]
  }
}
```

**Lỗi:** `404` không tìm thấy hoặc game `IsActive = false`, `500` lỗi hệ thống.

---

## GET /api/v1/board-games/{id}/play-configuration

Kiểm tra cấu hình số người chơi từ `GameTemplates` (`MinPlayers`, `MaxPlayers`) và xác định chế độ chơi UI có thể hiển thị.

### Response 200

```json
{
  "statusCode": 200,
  "message": "Game play configuration retrieved successfully",
  "data": {
    "gameTemplateId": "66666666-6666-6666-6666-666666666666",
    "gameName": "The Resistance: Avalon",
    "minPlayers": 5,
    "maxPlayers": 10,
    "supportsSoloPlay": false,
    "availablePlayModes": ["Group"]
  }
}
```

| Field | Ghi chú |
|-------|---------|
| `supportsSoloPlay` | `true` khi `minPlayers == 1` |
| `availablePlayModes` | `["Solo","Group"]` nếu hỗ trợ solo; ngược lại chỉ `["Group"]` |

**Lỗi:** `404` game không tồn tại / inactive, `500` lỗi hệ thống.

---

## POST /api/v1/board-games/{id}/play-navigation

Nhận lựa chọn chế độ chơi của người dùng và trả kết quả điều hướng + giới hạn phòng.

### Request body

```json
{
  "playMode": 0
}
```

| `playMode` | Ý nghĩa |
|------------|---------|
| `0` (`Solo`) | Chơi một mình — chỉ hợp lệ khi `minPlayers == 1` |
| `1` (`Group`) | Chơi nhóm — chuyển sang tạo phòng chờ |

### Response 200 — Solo (game có `minPlayers == 1`)

```json
{
  "statusCode": 200,
  "message": "Game play navigation resolved successfully",
  "data": {
    "gameTemplateId": "<guid>",
    "gameName": "Example Solo Game",
    "playMode": "Solo",
    "minPlayers": 1,
    "maxPlayers": 4,
    "supportsSoloPlay": true,
    "navigationTarget": "SoloBooking",
    "roomConfiguration": {
      "minPlayers": 1,
      "maxPlayers": 1,
      "defaultPlayerCount": 1
    }
  }
}
```

> **Lưu ý seed hiện tại:** catalog mẫu (Catan, Avalon, …) có `minPlayers ≥ 2`. Để test luồng Solo trên Swagger, cần game có `minPlayers = 1` trong DB.

### Response 200 — Nhóm (game nhóm hoặc chọn Group)

```json
{
  "statusCode": 200,
  "message": "Game play navigation resolved successfully",
  "data": {
    "gameTemplateId": "66666666-6666-6666-6666-666666666666",
    "gameName": "The Resistance: Avalon",
    "playMode": "Group",
    "minPlayers": 5,
    "maxPlayers": 10,
    "supportsSoloPlay": false,
    "navigationTarget": "LobbyCreation",
    "roomConfiguration": {
      "minPlayers": 5,
      "maxPlayers": 10,
      "defaultPlayerCount": 5
    }
  }
}
```

| `navigationTarget` | Hành vi client |
|--------------------|----------------|
| `SoloBooking` | Đi thẳng luồng đặt bàn trực tiếp (1 người) |
| `LobbyCreation` | Màn tạo phòng chờ với slider/input trong khoảng `roomConfiguration` |

**Lỗi:** `400` chọn Solo nhưng `minPlayers > 1`, `404` game không tồn tại, `500` lỗi hệ thống.

### PowerShell mẫu

```powershell
# Kiểm tra cấu hình Avalon (chỉ Group)
curl.exe "http://localhost:5022/api/v1/board-games/66666666-6666-6666-6666-666666666666/play-configuration"

# Điều hướng tạo phòng nhóm
curl.exe -X POST "http://localhost:5022/api/v1/board-games/66666666-6666-6666-6666-666666666666/play-navigation" ^
  -H "Content-Type: application/json" ^
  -d "{\"playMode\":1}"
```

---

## Acceptance Criteria — checklist test

| AC | Test | Kỳ vọng |
|----|------|---------|
| 1.1 Fuzzy search | `?search=avalon`, `?search=CATAN` | Trả đúng game, không phân biệt hoa/thường |
| 1.2 Multi-filter | `category_ids` + `player_count` + `duration_range` | Kết quả thỏa tất cả tiêu chí |
| 1.3 Chi tiết | `GET .../6666.../details` | Đủ ảnh, tên, mô tả, min/max người, `components[]` |

---

## Dữ liệu mẫu (sau seed)

### Game có đủ category + components (SQL seed)

| Game | ID | Gợi ý `search` |
|------|-----|----------------|
| Catan | `11111111-1111-1111-1111-111111111111` | `catan` |
| Monopoly | `22222222-2222-2222-2222-222222222222` | `monopoly` |
| Uno | `33333333-3333-3333-3333-333333333333` | `uno` |
| Splendor | `44444444-4444-4444-4444-444444444444` | `splendor` |
| Werewolf Ultimate | `55555555-5555-5555-5555-555555555555` | `werewolf` |
| The Resistance: Avalon | `66666666-6666-6666-6666-666666666666` | `avalon` |
| Codenames | `77777777-7777-7777-7777-777777777777` | `codenames` |
| Pandemic | `88888888-8888-8888-8888-888888888888` | `pandemic` |

Thêm game từ catalog seed (Wingspan, Azul, …) có thể chưa có `categories`.

### Nạp / cập nhật dữ liệu

```powershell
# Schema + seed board game (idempotent)
dotnet run --project tools/ExecSql -- BoardVerse.Data/update-all-entities.sql

# Hoặc seed dev đầy đủ (user, cafe, catalog)
dotnet run --project tools/SeedDevData/SeedDevData.csproj
```

---

## GET /api/v1/board-games/categories

```http
GET /api/v1/board-games/categories
```

**Response 200:**
```json
{
  "data": [
    { "id": "c1111111-1111-1111-1111-111111111111", "name": "Ẩn vai", "slug": "an-vai", "sortOrder": 1 }
  ]
}
```

---

## Ghi chú tìm kiếm

- Fuzzy search qua `NameSearchKey` + `SearchAliasesKey` (bỏ dấu, không phân biệt hoa thường).
- Ví dụ: `search=ma soi` → **Werewolf Ultimate**; `search=avalon` → **The Resistance: Avalon**.
- List trả `componentCount`; chi tiết linh kiện: `GET /{id}` hoặc `GET /{id}/details`.

---

## Tiếp theo

- Manager nhập game vào quán: [Master Games](./master-games.md) → [Cafe Inventory](./cafe-inventory.md)
- Player xem game tại quán: [Cafe Inventory — GET browse](./cafe-inventory.md#quyền-xem-kho-get) (đã kèm mô tả, thể loại, linh kiện)
