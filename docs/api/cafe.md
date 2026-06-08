# CafeController

**Base route:** `/api/cafes`  
**Controller:** `CafeController.cs`

| Endpoint | Method | Role |
|----------|--------|------|
| `/{id}` | GET | Public |
| `/{id}` | PUT | Manager (chủ quán) |
| `/{cafeId}/staff` | POST | Manager (chủ quán) |
| `/{cafeId}/staff/promote` | POST | Manager (chủ quán) |
| `/{cafeId}/staff` | GET | Manager (chủ quán) |
| `/{cafeId}/staff/{staffId}` | DELETE | Manager (chủ quán) |

> Lấy `cafeId` qua [GET /api/manager/my-cafes](./manager.md) thay vì hardcode.

---

## GET /api/cafes/{id}

Xem thông tin quán — **không cần token**.

**Response 200:** `CafeDto` (id, name, address, phoneNumber, description, createdAt)

**Lỗi:** `404` cafe không tồn tại hoặc inactive.

---

## PUT /api/cafes/{id}

Cập nhật thông tin quán — chỉ **chủ quán**.

**Body (tất cả optional):**
```json
{
  "name": "BoardVerse Demo Cafe",
  "address": "456 New Street, HCMC",
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
| Email là `User` | `POST .../staff/promote` trước |
| Email là `CafeStaff` | `POST .../staff` (chỉ email) |
| Gọi `POST staff` khi vẫn là `User` | `400` — message hướng dẫn gọi `/promote` |

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

Nâng `User` → `CafeStaff` và gắn quán.

---

## DELETE /api/cafes/{cafeId}/staff/{staffId}

Gỡ nhân viên khỏi quán. Nếu staff **không còn quán nào** → role tự hạ về `User`.

---

## GET /api/cafes/{cafeId}/staff

**Query:** `pageNumber`, `pageSize` (thống nhất với inventory — không dùng `page`).

**Response:** `userId`, `email`, `username`, `joinedAt`.
