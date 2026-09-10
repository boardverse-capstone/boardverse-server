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
| `/{id}/play-configuration` | GET | Kiểm tra min/max người và chế độ chơi khả dụng |
| `/{id}/play-navigation` | POST | Điều hướng Solo Booking hoặc tạo phòng chờ nhóm |

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
| 1.3 Chi tiết | `GET /api/v1/board-games/{id}` | Đủ ảnh, tên, mô tả, min/max người, `components[]` |

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
