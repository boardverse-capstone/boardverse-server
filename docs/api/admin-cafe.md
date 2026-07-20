# AdminCafeController

**Base route:** `/api/v1/admin/cafes`  
**Controller:** `AdminCafeController.cs`  
**Role:** Admin

API Admin can thiệp trạng thái vận hành của quán đối tác — dùng khi cần cấm/hủy kích hoạt từ phía hệ thống. Khác với `ManagerCafeProfileController` (chỉ chủ quán tự quản lý quán của mình).

> **Liên quan:** [cafe-partner.md](./cafe-partner.md) cho flow Phase 1 (đăng ký) + Phase 2 (manager tự vận hành).

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