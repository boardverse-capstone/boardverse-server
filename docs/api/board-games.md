# BoardGameController

**Base route:** `/api/v1/board-games`  
**Controller:** `BoardGameController.cs`  
**Role:** Public — không cần đăng nhập

API tra cứu **danh mục board game** dành cho người chơi: tìm kiếm gần đúng, lọc theo thể loại / số người / thời gian, xem chi tiết và danh sách linh kiện trong hộp.

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/categories` | GET | Danh sách thể loại (bộ lọc UI) |
| `/` | GET | Tìm kiếm + lọc + phân trang |
| `/top5` | GET | Top 5 board game được chơi nhiều nhất trong hệ thống (widget UI mobile) |
| `/{id}` | GET | Chi tiết game + linh kiện |
| `/{id}/active-cafes` | GET | Danh sách quán cafe đang ACTIVE có game này trong kho và có thể chơi được (mirror ngược của `/api/cafes/{cafeId}/active-games`) |
| `/{id}/play-configuration` | GET | Kiểm tra min/max người và chế độ chơi khả dụng |
| `/{id}/play-navigation` | POST | Điều hướng Solo Booking hoặc tạo phòng chờ nhóm |
| `/thumbnail-proxy` | GET | Proxy ảnh thumbnail từ BoardGameGeek CDN (bypass CORS cho Flutter Web) |

> **Khác với** [Master Games](./master-games.md): endpoint đó dành cho **Manager** nhập kho quán (`alreadyInInventory`, token bắt buộc). Board Games là catalog công khai cho Player.

---

## BoardGameDiscoveryController (companion)

**Base route:** `/api/v1/discovery`  
**Controller:** `BoardGameDiscoveryController.cs`  
**Role:** Mixed — `categories` và `survey` là **public**; `saved/*` yêu cầu đăng nhập.

API khảo sát gợi ý board game + quản lý danh sách game đã lưu của player.

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/api/v1/discovery/categories` | GET | Danh sách thể loại (mirror `BoardGameController.categories`) |
| `/api/v1/discovery/survey` | POST | Khảo sát gợi ý board game theo số người / thể loại / thời gian / kinh nghiệm, kèm quán cafe gần nhất + lobby đang mở |
| `/api/v1/discovery/saved` | GET | Danh sách board game đã lưu của player (yêu cầu đăng nhập) |
| `/api/v1/discovery/saved/{gameTemplateId}` | POST | Lưu hoặc bỏ lưu board game (toggle) |
| `/api/v1/discovery/saved/{gameTemplateId}` | DELETE | Xóa board game khỏi danh sách đã lưu (chỉ xóa, không toggle) |

> **Service nền:** `BoardVerse.Services.Services.BoardGameDiscoveryService` đã implement sẵn 4 method (`RunSurveyAsync`, `GetSavedGamesAsync`, `ToggleSaveAsync`, `UnsaveGameAsync`). Controller chỉ là lớp HTTP wiring phía trên service.

---

## Luồng UI gợi ý

```
GET /api/v1/board-games?search=avalon
  → hiển thị danh sách (thumbnail, tên, số người, thể loại)

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

# Top 5 board game được chơi nhiều nhất (widget UI mobile)
curl.exe "http://localhost:5022/api/v1/board-games/top5"

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

> **Mobile behavior — "Get All":** Nếu request **không truyền bất kỳ filter nào** (`search`, `category_ids`, `player_count`, `duration_range`), API tự động **bỏ qua phân trang** và trả về **toàn bộ** board game đang hoạt động. Response trả về `pageNumber=1`, `pageSize=<tổng số>`, `totalPages=1`, `hasPrevious=false`, `hasNext=false`. Ngay khi có ít nhất 1 filter, phân trang hoạt động bình thường với `pageSize=10`.

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
| `componentCount` | Số linh kiện — dùng list; chi tiết gọi `/{id}` |
| `categories` | Thể loại đã gắn; game catalog-only có thể `[]` |

**Lỗi:** `500` lỗi hệ thống.

---

## GET /api/v1/board-games/top5

Lấy **top 5 board game được chơi nhiều nhất** trong hệ thống, dùng cho widget "Top hot" trên UI mobile bên player (home / trang khám phá).

**Cách đếm `playCount`:** Tổng số lần xuất hiện của game trong các phiên chơi đã thực sự bắt đầu (`StartedAt != null`). Kết hợp cả:
- `ActiveSession.GameTemplateId` — game chính của phiên (primary).
- `ActiveSessionGame.GameTemplateId` — game bổ sung trong phiên (vd khách lấy thêm game khi đang chơi).

Chỉ trả về game đang `IsActive = true`. Sắp xếp giảm dần theo `playCount`, tie-break theo `GameTemplateId` để đảm bảo thứ tự ổn định.

> **Lưu ý:** Endpoint **public**, không cần đăng nhập. Phù hợp cho home screen / discovery page của player app.

### Request

```http
GET /api/v1/board-games/top5
```

Không nhận tham số.

### Response 200

```json
{
  "statusCode": 200,
  "message": "Lấy danh sách board game được chơi nhiều nhất thành công.",
  "data": [
    {
      "id": "66666666-6666-6666-6666-666666666666",
      "name": "Catan",
      "thumbnailUrl": "https://example.com/images/catan.jpg",
      "description": "Thương nhân trên đảo Catan...",
      "minPlayers": 3,
      "maxPlayers": 4,
      "playTime": 60,
      "componentCount": 12,
      "playCount": 127,
      "categories": [
        {
          "id": "c1111111-1111-1111-1111-111111111112",
          "name": "Chiến thuật",
          "slug": "chien-thuat",
          "description": "Trò chơi chiến thuật / cân não"
        }
      ]
    }
  ]
}
```

### Trường response

| Field | Mô tả |
|-------|-------|
| `id` | GUID board game |
| `name` | Tên board game |
| `thumbnailUrl` | Ảnh đại diện (có thể `null`) |
| `description` | Mô tả ngắn (có thể `null`) |
| `minPlayers` / `maxPlayers` | Số người chơi tối thiểu / tối đa |
| `playTime` | Thời gian chơi trung bình (phút) |
| `componentCount` | Số linh kiện trong hộp |
| `playCount` | Tổng số lượt chơi trong hệ thống |
| `categories` | Danh sách thể loại (có thể `[]`) |

**Lỗi:** `500` lỗi hệ thống.

### PowerShell

```powershell
curl.exe "http://localhost:5022/api/v1/board-games/top5" | ConvertFrom-Json | ConvertTo-Json -Depth 6
```

---

## GET /api/v1/board-games/{id}

Lấy toàn bộ thông tin chi tiết và danh sách linh kiện (AC 1.3).

Trả về object `BoardGameDetailDto` (12 trường) — xem bảng **Trường response** bên dưới.

**Optional auth:** Endpoint public (không bắt buộc đăng nhập). Nếu request có kèm JWT hợp lệ, response trả thêm field `isSaved` để client biết board game này có nằm trong danh sách yêu thích của player hiện tại hay không (giống `isSaved` trong survey endpoint và `active-cafes` endpoint).

### Path

| Param | Mô tả |
|-------|--------|
| `id` | GUID board game (`GameTemplates.Id`) |

### Ví dụ

```http
GET /api/v1/board-games/66666666-6666-6666-6666-666666666666
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
    ],
    "isSaved": false
  }
}
```

### Trường response — `BoardGameDetailDto`

| Field | Type | Nullable | Mô tả |
|-------|------|----------|-------|
| `id` | Guid | No | GUID board game (= `GameTemplates.Id`). |
| `name` | string | No | Tên board game. Mặc định `string.Empty` nếu DB trả null. |
| `thumbnailUrl` | string | Yes | URL ảnh thumbnail. `null` nếu game chưa có ảnh. |
| `description` | string | Yes | Mô tả ngắn về game. `null` nếu chưa nhập. |
| `minPlayers` | int | No | Số người chơi tối thiểu (theo `GameTemplates.MinPlayers`). |
| `maxPlayers` | int | No | Số người chơi tối đa (theo `GameTemplates.MaxPlayers`). |
| `playTime` | int | No | Thời gian chơi trung bình (phút). |
| `createdAt` | DateTime (ISO 8601) | No | Thời điểm tạo board game (UTC). |
| `updatedAt` | DateTime (ISO 8601) | No | Thời điểm cập nhật gần nhất (UTC). |
| `categories` | `CategoryDto[]` | No | Thể loại đã gắn — map qua `GameCatalogMapper.MapCategories(game)`. Mặc định `[]` nếu game chưa gắn thể loại. |
| `components` | `BoardGameComponentDto[]` | No | Linh kiện trong hộp — map qua `GameCatalogMapper.MapComponents(game.Components)`. Mặc định `[]` nếu chưa nhập. |
| `isSaved` | bool | No | `true` nếu board game này đang nằm trong danh sách yêu thích (favorites) của player hiện tại. `false` nếu player chưa đăng nhập, chưa lưu game này, hoặc token không hợp lệ. Cùng luật resolve với field `isSaved` trong `DiscoveryBoardGameDto` (`POST /api/v1/discovery/survey`) và `ActiveCafesByBoardGameResponseDto` (`GET /api/v1/board-games/{id}/active-cafes`). Dùng để hiển thị icon "đã lưu / chưa lưu" trên UI detail. |

> **Lưu ý JSON contract:** Response C# property `Name` (PascalCase) serialize thành `name` (camelCase) theo cấu hình JSON của API. Tương tự cho `ThumbnailUrl` → `thumbnailUrl`, `IsSaved` → `isSaved`, … Client (Flutter) parse theo camelCase.

### Field `isSaved` — luật resolve

| Tình huống | Giá trị `isSaved` |
|-----------|-------------------|
| Request không có token / token không hợp lệ (anonymous) | `false` (mặc định) |
| Có token hợp lệ + user đã lưu game này vào `PlayerBoardGameSaves` | `true` |
| Có token hợp lệ + user chưa lưu game này | `false` |

Service **chỉ gọi** `IPlayerBoardGameSaveRepository.ExistsAsync` khi `userId` khác `null` — anonymous request **không** tốn query DB.

**Lỗi:** `404` không tìm thấy hoặc game `IsActive = false`, `500` lỗi hệ thống.

---

## GET /api/v1/board-games/{boardgameId}/active-cafes

Lấy danh sách quán cafe đang ACTIVE có board game này trong kho và có thể chơi được. Đây là **chiều ngược** của `GET /api/cafes/{cafeId}/active-games` — player hỏi "chơi game này ở đâu?" sau khi xem chi tiết game.

| Auth | Mô tả |
|---|---|
| `[AllowAnonymous]` | Public — không cần đăng nhập. |

### Điều kiện quán được trả về

Quán cafe phải thỏa mãn **đồng thời**:

| Điều kiện | Mô tả |
|---|---|
| `Cafe.IsActive = true` | Quán chưa bị soft-delete |
| `Cafe.PartnerOperationalStatus = Active` | Quán đang hoạt động vận hành (không `Inactive`/`Banned`/`DataBlank`) |
| `CafeGameInventory.IsActive = true` | Quán chưa xóa mềm khỏi kho game |
| `CafeGameInventory.Status ∈ {Available, InUse}` | Kho game ở trạng thái phục vụ được (không `Damaged`/`Maintenance`/`Retired`) |
| `GameTemplate.IsActive = true` | Master game vẫn active |

> Endpoint này **throw 404** nếu `boardgameId` không tồn tại hoặc master game đã bị deactivate (giúp client phân biệt "không có quán nào" với "game này không tồn tại").

### Path

| Param | Type | Required | Mô tả |
|---|---|---|---|
| `boardgameId` | Guid | Yes | Mã board game (`GameTemplates.Id`) |

### Query parameters

| Param | Type | Required | Mô tả |
|---|---|---|---|
| `latitude` | double | No | Vĩ độ player (WGS84, -90 đến 90). Truyền kèm `longitude` để server tính `DistanceMeters` và sort theo khoảng cách tăng dần. |
| `longitude` | double | No | Kinh độ player (WGS84, -180 đến 180). Phải truyền kèm `latitude`; nếu chỉ truyền 1 trong 2 → bỏ qua, dùng sort theo tên. |
| `name` | string | No | Lọc theo tên quán (case-insensitive, partial match). |
| `pageNumber` | int | No | Số trang (mặc định 1). |
| `pageSize` | int | No | Kích thước trang (mặc định 20). |

### Sort mặc định

- Có `latitude` + `longitude` → sort theo `DistanceMeters` tăng dần (gần nhất trước), tie-break theo tên.
- Không có location → sort theo tên A→Z (giống `GET /api/cafes`).

### Ví dụ request

```http
GET /api/v1/board-games/11111111-1111-1111-1111-111111111111/active-cafes
```

```http
GET /api/v1/board-games/11111111-1111-1111-1111-111111111111/active-cafes?latitude=10.776889&longitude=106.700806&pageSize=5
```

```http
GET /api/v1/board-games/11111111-1111-1111-1111-111111111111/active-cafes?name=catan
```

### Response 200

Trả về `ActiveCafesByBoardGameResponseDto` — wrap `PaginatedResponse<NearbyCafeDto>` kèm field top-level `isSaved` cho biết board game này có nằm trong danh sách yêu thích của player hiện tại hay không (giống field `isSaved` trong `DiscoveryBoardGameDto` của endpoint `POST /api/v1/discovery/survey`).

**Cấu trúc top-level:**

| Field | Type | Mô tả |
|-------|------|-------|
| `isSaved` | bool | `true` nếu board game trong URL đang nằm trong danh sách yêu thích của player hiện tại. `false` nếu player chưa đăng nhập, chưa lưu game này, hoặc token không hợp lệ. Đặt ở top-level (không nằm trong từng `cafes` item) vì là thuộc tính của *board game được truy vấn*, không phải của từng quán. Khi danh sách cafe rỗng, client vẫn cần `isSaved` để hiển thị icon save/unsave ở header trang detail game. |
| `cafes` | `PaginatedResponse<NearbyCafeDto>` | Danh sách quán cafe đang ACTIVE có board game này trong kho. |

**Cấu trúc `cafes.data[]` (mỗi `NearbyCafeDto`):**

| Field | Mô tả |
|-------|-------|
| `id` / `name` / `address` / `phoneNumber` / `description` | Thông tin cơ bản quán |
| `latitude` / `longitude` | Tọa độ quán (nullable) |
| `distanceMeters` | Khoảng cách tới player (mét); 0 nếu không truyền lat/lng |
| `totalSeats` / `billingModel` / `basePrice` / `tieredBlockRate` / `tieredBlockMinutes` | Biểu phí (BR-01/BR-16) |
| `depositPercentage` / `isPricingLocked` | Cấu hình cọc (BR-02/BR-04) |
| `hasSePayConfigured` | Quán đã cấu hình SePay cho session payment |
| `availableGameCount` | Số hộp board game này đang `Available` (có thể chơi ngay) |
| `totalGameBoxCount` | Tổng số hộp vật lý cho game này (`Available` + `InUse`) |
| `availableTableCount` / `totalTableCount` | Bàn còn trống / tổng bàn |
| `meta` | Phân trang (currentPage, pageSize, totalItems, totalPages) |

**Luật resolve `isSaved`:**
- Request **không có token** (anonymous) → `isSaved = false` (player chưa có danh sách yêu thích).
- Request **có token hợp lệ** → service query `PlayerBoardGameSaves` theo `(userId, gameTemplateId)`. Có row → `true`, không có row → `false`.
- Service dùng `GetOptionalViewerContext()` ở controller để unwrap `userId` mà không throw 401 nếu request anonymous.

```json
{
  "statusCode": 200,
  "message": "Lấy danh sách quán cafe có board game đang hoạt động thành công.",
  "data": {
    "isSaved": false,
    "cafes": {
      "data": [
        {
          "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          "name": "BoardVerse Cafe Thủ Đức",
          "address": "12 Võ Văn Ngân",
          "latitude": 10.85,
          "longitude": 106.77,
          "phoneNumber": "0901234567",
          "description": "Quán board game chuyên Catan, Avalon.",
          "createdAt": "2025-09-01T00:00:00Z",
          "distanceMeters": 1250.5,
          "totalSeats": 30,
          "billingModel": "TimeBased",
          "basePrice": 50000,
          "tieredBlockRate": 10000,
          "tieredBlockMinutes": 15,
          "depositPercentage": 0.5,
          "isPricingLocked": false,
          "hasSePayConfigured": true,
          "availableGameCount": 2,
          "totalGameBoxCount": 3,
          "availableTableCount": 4,
          "totalTableCount": 6
        }
      ],
      "meta": {
        "currentPage": 1,
        "pageSize": 20,
        "totalItems": 1,
        "totalPages": 1,
        "hasPrevious": false,
        "hasNext": false
      }
    }
  }
}
```

### Response 404 — Board game không tồn tại / inactive

```json
{
  "statusCode": 404,
  "message": "Không tìm thấy board game '11111111-1111-1111-1111-111111111111' hoặc game đã bị vô hiệu hóa.",
  "data": null
}
```

**Lỗi:** `404` board game không tồn tại / inactive, `500` lỗi hệ thống.

### PowerShell mẫu

```powershell
# Tất cả quán có Catan (không có location → sort theo tên A→Z)
curl.exe "http://localhost:5022/api/v1/board-games/11111111-1111-1111-1111-111111111111/active-cafes"

# Kèm location để sort theo khoảng cách + lọc theo tên
curl.exe "http://localhost:5022/api/v1/board-games/11111111-1111-1111-1111-111111111111/active-cafes?latitude=10.776889&longitude=106.700806&name=catan&pageSize=5"

# Board game không tồn tại → 404
curl.exe -i "http://localhost:5022/api/v1/board-games/00000000-0000-0000-0000-000000000000/active-cafes"
```

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

## GET /api/v1/board-games/thumbnail-proxy

Proxy ảnh thumbnail từ **BoardGameGeek CDN** (và các host được whitelist) về server-side để bypass CORS cho **Flutter Web**. CanvasKit renderer của Flutter Web sẽ taint canvas khi load ảnh từ upstream không trả `Access-Control-Allow-Origin`, làm hỏng `toImage()` / `getImageData()`. Mobile (Android/iOS) **không bắt buộc** dùng — load trực tiếp từ upstream vẫn OK, nhưng gọi qua proxy vẫn hoạt động (tốn thêm 1 round-trip).

| Auth | Mô tả |
|---|---|
| `[AllowAnonymous]` | Public — không cần đăng nhập. |

### Whitelist host

Backend chỉ proxy ảnh từ 4 host cố định (chống SSRF):

| Host |
|---|
| `cf.geekdo-images.com` |
| `cf.geekdo.com` |
| `images.boardgamegeek.com` |
| `boardgamegeek.com` |

URL ngoài whitelist sẽ trả **502** (không 400 — tránh lộ thông tin whitelist cho attacker).

### Query parameters

| Param | Type | Required | Mô tả |
|---|---|---|---|
| `url` | string | Yes | URL ảnh gốc (http/https). Phải được **encode** bằng `Uri.encodeComponent` vì chứa `:` và `/`. |

### Ví dụ request

```http
GET /api/v1/board-games/thumbnail-proxy?url=https%3A%2F%2Fcf.geekdo-images.com%2F...%2Fpic123.png
```

### Response 200 — binary

Trả về ảnh binary với `Content-Type` theo upstream (`image/png`, `image/jpeg`, hoặc `application/octet-stream`). Header bổ sung:

```http
Cache-Control: public, max-age=86400
```

> 24h cache trên client + proxy server. Thumbnail BGG ít khi thay đổi nên cache dài hợp lý.

### Response 400 — URL không hợp lệ

```json
{
  "statusCode": 400,
  "message": "URL ảnh thumbnail không hợp lệ hoặc rỗng.",
  "data": null
}
```

Trigger khi:
- `url` rỗng / toàn khoảng trắng.
- URL không phải http/https (vd: `ftp://...`, `file://...`, relative path).

### Response 502 — Upstream lỗi

```json
{
  "statusCode": 502,
  "message": "Không thể tải ảnh thumbnail từ nguồn ngoài.",
  "data": null
}
```

Trigger khi:
- Host không nằm trong whitelist.
- Upstream trả non-2xx (404, 500, …).
- Upstream trả `Content-Type` không phải `image/*` (cũng chấp nhận `application/octet-stream`).
- Response vượt size cap 5 MB.
- Timeout (> 5s).

Service đã log lý do cụ thể trong log file — kiểm tra khi debug.

### Lưu ý quan trọng cho client

- **Phải URL-encode `url`** trước khi đưa vào query string. Nếu không, dấu `:` và `/` của URL gốc sẽ vỡ.
- **Cache key** nên đặt theo URL gốc (không phải URL proxy) để cache hit đúng trên cả 2 platform.
- **Platform detection**: chỉ dùng proxy trên `kIsWeb`. Android/iOS load trực tiếp upstream tiết kiệm băng thông.
- **Fallback**: khi 502, hiển thị placeholder (icon / shimmer). Không retry liên tục.

### Ví dụ Dart (Flutter)

```dart
import 'package:flutter/foundation.dart';
import 'package:cached_network_image/cached_network_image.dart';

String resolveThumbnailUrl(String rawUrl) {
  if (rawUrl.isEmpty) return '';
  if (!kIsWeb) return rawUrl; // Mobile: load trực tiếp
  final encoded = Uri.encodeComponent(rawUrl);
  return 'https://api.boardverse.dev/api/v1/board-games/thumbnail-proxy?url=$encoded';
}

// Sử dụng
CachedNetworkImage(
  imageUrl: resolveThumbnailUrl(boardGame.thumbnailUrl),
  cacheKey: 'thumb:${boardGame.thumbnailUrl}', // cache theo URL gốc
  placeholder: (_, __) => const ShimmerBox(),
  errorWidget: (_, __, ___) => const Icon(Icons.image_not_supported),
)
```

### PowerShell mẫu

```powershell
# Lấy thumbnail Catan (encode URL trước khi truyền)
$rawUrl = "https://cf.geekdo-images.com/pic123.png"
$encoded = [System.Uri]::EscapeDataString($rawUrl)
curl.exe -o catan.png "http://localhost:5022/api/v1/board-games/thumbnail-proxy?url=$encoded"

# Lấy thumbnail Avalon với hiển thị header response
curl.exe -I "http://localhost:5022/api/v1/board-games/thumbnail-proxy?url=https%3A%2F%2Fcf.geekdo-images.com%2Fpic456.png"

# Test URL rỗng → 400
curl.exe "http://localhost:5022/api/v1/board-games/thumbnail-proxy?url="

# Test URL không whitelist (vd: example.com) → 502
curl.exe -I "http://localhost:5022/api/v1/board-games/thumbnail-proxy?url=https%3A%2F%2Fexample.com%2Fimage.png"
```

---

## Acceptance Criteria — checklist test

| AC | Test | Kỳ vọng |
|----|------|---------|
| 1.1 Fuzzy search | `?search=avalon`, `?search=CATAN` | Trả đúng game, không phân biệt hoa/thường |
| 1.2 Multi-filter | `category_ids` + `player_count` + `duration_range` | Kết quả thỏa tất cả tiêu chí |
| 1.3 Chi tiết | `GET /api/v1/board-games/{id}` | Đủ ảnh, tên, mô tả, min/max người, `components[]`, `isSaved` |
| 1.3a Chi tiết — `isSaved` (anonymous) | `GET /{id}` không có token | 200 + `data.isSaved = false`; service KHÔNG gọi `PlayerBoardGameSaveRepository.ExistsAsync` |
| 1.3b Chi tiết — `isSaved` (logged in, đã lưu) | `GET /{id}` có token + user đã lưu game này | 200 + `data.isSaved = true` |
| 1.3c Chi tiết — `isSaved` (logged in, chưa lưu) | `GET /{id}` có token + user chưa lưu game này | 200 + `data.isSaved = false` |
| 1.4 Thumbnail proxy (URL hợp lệ) | `GET /thumbnail-proxy?url=https://cf.geekdo-images.com/.../pic.png` | 200 + binary `image/png` + header `Cache-Control: public, max-age=86400` |
| 1.5 Thumbnail proxy (host ngoài whitelist) | `?url=https://example.com/image.png` | 502 + `ThumbnailProxyFailed` |
| 1.6 Thumbnail proxy (URL rỗng) | `?url=` | 400 + `ThumbnailUrlInvalid` |
| 1.7 Thumbnail proxy (non-http scheme) | `?url=ftp://cf.geekdo-images.com/x.png` | 400 hoặc 502 tùy đường đi validation |
| 1.8 Thumbnail proxy (oversize) | response upstream > 5 MB | 502 |

---

## Acceptance Criteria — checklist test (`/{boardgameId}/active-cafes`)

| AC | Test | Kỳ vọng |
|----|------|---------|
| AC-1 Board game không tồn tại | `GET /active-cafes` với `boardgameId` Guid.Empty | 404 + `BoardGameNotFoundException` |
| AC-2 Board game đã `IsActive=false` | `GET /active-cafes` với game inactive | 404 + `BoardGameNotFoundException` |
| AC-3 Có kết quả, không location | `GET /active-cafes` không truyền lat/lng | 200 + danh sách quán sort theo tên A→Z |
| AC-4 Có kết quả, có location | `GET /active-cafes?latitude=...&longitude=...` | 200 + `DistanceMeters` được tính + sort theo khoảng cách |
| AC-5 Partial location (chỉ lat) | `?latitude=...` (không có lng) | 200 + sort theo tên (coi như không có location) |
| AC-6 Filter theo tên | `?name=catan` | 200 + chỉ quán có tên chứa "catan" |
| AC-7 Không có quán nào | game tồn tại nhưng 0 quán match filter | 200 + `data: []`, `totalItems: 0` |
| AC-8 Phân trang | `?pageNumber=2&pageSize=5` | 200 + `meta.currentPage=2`, `pageSize=5` |
| AC-9 Board game thiếu 1 trong các filter kho | game có `CafeGameInventory.Status=Damaged` | Quán đó KHÔNG xuất hiện trong response |
| AC-10 Quán bị `IsActive=false` | quán đã soft-delete | Quán đó KHÔNG xuất hiện trong response |
| AC-11 `isSaved` (anonymous) | `GET /active-cafes` không có token | 200 + `data.isSaved = false`; service KHÔNG gọi `PlayerBoardGameSaveRepository.ExistsAsync` |
| AC-12 `isSaved` (logged in, game đã lưu) | `GET /active-cafes` có token + user đã lưu game này | 200 + `data.isSaved = true` |
| AC-13 `isSaved` (logged in, game chưa lưu) | `GET /active-cafes` có token + user chưa lưu game này | 200 + `data.isSaved = false` |
| AC-14 `isSaved` resolve dù cafe rỗng | game tồn tại + 0 quán match + có token + user đã lưu | 200 + `data.cafes.data = []`, `data.isSaved = true` (resolve dù không có cafe) |

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

Thêm game từ catalog (Wingspan, Azul, …) có thể chưa có `categories` — bổ sung qua Admin API hoặc DB.

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
- List trả `componentCount`; chi tiết linh kiện: `GET /{id}`.

---

## Tiếp theo

- Manager nhập game vào quán: [Master Games](./master-games.md) → [Cafe Inventory](./cafe-inventory.md)
- Player xem game tại quán: [Cafe Inventory — GET browse](./cafe-inventory.md#quyền-xem-kho-get) (đã kèm mô tả, thể loại, linh kiện)
- Khảo sát + danh sách đã lưu: xem mục [BoardGameDiscoveryController](#boardgamediscoverycontroller-companion) ở đầu file.

---

## Acceptance Criteria — checklist test (BoardGameDiscoveryController)

| AC | Test | Kỳ vọng |
|----|------|---------|
| D-1 Survey happy | `POST /survey` body `{playerCount: 4}` không location | 200 + games[] có `matchScore` 0–100 |
| D-2 Survey có location | `POST /survey?latitude=...&longitude=...` | 200 + `nearestCafe` không null + `openLobbies` ≤ 5 |
| D-3 Survey invalid playerCount | `playerCount: 0` hoặc `playerCount: 6` | 400 + message "Số người chơi phải từ 1 đến 5." |
| D-4 Survey invalid duration | `preferredDurations: ["invalid"]` | 400 + message liệt kê giá trị hợp lệ |
| D-5 Saved list | `GET /saved` với token | 200 + danh sách game đã lưu có `savedAt` |
| D-6 Saved list no auth | `GET /saved` không token | 401 |
| D-7 Toggle save (lần 1) | `POST /saved/{id}` chưa lưu | 200 + `isSaved: true`, `savedAt` set |
| D-8 Toggle save (lần 2) | `POST /saved/{id}` đã lưu | 200 + `isSaved: false`, `savedAt: null` |
| D-9 Unsave game | `DELETE /saved/{id}` đã lưu | 200 + `isSaved: false` |
| D-10 Unsave chưa lưu | `DELETE /saved/{id}` chưa lưu | 404 + message `GameNotSaved(gameTemplateId)` |
| D-11 Toggle game missing | `POST /saved/{non-existent-guid}` | 404 + `BoardGameNotFoundException` |

---

## Tổng kết file đã thay đổi (build pass 2026-09-10)

| File | Loại thay đổi | Ghi chú |
|---|---|---|
| `BoardVerse.API/Controllers/BoardGameDiscoveryController.cs` | **Mới** | Expose 5 endpoint HTTP cho discovery flow (categories mirror, survey, saved CRUD). |
| `BoardVerse.Services/IServices/IBoardGameDiscoveryService.cs` | Sửa | Thêm method `UnsaveGameAsync`. |
| `BoardVerse.Services/Services/BoardGameDiscoveryService.cs` | Sửa | Implement `UnsaveGameAsync`; thêm helper `MapToNearestCafe`. |
| `BoardVerse.Core/DTOs/Game/TopBoardGameDto.cs` | Mới | DTO cho `BoardGameController.GetTopPlayedBoardGamesAsync`. |
| `BoardVerse.Core/IRepositories/IGameTemplateRepository.cs` | Sửa | Thêm `GetTopPlayedBoardGamesAsync`. |
| `BoardVerse.Core/Messages/ApiSuccessMessages.cs` | Sửa | Thêm nested class `Discovery` (`SurveyCompleted`, `SavedGamesRetrieved`, `GameSaved`, `GameUnsaved`). |
| `BoardVerse.Tests/Services/BoardGameServiceTests.cs` | Mới | 19 unit test cho `BoardGameService` (search/detail/categories/play config/navigation/top5 với cache). |
| `docs/api/board-games.md` | Cập nhật | Tài liệu này — bổ sung phần `BoardGameDiscoveryController` và 5 endpoint mới. |

---

## Tổng kết file đã thay đổi (build pass 2026-09-29) — Thumbnail Proxy

| File | Loại thay đổi | Ghi chú |
|---|---|---|
| `BoardVerse.API/Controllers/BoardGameController.cs` | Sửa | Thêm action `GetThumbnailProxy` (route `/thumbnail-proxy`, public, trả binary image + `Cache-Control: 24h`). |
| `BoardVerse.Services/Services/Images/ThumbnailProxyService.cs` | **Mới** | Fetch ảnh từ BoardGameGeek CDN, validate host whitelist (chống SSRF), timeout 5s, size cap 5 MB. |
| `BoardVerse.Services/Extensions/ImageServiceExtensions.cs` | Sửa | Đăng ký `HttpClient` typed `IThumbnailProxyService` (timeout mặc định 10s, header `User-Agent: Mozilla/5.0`). |
| `BoardVerse.Tests/Services/ThumbnailProxyServiceTests.cs` | **Mới** | 17 unit test: URL validation, host whitelist, content-type, size cap, happy path. |
| `docs/api/board-games.md` | Cập nhật | Tài liệu này — bổ sung section `GET /thumbnail-proxy` (endpoint, params, response, AC). |

**Whitelist host** (chống SSRF — chỉ chấp nhận 4 host):

- `cf.geekdo-images.com`
- `cf.geekdo.com`
- `images.boardgamegeek.com`
- `boardgamegeek.com`

**Tại sao cần proxy này:**

- Flutter Web (CanvasKit renderer) sẽ **taint canvas** khi load ảnh từ upstream không trả `Access-Control-Allow-Origin`. Hậu quả: `toImage()` / `getImageData()` fail, không save screenshot được.
- Mobile (Android/iOS) không cần — load trực tiếp từ `cf.geekdo-images.com` vẫn OK. FE team chỉ cần thêm helper `resolveThumbnailUrl(raw)` đi qua proxy khi `kIsWeb`, ngược lại giữ raw URL.

---

## Service nội bộ — `BoardGameDiscoveryService`

> **Trạng thái (2026-09-10):** Service `IBoardGameDiscoveryService` đã được implement đầy đủ với **4 method** — `RunSurveyAsync`, `GetSavedGamesAsync`, `ToggleSaveAsync`, `UnsaveGameAsync`. Controller `BoardGameDiscoveryController` đã được tạo (2026-09-10) để expose tất cả method qua HTTP tại `/api/v1/discovery/*`.

| Method (service) | Controller route | Auth | Mô tả |
|---|---|---|---|
| `RunSurveyAsync(request, userId, lat, lng, ct)` | `POST /api/v1/discovery/survey` | `[AllowAnonymous]` | Survey đề xuất board game. Body: `BoardGameSurveyRequestDto` (playerCount 1–5, categoryIds, preferredDurations, searchKeyword, experienceLevel). Query: `latitude`, `longitude` (optional). Trả `BoardGameSurveyResponseDto` gồm `games[]` (scored 0–100), `categories`, `nearestCafe`, `openLobbies`. Nếu có token → ưu tiên `userId` để fill `IsSaved`. |
| `GetSavedGamesAsync(userId, ct)` | `GET /api/v1/discovery/saved` | `[Authorize]` | Danh sách game user đã lưu (từ `IPlayerBoardGameSaveRepository`), kèm `hasOpenLobby` flag. |
| `ToggleSaveAsync(userId, gameTemplateId, ct)` | `POST /api/v1/discovery/saved/{gameTemplateId}` | `[Authorize]` | Toggle save/un-save. Throw `BoardGameNotFoundException` nếu game không tồn tại. Trả `BoardGameSaveResultDto` với `IsSaved` mới. |
| `UnsaveGameAsync(userId, gameTemplateId, ct)` | `DELETE /api/v1/discovery/saved/{gameTemplateId}` | `[Authorize]` | Xóa cứng khỏi danh sách đã lưu. Throw `BoardGameNotFoundException` nếu game không tồn tại; throw `NotFoundException` (404) nếu user chưa lưu game này. |

### DTO liên quan

- `BoardGameSurveyRequestDto`: `PlayerCount (1..5)`, `CategoryIds`, `PreferredDurations (string[] ∈ {under30, 30to60, over60})`, `SearchKeyword`, `ExperienceLevel (Beginner|Casual|Regular|Expert)`.
- `BoardGameSurveyResponseDto`: `Games (DiscoveryBoardGameDto[])`, `TotalCount`, `SurveyedPlayerCount`, `AppliedFilters`, `NearestCafe`, `OpenLobbies`.
- `DiscoveryBoardGameDto`: game + `MatchScore` + `IsSaved` + `HasOpenLobby`.
- `SavedBoardGameDto`: `Id` (save id), `GameTemplateId`, `GameName`, `ThumbnailUrl`, `Description`, `MinPlayers`, `MaxPlayers`, `PlayTimeMinutes`, `Categories`, `SavedAt`, `HasOpenLobby`.
- `BoardGameSaveResultDto`: `GameTemplateId`, `IsSaved`, `SavedAt?`.

### Error contract

| Tình huống | Message (`ApiErrorMessages.Discovery.*` / `BoardGame.*`) | HTTP |
|---|---|---|
| `PlayerCount < 1 \|\| > 5` | `PlayerCountInvalid` — "Số người chơi phải từ 1 đến 5." | 400 |
| `PreferredDurations` không nằm trong `{under30, 30to60, over60}` | `BadRequestException` với message custom "Giá trị PreferredDurations không hợp lệ: '{value}'. Chỉ chấp nhận: under30, 30to60, over60." | 400 |
| Game không tồn tại khi toggle save | `BoardGameNotFoundException` + `GameNotSaved(gameTemplateId)` | 404 |
| Game không tồn tại khi xóa save | `BoardGameNotFoundException` + `MasterNotFound(gameTemplateId)` | 404 |
| User chưa lưu game khi gọi DELETE | `NotFoundException` + `GameNotSaved(gameTemplateId)` | 404 |
| `GetUserIdFromClaims()` thiếu claim NameIdentifier | `UnauthorizedException` + `InvalidUserIdClaim` | 401 |

### Lưu ý implementation

- `PaginationParams` chỉ có parameterless constructor + property init (`{ PageNumber, PageSize }`). Service `BoardGameDiscoveryService.RunSurveyAsync` gọi `new PaginationParams { PageNumber = 1, PageSize = 1 }` (sửa lỗi compile `CS1729` ngày 2026-09-08).
- `NearbyCafeSearchResultDto` có property lồng `Cafes.Data` chứ không phải `Data` trực tiếp — dùng `cafeResult.Cafes.Data?.FirstOrDefault()` để lấy 1 cafe gần nhất.
- Endpoint `survey` chấp nhận cả anonymous + authenticated. Khi có token, controller unwrap `userId` qua `GetOptionalViewerContext()` và truyền xuống service để fill `IsSaved` flag cho từng game.
- `HasOpenLobby` chỉ `true` nếu game có lobby với status `Open`, `Viable`, `Full` hoặc `WaitingCheckIn` (theo `LobbyStatus` enum).
- `UnsaveGameAsync` dùng `DELETE` method (HTTP semantic) thay vì POST — khác với `ToggleSaveAsync` dùng POST.

---

## GET /api/v1/discovery/categories

Mirror với `GET /api/v1/board-games/categories`. Trả cùng response (danh sách `CategoryDto`).

| Auth | Mô tả |
|---|---|
| `[AllowAnonymous]` | Public — không cần token. |

### Response 200

```json
{
  "statusCode": 200,
  "message": "Lấy danh sách thể loại thành công.",
  "data": [
    { "id": "c1111111-1111-1111-1111-111111111111", "name": "Ẩn vai", "slug": "an-vai", "sortOrder": 1 }
  ]
}
```

**Lỗi:** `500` lỗi hệ thống.

---

## POST /api/v1/discovery/survey

Khảo sát gợi ý board game theo tiêu chí của player. Trả về danh sách game có `MatchScore` (0–100), kèm quán cafe gần nhất (nếu có location) và lobby đang mở cho top-1 game.

| Auth | Mô tả |
|---|---|
| `[AllowAnonymous]` | Public — không yêu cầu token. Có token sẽ ưu tiên fill `IsSaved`. |

### Request body

```json
{
  "playerCount": 4,
  "categoryIds": ["c1111111-1111-1111-1111-111111111111"],
  "preferredDurations": ["30to60", "over60"],
  "experienceLevel": 2,
  "searchKeyword": "ma soi"
}
```

| Field | Type | Required | Mô tả |
|---|---|---|---|
| `playerCount` | int | Yes | Số người chơi (1–5). |
| `categoryIds` | `Guid[]` | No | Lọc thể loại (multi-select). |
| `preferredDurations` | `string[]` | No | Khoảng thời gian: `under30`, `30to60`, `over60`. |
| `experienceLevel` | int (1–4) | No | `Beginner=1`, `Casual=2`, `Regular=3`, `Expert=4`. |
| `searchKeyword` | string | No | Fuzzy search term. |

### Query parameters

| Field | Type | Required | Mô tả |
|---|---|---|---|
| `latitude` | double | No | WGS84 lat của player (để tìm cafe gần nhất). |
| `longitude` | double | No | WGS84 lng của player. |

### Response 200

```json
{
  "statusCode": 200,
  "message": "Khảo sát hoàn tất, đây là kết quả gợi ý board game.",
  "data": {
    "games": [
      {
        "id": "55555555-5555-5555-5555-555555555555",
        "name": "Werewolf Ultimate",
        "thumbnailUrl": "https://example.com/images/werewolf.jpg",
        "description": "Trò chơi nhập vai đêm...",
        "minPlayers": 5,
        "maxPlayers": 16,
        "playTimeMinutes": 30,
        "categories": ["Ẩn vai"],
        "matchScore": 78.5,
        "isSaved": false,
        "hasOpenLobby": true
      }
    ],
    "totalCount": 8,
    "surveyedPlayerCount": 4,
    "appliedFilters": ["Ẩn vai"],
    "nearestCafe": {
      "cafeId": "...",
      "cafeName": "BoardVerse Cafe Thủ Đức",
      "cafeAddress": "12 Võ Văn Ngân",
      "distanceKm": 1.8,
      "latitude": 10.85,
      "longitude": 106.77,
      "availableGamesCount": 24,
      "hasOpenLobby": false
    },
    "openLobbies": [
      {
        "lobbyId": "...",
        "gameTemplateId": "55555555-5555-5555-5555-555555555555",
        "gameName": "Werewolf Ultimate",
        "currentMembers": 3,
        "maxMembers": 8,
        "playDate": "2026-09-12T00:00:00Z",
        "startTime": "19:30",
        "cafeId": "...",
        "cafeName": "BoardVerse Cafe Thủ Đức",
        "isOpen": true
      }
    ]
  }
}
```

**Lỗi:** `400` (`PlayerCount` ngoài 1–5 hoặc `PreferredDurations` sai), `500` lỗi hệ thống.

### PowerShell mẫu

```powershell
# Survey đơn giản, không có location
curl.exe -X POST "http://localhost:5022/api/v1/discovery/survey" ^
  -H "Content-Type: application/json" ^
  -d "{\"playerCount\":4,\"preferredDurations\":[\"30to60\"]}"

# Survey có location (để lấy nearestCafe + openLobbies)
curl.exe -X POST "http://localhost:5022/api/v1/discovery/survey?latitude=10.85&longitude=106.77" ^
  -H "Content-Type: application/json" ^
  -d "{\"playerCount\":5,\"categoryIds\":[\"c1111111-1111-1111-1111-111111111111\"],\"experienceLevel\":2,\"searchKeyword\":\"ma soi\"}"
```

---

## GET /api/v1/discovery/saved

Lấy danh sách board game đã lưu của player (cần đăng nhập).

| Auth | Mô tả |
|---|---|
| `[Authorize]` | Bắt buộc JWT. Throw `UnauthorizedException` nếu claim `NameIdentifier` thiếu. |

### Response 200

```json
{
  "statusCode": 200,
  "message": "Lấy danh sách board game đã lưu thành công.",
  "data": [
    {
      "id": "<save-id-guid>",
      "gameTemplateId": "66666666-6666-6666-6666-666666666666",
      "gameName": "The Resistance: Avalon",
      "thumbnailUrl": "https://example.com/images/avalon.jpg",
      "description": "Phe Hiệp sĩ phải hoàn thành...",
      "minPlayers": 5,
      "maxPlayers": 10,
      "playTimeMinutes": 30,
      "categories": ["Ẩn vai"],
      "savedAt": "2026-09-08T14:22:00Z",
      "hasOpenLobby": true
    }
  ]
}
```

**Lỗi:** `401` thiếu/hết hạn token, `500` lỗi hệ thống.

---

## POST /api/v1/discovery/saved/{gameTemplateId}

Lưu hoặc bỏ lưu board game (toggle). Nếu chưa lưu → lưu; nếu đã lưu → bỏ lưu.

| Auth | Mô tả |
|---|---|
| `[Authorize]` | Bắt buộc JWT. |

### Path

| Field | Type | Required | Mô tả |
|---|---|---|---|
| `gameTemplateId` | Guid | Yes | ID board game cần toggle save. |

### Response 200 — saved

```json
{
  "statusCode": 200,
  "message": "Đã lưu board game thành công.",
  "data": {
    "gameTemplateId": "66666666-6666-6666-6666-666666666666",
    "isSaved": true,
    "savedAt": "2026-09-10T14:30:00Z"
  }
}
```

### Response 200 — unsaved (toggle trở lại)

```json
{
  "statusCode": 200,
  "message": "Đã bỏ lưu board game.",
  "data": {
    "gameTemplateId": "66666666-6666-6666-6666-666666666666",
    "isSaved": false,
    "savedAt": null
  }
}
```

**Lỗi:** `401` thiếu/hết hạn token, `404` game không tồn tại / inactive, `500` lỗi hệ thống.

### PowerShell mẫu

```powershell
# Toggle save Avalon (lần 1: lưu, lần 2: bỏ lưu)
curl.exe -X POST "http://localhost:5022/api/v1/discovery/saved/66666666-6666-6666-6666-666666666666" ^
  -H "Authorization: Bearer <player-token>"
```

---

## DELETE /api/v1/discovery/saved/{gameTemplateId}

Xóa board game khỏi danh sách đã lưu của player (xóa cứng, không toggle). Throw 404 nếu user chưa lưu game này.

| Auth | Mô tả |
|---|---|
| `[Authorize]` | Bắt buộc JWT. |

### Path

| Field | Type | Required | Mô tả |
|---|---|---|---|
| `gameTemplateId` | Guid | Yes | ID board game đã lưu cần xóa. |

### Response 200

```json
{
  "statusCode": 200,
  "message": "Đã bỏ lưu board game.",
  "data": {
    "gameTemplateId": "66666666-6666-6666-6666-666666666666",
    "isSaved": false,
    "savedAt": null
  }
}
```

**Lỗi:**

- `401` thiếu/hết hạn token.
- `404`:
  - Game không tồn tại / inactive (`BoardGameNotFoundException`).
  - User chưa lưu game này (`NotFoundException` + `GameNotSaved(gameTemplateId)`).
- `500` lỗi hệ thống.

### PowerShell mẫu

```powershell
curl.exe -X DELETE "http://localhost:5022/api/v1/discovery/saved/66666666-6666-6666-6666-666666666666" ^
  -H "Authorization: Bearer <player-token>"
```

---

## Tổng kết file đã thay đổi (build pass 2026-10-03) — `/{boardgameId}/active-cafes`

Thêm endpoint **mirror ngược** của `GET /api/cafes/{cafeId}/active-games`. Player hỏi "chơi game này ở đâu?" thay vì "quán này có những game nào?". Cùng shape response (`PaginatedResponse<NearbyCafeDto>`) để frontend dùng lại UI component hiện có.

| File | Loại thay đổi | Ghi chú |
|---|---|---|
| `BoardVerse.API/Controllers/BoardGameController.cs` | Sửa | Thêm action `GetActiveCafesByBoardGame` (route `/{boardgameId}/active-cafes`, public, `[AllowAnonymous]`). |
| `BoardVerse.Services/IServices/IBoardGameService.cs` | Sửa | Thêm method `GetActiveCafesByBoardGameAsync`. |
| `BoardVerse.Services/Services/BoardGameService.cs` | Sửa | Implement `GetActiveCafesByBoardGameAsync`: validate board game active (404 nếu không), chỉ truyền `lat`/`lng` xuống repo khi cả 2 cùng có. Inject thêm `ICafeRepository`. |
| `BoardVerse.Core/IRepositories/ICafeRepository.cs` | Sửa | Thêm method `GetActiveCafesByBoardGameAsync` mirror ngược của `GetActiveGamesByCafeAsync`. |
| `BoardVerse.Data/Repositories/CafeRepository.cs` | Sửa | Implement query: filter theo `Cafe.IsActive + PartnerOperationalStatus=Active + CafeGameInventory.{IsActive, Status ∈ {Available,InUse}} + GameTemplate.IsActive`; sort theo `DistanceMeters` khi có lat/lng, ngược lại sort theo tên. Đếm `AvailableGameCount`/`TotalGameBoxCount` từ `CafeInventoryBox` (vật lý). |
| `BoardVerse.Core/DTOs/Game/ActiveCafesByBoardGameQueryDto.cs` | **Mới** | Query DTO: `Latitude`, `Longitude`, `Name`, `PageNumber`, `PageSize`. |
| `BoardVerse.Core/DTOs/Game/ActiveCafesByBoardGameResponseDto.cs` | **Mới** | Response DTO: `IsSaved` (top-level) + `Cafes` (wrap `PaginatedResponse<NearbyCafeDto>`). Wrap (không trả thẳng `PaginatedResponse`) để đặt `isSaved` ở top-level — tránh trùng dữ liệu trên từng item cafe, và vẫn resolve được `isSaved` khi danh sách cafe rỗng. |
| `BoardVerse.Core/IRepositories/IPlayerBoardGameSaveRepository.cs` | Sửa | Có sẵn `ExistsAsync(userId, gameTemplateId, ct)` — service dùng để resolve `isSaved`. |
| `BoardVerse.Core/Messages/ApiSuccessMessages.cs` | Sửa | Thêm `BoardGame.ActiveCafesRetrieved`. |
| `BoardVerse.Tests/Services/BoardGameServiceTests.cs` | Sửa | 4 unit test mới cho `isSaved` resolve (anonymous → false, logged-in saved → true, logged-in not-saved → false, empty cafes vẫn resolve isSaved). Cập nhật `BuildService` helper để inject `Mock<ICafeRepository>` + `Mock<IPlayerBoardGameSaveRepository>`. |
| `docs/api/board-games.md` | Cập nhật | Tài liệu này — bổ sung section `GET /api/v1/board-games/{boardgameId}/active-cafes` (điều kiện filter, query params, response shape với `isSaved`, luật resolve `isSaved`, error 404, AC checklist 11–14 cho `isSaved`). |

---

## Tổng kết file đã thay đổi (build pass 2026-10-03) — `/{id}` thêm `isSaved`

Bổ sung field `isSaved` vào `GET /api/v1/board-games/{id}` để client hiển thị icon save/unsave trên UI detail (giống pattern `isSaved` đã có ở `DiscoveryBoardGameDto` và `ActiveCafesByBoardGameResponseDto`). Endpoint vẫn public — `userId` optional; nếu có token hợp lệ thì service tra `PlayerBoardGameSaves` để set `IsSaved`, nếu không thì mặc định `false`.

| File | Loại thay đổi | Ghi chú |
|---|---|---|
| `BoardVerse.Core/DTOs/Game/BoardGameDetailDto.cs` | Sửa | Thêm property `bool IsSaved` (default `false`). |
| `BoardVerse.Services/IServices/IBoardGameService.cs` | Sửa | `GetBoardGameByIdAsync` thêm tham số `Guid? userId = null` (default `null` để không break caller cũ). |
| `BoardVerse.Services/Services/BoardGameService.cs` | Sửa | Implementation: nếu `userId.HasValue` thì gọi `IPlayerBoardGameSaveRepository.ExistsAsync(userId, id, ct)` để set `IsSaved`; ngược lại `IsSaved = false`. Service **không** gọi repo khi `userId` null (tránh query DB thừa cho anonymous). `MapDetail` thêm parameter `bool isSaved = false`. |
| `BoardVerse.API/Controllers/BoardGameController.cs` | Sửa | Action `GetBoardGameById` gọi `GetOptionalViewerContext()` để lấy `userId` từ JWT (nếu có), truyền xuống service. XML doc bổ sung mô tả `isSaved` + luật resolve. |
| `BoardVerse.Tests/Services/BoardGameServiceTests.cs` | Sửa | 3 unit test mới cho `IsSaved` resolve: anonymous → false (verify repo KHÔNG được gọi), logged-in saved → true, logged-in not-saved → false. |
| `docs/api/board-games.md` | Cập nhật | Tài liệu này — section `GET /api/v1/board-games/{id}` bổ sung bảng luật resolve `isSaved` (anonymous / saved / not-saved) + AC checklist 1.3a/1.3b/1.3c. |
