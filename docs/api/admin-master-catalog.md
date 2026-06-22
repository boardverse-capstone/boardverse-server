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
