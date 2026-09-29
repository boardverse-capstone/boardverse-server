# AdminMasterCatalogController

**Base route:** `/api/v1/admin`  
**Controller:** `AdminMasterCatalogController.cs`  
**Role:** Admin

Quản lý **master catalog**: thể loại (`Categories`), linh kiện và gán thể loại cho `GameTemplate`.

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/categories` | GET | Danh sách thể loại |
| `/categories` | POST | Tạo thể loại |
| `/categories/{id}` | PUT | Cập nhật thể loại |
| `/categories/{id}` | DELETE | Vô hiệu hóa (soft delete) |
| `/master-games/{gameTemplateId}/components` | GET | Linh kiện của game |
| `/master-games/{gameTemplateId}/components` | POST | Thêm linh kiện |
| `/master-games/{gameTemplateId}/components/{componentId}` | PUT | Sửa linh kiện |
| `/master-games/{gameTemplateId}/components/{componentId}` | DELETE | Xóa linh kiện |
| `/master-games/{gameTemplateId}/categories` | GET | Thể loại đang gán |
| `/master-games/{gameTemplateId}/categories` | PUT | Gán lại toàn bộ thể loại |
| `/master-games/{gameTemplateId}` | PUT | Cập nhật metadata master game |
| `/master-games/{gameTemplateId}/thumbnail` | PATCH | Cập nhật thumbnail |

Import game từ BGG (kèm auto-map categories): [BGG API](./bgg.md).

---

## GET /api/v1/admin/categories

**Query:** `includeInactive` (bool, mặc định `false`).

**Response 200:** mảng `AdminCategoryResponseDto`:

| Field | Mô tả |
|-------|--------|
| `id`, `name`, `slug` | Định danh và slug URL |
| `description` | Mô tả tuỳ chọn |
| `sortOrder` | Thứ tự hiển thị |
| `isActive` | `false` = đã vô hiệu hóa |
| `createdAt`, `updatedAt` | Audit |

---

## POST /api/v1/admin/categories

**Body:**

```json
{
  "name": "Chiến thuật",
  "slug": "chien-thuat",
  "description": "Game chiến thuật",
  "sortOrder": 10
}
```

| Field | Ràng buộc |
|-------|-----------|
| `name` | Bắt buộc, 2–100 ký tự |
| `slug` | Tuỳ chọn — tự sinh từ `name` (bỏ dấu, lowercase, hyphen) |
| `sortOrder` | 0–9999 |

**Response 201:** category đã tạo.

**Lỗi:** `409` slug trùng.

---

## PUT /api/v1/admin/categories/{id}

Chỉ gửi field cần đổi: `name`, `slug`, `description`, `sortOrder`, `isActive`.

---

## DELETE /api/v1/admin/categories/{id}

Soft delete — đặt `isActive = false`. Không xóa cứng.

---

## Components — `/master-games/{gameTemplateId}/components`

### GET

Trả danh sách linh kiện master của game.

### POST

```json
{
  "componentName": "Meeple x5",
  "componentKind": 3,
  "defaultQuantity": 5
}
```

| Field | Mô tả |
|-------|--------|
| `componentName` | Bắt buộc |
| `componentKind` | `BoardGameComponentKind` (tuỳ chọn) — xem [BGG component-catalog](./bgg.md) |
| `defaultQuantity` | 1–9999, mặc định 1 |

**Response 201.**

### PUT `.../components/{componentId}`

Partial update: `componentName`, `componentKind`, `defaultQuantity`.

### DELETE `.../components/{componentId}`

Xóa cứng linh kiện. **Lỗi `409`** nếu linh kiện đang được tham chiếu bởi cafe inventory penalties.

---

## Categories trên game — `/master-games/{gameTemplateId}/categories`

### GET

Danh sách thể loại đang gán cho game.

### PUT

**Thay thế toàn bộ** danh sách thể loại (không merge):

```json
{
  "categoryIds": [
    "c1111111-1111-1111-1111-111111111111",
    "c2222222-2222-2222-2222-222222222222"
  ]
}
```

Chỉ chấp nhận category **active**. **Lỗi `400`** nếu id không tồn tại hoặc inactive.

---

## PUT /api/v1/admin/master-games/{gameTemplateId}

Cập nhật metadata của master game (tên, mô tả, min/max players, play time, designer, …).

**Body (partial):**
```json
{
  "name": "Catan (Updated)",
  "description": "...",
  "minPlayers": 3,
  "maxPlayers": 4,
  "playTimeMinutes": 90,
  "designer": "Klaus Teuber",
  "yearPublished": 1995
}
```

| Field | Ràng buộc |
|-------|-----------|
| `name` | Optional, 2-200 ký tự |
| `minPlayers` | ≥ 1 |
| `maxPlayers` | ≥ `minPlayers` |
| `playTimeMinutes` | > 0 |
| `designer` | Optional |
| `yearPublished` | Optional, 1900-2100 |

**Response codes:**
- `200` — Cập nhật thành công
- `400` — Dữ liệu không hợp lệ (vd: `maxPlayers < minPlayers`)
- `404` — Không tìm thấy game
- `409` — Tên trùng với game khác

---

## PATCH /api/v1/admin/master-games/{gameTemplateId}/thumbnail

Cập nhật riêng thumbnail URL của game (tách thành endpoint riêng vì file ảnh lớn + thường xuyên đổi).

**Body:**
```json
{ "thumbnailUrl": "https://cdn.boardverse.vn/games/catan.jpg" }
```

**Response codes:**
- `200` — Cập nhật thành công
- `400` — URL không hợp lệ
- `404` — Không tìm thấy game

> **Lưu ý:** Backend không upload file — chỉ lưu URL do client gửi. Xem [third-party-services.md](../third-party-services.md) — không có CDN/file storage tích hợp.

---

## GET /api/v1/admin/master-games/missing-weight

Liệt kê board game master đang **thiếu `Weight`** (giá trị `Weight` = null).

**Dùng cho:** Admin kiểm tra các game import từ BGG mà BGG chưa trả về `averageweight` (do game quá mới / chưa đủ vote cộng đồng), hoặc admin tạo game master thủ công nhưng quên set `Weight`. Sau đó dùng [`PUT /api/v1/admin/master-games/{gameTemplateId}`](#put-apiv1adminmaster-gamesgametemplateid) với `{ "weight": 2.5 }` để bù.

**Query:**

| Param | Mặc định | Mô tả |
|---|---|---|
| `includeInactive` | `false` | Khi `true`, bao gồm cả game đã vô hiệu hóa (`IsActive = false`). |

**Response 200:**
```json
{
  "data": [
    {
      "id": "11111111-1111-1111-1111-111111111111",
      "name": "Some New Game",
      "searchAliases": null,
      "thumbnailUrl": null,
      "description": null,
      "bggId": 123456,
      "weight": null,
      "isActive": true,
      "minPlayers": 2,
      "maxPlayers": 4,
      "playTime": 60,
      "createdAt": "...",
      "updatedAt": "..."
    }
  ]
}
```

Danh sách sort theo `name` tăng dần (alphabetical).

**Response codes:**
- `200` — Lấy danh sách thành công (có thể rỗng nếu tất cả game đều đã set Weight).
- `401` — Thiếu/sai token.
- `403` — Không phải Admin.
