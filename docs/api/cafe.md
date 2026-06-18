# CafeController

**Base route:** `/api/cafes`  
**Controller:** `CafeController.cs`

| Endpoint | Method | Role |
|----------|--------|------|
| `/nearby` | GET | Public (Player discovery — GPS query params) |
| `/nearby/me` | GET | Player (dùng vị trí đã lưu trên profile) |
| `/{id}` | GET | Public |
| `/{id}` | PUT | Manager (chủ quán) |
| `/{cafeId}/staff` | POST | Manager (chủ quán) |
| `/{cafeId}/staff/promote` | POST | Manager (chủ quán) |
| `/{cafeId}/staff` | GET | Manager (chủ quán) |
| `/{cafeId}/staff/{staffId}` | DELETE | Manager (chủ quán) |

> Lấy `cafeId` qua [GET /api/manager/my-cafes](./manager.md) thay vì hardcode.

---

## GET /api/cafes/nearby

Tìm quán đối tác **ACTIVE** gần vị trí player (PostGIS `geography` + GiST index). **Không cần token.**  
Dùng cho luồng **Khám phá game**: `gameTemplateId` **bắt buộc** — chỉ quán có ít nhất một hộp game (`CafeInventoryBoxes`) thuộc tựa đó, trạng thái `Available` hoặc `InUse` (AC 2.1, 3.1).

**Query:**

| Param | Mô tả | Mặc định |
|-------|--------|----------|
| `latitude` | Vĩ độ player (WGS84) | bắt buộc |
| `longitude` | Kinh độ player | bắt buộc |
| `gameTemplateId` | Tựa game player đã chọn | **bắt buộc** |
| `radiusKm` | Bán kính tìm kiếm (km) | `15` (0.1–50) |
| `pageNumber` | Trang | `1` |
| `pageSize` | Kích thước trang | `20` |

**Response 200:** wrapper `NearbyCafeSearchResultDto`:

| Field | Mô tả |
|-------|--------|
| `cafes` | Phân trang `NearbyCafeDto` (shape cũ nằm trong `cafes.data` + `cafes.meta`) |
| `emptyResultMessage` | Thông điệp UI khi **không có quán nào** (AC 5.1); `null` khi có kết quả |
| `alternativeSuggestions` | Game cùng thể loại còn hàng `Available` gần player (AC 5.2); `[]` khi có quán |

Mỗi phần tử `alternativeSuggestions`:

| Field | Mô tả |
|-------|--------|
| `gameTemplateId`, `gameName`, `thumbnailUrl` | Tựa game thay thế |
| `minPlayers`, `maxPlayers` | Giới hạn người chơi |
| `nearbyCafeCount` | Số quán trong bán kính có hộp `Available` |
| `nearestCafeDistanceMeters` | Khoảng cách quán gần nhất có hàng |
| `availableBoxCount` | Tổng hộp `Available` trong bán kính |
| `sharedCategories` | Thể loại trùng với game gốc |

Mỗi quán trong `cafes.data` (`NearbyCafeDto`):
| `distanceMeters` | Khoảng cách địa lý từ GPS player (PostGIS, sắp xếp tăng dần) |
| `availableGameCount` | Số hộp game `Available` (theo `gameTemplateId` nếu có, ngược lại tổng kho) |
| `availableTableCount` | Số bàn vật lý trạng thái `Available` (AC 2.3) |
| `totalTableCount` | Tổng số bàn active của quán (AC 2.3) |
| `totalGameBoxCount` | Tổng hộp game playable (`Available` + `InUse`) của tựa đã chọn |
| `selectedGameAvailabilityStatus` | `GameAvailable` hoặc `WaitingForGame` (AC 3.2 — UI: **Chờ game trống**) |
| `estimatedWaitMinutes` | Phút chờ ước tính khi `WaitingForGame` (AC 3.3); `null` khi còn hộp trống |

UI card ví dụ: **Còn trống {availableTableCount}/{totalTableCount} bàn** · **Chờ game ~{estimatedWaitMinutes} phút** khi `selectedGameAvailabilityStatus = WaitingForGame`.

**Response 200 — không có quán (AC 5.1, 5.2):**

```json
{
  "statusCode": 200,
  "message": "Nearby cafes retrieved successfully",
  "data": {
    "cafes": {
      "data": [],
      "meta": { "currentPage": 1, "pageSize": 20, "totalItems": 0, "totalPages": 0, "hasPrevious": false, "hasNext": false }
    },
    "emptyResultMessage": "Không tìm thấy địa điểm phù hợp có sẵn tựa game này xung quanh bạn.",
    "alternativeSuggestions": [
      {
        "gameTemplateId": "55555555-5555-5555-5555-555555555555",
        "gameName": "Werewolf Ultimate",
        "minPlayers": 5,
        "maxPlayers": 20,
        "nearbyCafeCount": 1,
        "nearestCafeDistanceMeters": 120.5,
        "availableBoxCount": 2,
        "sharedCategories": [{ "id": "c1111111-1111-1111-1111-111111111111", "name": "Ẩn vai", "slug": "an-vai" }]
      }
    ]
  }
}
```

