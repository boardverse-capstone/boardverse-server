# BggController

**Base route:** `/api/v1/bgg`  
**Controller:** `BggController.cs`  
**Role:** Admin

Tích hợp **BoardGameGeek (BGG)** để Admin tìm game, xem trước metadata/linh kiện và import vào **master catalog** (`GameTemplates`).

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/component-catalog` | GET | Danh mục loại linh kiện chuẩn (cards, dice, meeples, …) |
| `/search` | GET | Tìm game trên BGG theo từ khóa |
| `/games/{bggId}` | GET | Xem trước metadata + linh kiện đã resolve |
| `/import` | POST | Import/cập nhật game vào master catalog |

> **Manager** không dùng API này — chỉ browse master catalog qua [Master Games](./master-games.md) và thêm vào kho quán.

**Yêu cầu:** Login bằng tài khoản **Admin**; cấu hình `Bgg:ApiToken` trong appsettings.

---

## Luồng import

```
GET /api/v1/bgg/search?query=catan
  → chọn bggId (vd. 13)

GET /api/v1/bgg/games/13?curatedComponentsOnly=false
  → xem preview (components, hasCuratedComponents)

POST /api/v1/bgg/import
  { "bggId": 13, "overwriteExisting": false, "curatedComponentsOnly": false }
  → gameTemplateId để Manager dùng nhập kho
```

---

## GET /api/v1/bgg/component-catalog

Trả về danh sách `BoardGameComponentKind` chuẩn (tên EN/VI, mô tả, số lượng gợi ý).

```http
GET /api/v1/bgg/component-catalog
Authorization: Bearer <admin-token>
```

**Response 200:**
```json
{
  "data": [
    {
      "kind": 1,
      "nameEn": "Playing Cards",
      "nameVi": "Thẻ bài",
      "description": "Standard playing or game-specific cards.",
      "typicalDefaultQuantity": 52
    }
  ]
}
```

**Lỗi:** `401`, `403`, `500`.

---

## GET /api/v1/bgg/search

**Query:**

| Param | Mô tả |
|-------|--------|
| `query` | Từ khóa (tối thiểu 2 ký tự) |

```http
GET /api/v1/bgg/search?query=wingspan
Authorization: Bearer <admin-token>
```

**Response 200:**
```json
{
  "data": [
    {
      "bggId": 266192,
      "name": "Wingspan",
      "yearPublished": 2019
    }
  ]
}
```

**Lỗi:**

| Code | Khi nào |
|------|---------|
| `400` | Query rỗng hoặc &lt; 2 ký tự |
| `401` | Thiếu/sai token |
| `403` | Không phải Admin |
| `500` | BGG API không phản hồi |

---

## GET /api/v1/bgg/games/{bggId}

**Route:** `bggId` — số nguyên dương trên BGG.

**Query:**

| Param | Mặc định | Mô tả |
|-------|----------|--------|
| `curatedComponentsOnly` | `false` | `true` → chỉ linh kiện từ `GameCatalog` nội bộ |

```http
GET /api/v1/bgg/games/13?curatedComponentsOnly=false
Authorization: Bearer <admin-token>
```

**Response 200:** `BggGamePreviewDto` — name, players, playTime, categories, mechanics, `components[]`, `hasCuratedComponents`, `componentResolutionNote`.

**Lỗi:**

| Code | Khi nào |
|------|---------|
| `400` | `bggId` ≤ 0 |
| `404` | BGG không trả game hoặc parse lỗi |
| `401` / `403` / `500` | Như trên |

---

## POST /api/v1/bgg/import

**Body:**

| Field | Mô tả |
|-------|--------|
| `bggId` | Bắt buộc — ID trên BGG |
| `overwriteExisting` | `true` → ghi đè metadata + thay linh kiện nếu đã có (theo BggId hoặc tên) |
| `curatedComponentsOnly` | `true` → chỉ dùng linh kiện curated |

```http
POST /api/v1/bgg/import
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "bggId": 13,
  "overwriteExisting": false,
  "curatedComponentsOnly": false
}
```

**Response 201** (tạo mới) / **200** (cập nhật khi `overwriteExisting=true`):
```json
{
  "data": {
    "gameTemplateId": "11111111-1111-1111-1111-111111111111",
    "bggId": 13,
    "name": "Catan",
    "created": true,
    "componentCount": 12,
    "categoryCount": 3,
    "primaryComponentSource": 1
  }
}
```

Import tự **map thể loại** từ BGG categories/mechanics sang slug nội bộ (nếu khớp `Categories` active).

**Lỗi:**

| Code | Khi nào |
|------|---------|
| `400` | `bggId` không hợp lệ hoặc không resolve được linh kiện |
| `404` | BGG không trả game |
| `409` | Game đã tồn tại, `overwriteExisting=false` |
| `401` / `403` / `500` | Như trên |

---

## Ghi chú linh kiện

- BGG **không** cung cấp danh sách hộp game có cấu trúc.
- Backend ưu tiên `GameCatalog` (curated theo `BggId`); nếu không có thì suy luận từ mechanics BGG.
- Import yêu cầu ít nhất một linh kiện đã resolve.

---

## Ghi chú Weight (averageweight)

`Weight` (BGG Complexity Weight, 1.0 → 5.0) lấy từ `<statistics><averageweight>` trong XML BGG.

**Retry logic 2 lớp (đã implement):**

1. **HTTP 202 Accepted** — retry với `RetryDelayMilliseconds` (mặc định 2000ms), tối đa `MaxRetryAttempts` lần (mặc định 5).
2. **XML thiếu `<statistics>`** — nếu BGG trả 200 OK nhưng XML không có khối `<statistics>` (BGG chưa kịp tính averageweight do async queue), backend retry **1 lần** với `StatsRetryDelayMilliseconds` (mặc định 30 000ms = 30s).

**Sau khi import, nếu `Weight = null` vẫn xảy ra:**

- BGG chưa đủ vote cho game đó (game quá mới hoặc niche).
- Backend log warning:
  ```
  BGG import: GameTemplate {GameId} (BggId={BggId}, Name={Name}) has no averageweight from BGG.
  Admin should set Weight manually via PUT /api/v1/admin/master-games/{GameId}.
  ```
- Admin dùng:
  - `GET /api/v1/admin/master-games/missing-weight` — danh sách game thiếu Weight.
  - `PUT /api/v1/admin/master-games/{gameTemplateId}` với `{ "weight": 2.5 }` để set thủ công.

**Config:**

| Setting | Mặc định | Mô tả |
|---|---|---|
| `Bgg:MaxRetryAttempts` | 5 | Số lần retry HTTP 202 / transport error. |
| `Bgg:RetryDelayMilliseconds` | 2000 | Delay giữa các lần retry HTTP. |
| `Bgg:StatsRetryDelayMilliseconds` | 30000 | Delay khi retry do XML thiếu `<statistics>`. |
