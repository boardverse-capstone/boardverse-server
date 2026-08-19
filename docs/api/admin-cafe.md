# AdminCafeController

**Base route:** `/api/v1/admin/cafes`
**Controller:** `AdminCafeController.cs`
**Role:** Admin

API Admin CRUD đầy đủ cho Cafe — tạo, cập nhật, xóa, và thiết lập trạng thái vận hành. Khác với `ManagerCafeProfileController` (chỉ chủ quán tự quản lý quán của mình).

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/` | GET | Danh sách tất cả cafes (phân trang, filter) |
| `/{cafeId}` | GET | Chi tiết một cafe |
| `/` | POST | Tạo cafe mới |
| `/{cafeId}` | PUT | Cập nhật cafe |
| `/{cafeId}` | DELETE | Xóa cafe |
| `/{cafeId}/operational-status` | PUT | Đặt trạng thái vận hành |

**Header:** `Authorization: Bearer <admin-token>`

---

## GET /api/v1/admin/cafes

Lấy danh sách tất cả cafes (phân trang, filter).

### Query

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `page` | int | No | Số trang (≥ 1). Mặc định 1 |
| `pageSize` | int | No | Số item/trang (1-100). Mặc định 20 |
| `searchTerm` | string | No | Tìm kiếm theo tên hoặc địa chỉ |
| `status` | string | No | Filter: `DATA_BLANK`, `ACTIVE`, `INACTIVE`, `BANNED` |
| `managerId` | guid | No | Filter theo manager |

### Response 200

```json
{
  "statusCode": 200,
  "message": "CafesRetrieved",
  "data": {
    "items": [
      {
        "id": "guid",
        "name": "BoardGame Cafe A",
        "address": "123 Main Street",
        "latitude": 10.776889,
        "longitude": 106.700806,
        "phoneNumber": "0909999999",
        "managerId": "guid",
        "managerName": "Manager A",
        "operationalStatus": "ACTIVE",
        "depositRefundPolicy": "Full",
        "createdAt": "2026-01-01T10:00:00Z"
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalCount": 10,
    "totalPages": 1
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `500` | Lỗi hệ thống |

---

## GET /api/v1/admin/cafes/{cafeId}

Lấy chi tiết một cafe.

### Response 200

```json
{
  "statusCode": 200,
  "message": "CafeRetrieved",
  "data": {
    "id": "guid",
    "name": "BoardGame Cafe A",
    "address": "123 Main Street",
    "latitude": 10.776889,
    "longitude": 106.700806,
    "phoneNumber": "0909999999",
    "description": "Best board game cafe in town",
    "managerId": "guid",
    "managerName": "Manager A",
    "managerEmail": "manager@example.com",
    "operationalStatus": "ACTIVE",
    "operationalStatusReason": null,
    "depositRefundPolicy": "Full",
    "billingModel": "TimeBased",
    "basePrice": 60000,
    "tieredBlockRate": 20000,
    "tieredBlockMinutes": 30,
    "createdAt": "2026-01-01T10:00:00Z",
    "updatedAt": "2026-08-01T14:30:00Z"
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `404` | Không tìm thấy cafe |
| `500` | Lỗi hệ thống |

---

## POST /api/v1/admin/cafes

Tạo cafe mới (Admin tạo thay manager).

### Body

```json
{
  "name": "BoardGame Cafe B",
  "address": "456 New Street, HCMC",
  "latitude": 10.780000,
  "longitude": 106.710000,
  "phoneNumber": "0919999999",
  "managerId": "guid",
  "description": "New board game cafe"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | ✅ | Tên cafe (5-200 ký tự) |
| `address` | string | ✅ | Địa chỉ (5-500 ký tự) |
| `latitude` | double | ✅ | Vĩ độ (-90 to 90) |
| `longitude` | double | ✅ | Kinh độ (-180 to 180) |
| `phoneNumber` | string | ✅ | Số điện thoại (10-11 số) |
| `managerId` | guid | ✅ | UserId của manager |
| `description` | string | No | Mô tả |

### Response 201

```json
{
  "statusCode": 201,
  "message": "CafeCreated",
  "data": {
    "id": "guid",
    "name": "BoardGame Cafe B",
    ...
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `400` | Dữ liệu không hợp lệ |
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `404` | Manager không tồn tại |
| `409` | Manager đã có cafe khác |
| `500` | Lỗi hệ thống |

---

## PUT /api/v1/admin/cafes/{cafeId}

Cập nhật thông tin cafe.

### Body

```json
{
  "name": "Updated Cafe Name",
  "address": "789 Updated Street",
  "latitude": 10.785000,
  "longitude": 106.715000,
  "phoneNumber": "0929999999",
  "description": "Updated description"
}
```

Tất cả fields đều optional.

### Response 200

```json
{
  "statusCode": 200,
  "message": "CafeUpdated",
  "data": {
    "id": "guid",
    "name": "Updated Cafe Name",
    ...
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `400` | Dữ liệu không hợp lệ |
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `404` | Không tìm thấy cafe |
| `500` | Lỗi hệ thống |

---

## DELETE /api/v1/admin/cafes/{cafeId}

Xóa cafe (chỉ khi không có dữ liệu quan trọng).

### Response 200

```json
{
  "statusCode": 200,
  "message": "CafeDeleted"
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `404` | Không tìm thấy cafe |
| `409` | Cafe có active sessions hoặc bookings |
| `500` | Lỗi hệ thống |

---

## PUT /api/v1/admin/cafes/{cafeId}/operational-status

Đặt trực tiếp trạng thái vận hành của quán.

### Body

```json
{
  "operationalStatus": "BANNED",
  "reason": "Vi phạm điều khoản hợp tác nhiều lần."
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `operationalStatus` | enum | ✅ | `DATA_BLANK`, `ACTIVE`, `INACTIVE`, `BANNED` |
| `reason` | string | ❌ | Bắt buộc khi `BANNED`. 5-500 ký tự |

### Status Behavior

| Status | Behavior |
|--------|----------|
| `DATA_BLANK` | Tạm ẩn quán, không hiển thị trên mobile |
| `ACTIVE` | Hiển thị cho player |
| `INACTIVE` | Quán ngừng kinh doanh — manager đã đóng cửa vĩnh viễn hoặc admin đặt |
| `BANNED` | Admin cấm do vi phạm chính sách — yêu cầu `reason` |

### Response 200

```json
{
  "statusCode": 200,
  "message": "OperationalStatusUpdated",
  "data": {
    "id": "guid",
    "name": "BoardGame Cafe A",
    "operationalStatus": "BANNED",
    "operationalStatusReason": "Vi phạm điều khoản hợp tác nhiều lần."
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `400` | `status` không hợp lệ hoặc thiếu `reason` khi `BANNED` |
| `401` | Thiếu token |
| `403` | Không phải Admin |
| `404` | Không tìm thấy cafe |
| `500` | Lỗi hệ thống |

---

## So sánh với `ManagerCafeProfileController`

| Controller | Ai đặt | Trạng thái có thể đặt |
|------------|--------|------------------------|
| `ManagerCafeProfileController` | Manager (chủ quán) | `activate` / `deactivate` / `close` / `reopen` |
| `AdminCafeController` (file này) | Admin | CRUD đầy đủ + `DATA_BLANK` / `ACTIVE` / `INACTIVE` / `BANNED` |

Admin có thêm quyền **`BANNED`** và CRUD đầy đủ — chỉ Admin mới có.

---

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/{cafeId}/operational-status` | PUT | Đặt trạng thái vận hành quán |

**Header:** `Authorization: Bearer <admin-token>`

---

## PUT /api/v1/admin/cafes/{cafeId}/operational-status

Đặt trực tiếp trạng thái vận hành của quán: `DATA_BLANK`, `ACTIVE`, `INACTIVE`, `BANNED`.

**Body:**
```json
{
  "operationalStatus": "BANNED",
  "reason": "Vi phạm điều khoản hợp tác nhiều lần."
}
```

| Field | Ràng buộc |
|-------|-----------|
| `operationalStatus` | enum: `DATA_BLANK`, `ACTIVE`, `INACTIVE`, `BANNED` |
| `reason` | **Bắt buộc** khi `BANNED`. 5–500 ký tự. |

**Hành vi:**

| Status | Hành động |
|--------|-----------|
| `DATA_BLANK` | Tạm ẩn quán, không hiển thị trên mobile |
| `ACTIVE` | Hiển thị cho player |
| `INACTIVE` | Quán ngừng kinh doanh — manager đã đóng cửa vĩnh viễn hoặc admin đặt |
| `BANNED` | Admin cấm do vi phạm chính sách — yêu cầu `reason` |

**Response 200:** thông tin quán sau cập nhật, kèm `operationalStatusReason` (khi `INACTIVE`/`BANNED`).

**Response codes:**
- `200` — Trạng thái đã cập nhật
- `400` — `status` không hợp lệ hoặc thiếu `reason` khi `BANNED`
- `401` — Thiếu/sai token
- `403` — Không có quyền Admin
- `404` — Không tìm thấy quán

---

## So sánh với `ManagerCafeProfileController`

| Controller | Ai đặt | Trạng thái có thể đặt |
|------------|--------|------------------------|
| `ManagerCafeProfileController` | Manager (chủ quán) | `activate` / `deactivate` / `close` / `reopen` |
| `AdminCafeController` (file này) | Admin | `DATA_BLANK` / `ACTIVE` / `INACTIVE` / `BANNED` |

Admin có thêm quyền **`BANNED`** — chỉ Admin mới đặt được.