Logic gợi ý: lấy `category_id` của game gốc → tìm game **khác** cùng ít nhất một thể loại → có `CafeInventoryBoxes` trạng thái **`Available`** tại quán ACTIVE trong bán kính → sắp xếp theo quán gần nhất.

**Công thức chờ (AC 3.3):** `GameTemplates.PlayTime` − thời gian đã chơi (từ `ActiveSessions.StartedAt` khi POS giao game). Lấy **min** trên các hộp `InUse` của tựa tại quán. Quán vẫn hiển thị khi tất cả hộp đang `InUse` (AC 3.1).

POS tạo/kết thúc session qua [CafePosController](./cafe-pos.md).

**Lỗi:** `400` tọa độ, bán kính, hoặc thiếu/không hợp lệ `gameTemplateId`.

---

## GET /api/cafes/nearby/me

Cùng logic và response như `GET /nearby`, nhưng dùng **tọa độ đã lưu** trên profile (`LastKnownLatitude` / `LastKnownLongitude`) thay vì query `latitude`/`longitude`. **Yêu cầu đăng nhập.**

**Luồng gợi ý (mobile):**

```
1. Lấy GPS thiết bị
2. PUT /api/userprofile/me/location   → lưu server
3. GET /api/cafes/nearby/me?gameTemplateId=...   → không cần gửi lại lat/lng
```

Hoặc gọi thẳng `GET /nearby?latitude=...&longitude=...&gameTemplateId=...` (public, không cần token).

**Query:**

| Param | Mô tả | Mặc định |
|-------|--------|----------|
| `gameTemplateId` | Tựa game đã chọn | **bắt buộc** |
| `radiusKm` | Bán kính (km) | `15` |
| `pageNumber` | Trang | `1` |
| `pageSize` | Kích thước trang | `20` |

**Lỗi:** `400` chưa lưu vị trí (`PUT me/location` trước); `401` thiếu token.

---

## GET /api/cafes/{id}

Xem thông tin quán — **không cần token**.

**Response 200:** `CafeDto` (id, name, address, latitude, longitude, phoneNumber, description, createdAt)

**Lỗi:** `404` cafe không tồn tại hoặc inactive.

---

## PUT /api/cafes/{id}

Cập nhật thông tin quán — chỉ **chủ quán**.

**Body (tất cả optional):**
```json
{
  "name": "BoardVerse Demo Cafe",
  "address": "456 New Street, HCMC",
  "latitude": 10.776889,
  "longitude": 106.700806,
  "phoneNumber": "0909999999",
  "description": "Updated description"
}
```

---

## Hai luồng thêm nhân viên

### Luồng A — Tài khoản mới
```
POST /api/cafes/{cafeId}/staff
{ "email", "username", "password"? }
```

### Luồng B — User đã đăng ký
```
POST /api/cafes/{cafeId}/staff/promote
{ "email", "username"?, "password"? }
```

### Luồng C — CafeStaff đã có, gắn thêm quán
```
POST /api/cafes/{cafeId}/staff
{ "email" }
```

| Tình huống | API |
|------------|-----|
| Email chưa có | `POST .../staff` (+ username bắt buộc) |
| Email là `Player` | `POST .../staff/promote` trước |
| Email là `CafeStaff` | `POST .../staff` (chỉ email) |
| Gọi `POST staff` khi vẫn là `Player` | `400` — message hướng dẫn gọi `/promote` |

---

## POST /api/cafes/{cafeId}/staff

```json
{
  "email": "staff@example.com",
  "username": "johndoe",
  "password": "Staff@1234"
}
```

| Field | Ràng buộc |
|-------|-----------|
| `email` | Bắt buộc |
| `username` | Bắt buộc khi tạo mới; bỏ qua khi gắn CafeStaff đã có |
| `password` | Tuỳ chọn, 8–100 ký tự |

---

## POST /api/cafes/{cafeId}/staff/promote

```json
{
  "email": "player@example.com",
  "username": "johndoe",
  "password": "Staff@1234"
}
```

Nâng `Player` → `CafeStaff` và gắn quán.

---

## DELETE /api/cafes/{cafeId}/staff/{staffId}

Gỡ nhân viên khỏi quán. Nếu staff **không còn quán nào** → role tự hạ về `Player`.

---

## GET /api/cafes/{cafeId}/staff

**Query:** `pageNumber`, `pageSize` (thống nhất với inventory — không dùng `page`).

**Response:** `userId`, `email`, `username`, `joinedAt`.
