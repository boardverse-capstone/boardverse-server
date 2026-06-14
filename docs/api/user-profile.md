# UserProfileController

**Base route:** `/api/userprofile`  
**Controller:** `UserProfileController.cs`  
**Role:** Player, Manager, CafeStaff, Admin — yêu cầu đăng nhập

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/` | GET | Hồ sơ của tôi |
| `/` | POST | Tạo hồ sơ (lần đầu) |
| `/` | PUT | Cập nhật hồ sơ |
| `/` | DELETE | Vô hiệu hóa hồ sơ |
| `/progress` | POST | Cập nhật Elo / level |
| `/me/avatar` | PUT | Đổi avatar |
| `/me/location` | GET | Vị trí gần nhất đã lưu (chỉ bản thân) |
| `/me/location` | PUT | Cập nhật vị trí hiện tại (GPS/map) |
| `/me/location` | DELETE | Xóa vị trí trên profile |
| `/me/karma-history` | GET | Trạng thái karma |

**Header bắt buộc:** `Authorization: Bearer <token>`

---

## Cách dùng — luồng người chơi mới

```
1. POST /api/auth/register     → tạo tài khoản
2. POST /api/auth/login      → lấy token
3. GET  /api/userprofile       → 200 với giá trị mặc định (chưa có hồ sơ chi tiết)
4. POST /api/userprofile       → tạo hồ sơ lần đầu
5. GET  /api/userprofile       → xem hồ sơ đầy đủ
```

> Mỗi user chỉ có **một** hồ sơ. Gọi `POST` lần 2 sẽ lỗi `409`.

### PowerShell mẫu

```powershell
# Login
$login = curl.exe -s -X POST http://localhost:5022/api/auth/login `
  -H "Content-Type: application/json" `
  -d '{\"usernameOrEmail\":\"alice@example.com\",\"password\":\"P@ssw0rd!\"}' `
  | ConvertFrom-Json
$token = $login.data.token

# Tạo profile
curl.exe -X POST http://localhost:5022/api/userprofile `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -d '{\"bio\":\"Board game fan\",\"firstName\":\"Alice\",\"lastName\":\"Nguyen\"}'

# Xem profile
curl.exe http://localhost:5022/api/userprofile `
  -H "Authorization: Bearer $token"
```

---

## GET /api/userprofile

Lấy hồ sơ **của chính user đang đăng nhập** (không cần truyền userId).

**Response 200:**
```json
{
  "data": {
    "userId": "guid",
    "username": "alice",
    "avatarUrl": null,
    "bio": "Board game fan",
    "karmaPoints": 0,
    "gamerTier": "Bronze",
    "globalElo": 1200,
    "level": 1,
    "updatedAt": "2026-06-08T12:00:00Z"
  }
}
```

| Field | Mô tả |
|-------|--------|
| `karmaPoints` | Điểm karma tích lũy |
| `gamerTier` | Hạng (Bronze, Silver, …) theo karma |
| `globalElo` | Điểm Elo toàn cục |
| `level` | Cấp độ người chơi |

**Lỗi:** `401` thiếu/sai token, `403` tài khoản bị chặn, `404` user trong token không tồn tại.

---

## POST /api/userprofile

Tạo hồ sơ lần đầu sau khi đăng ký.

**Body (tất cả optional):**
```json
{
  "bio": "Board game enthusiast",
  "firstName": "Alice",
  "lastName": "Nguyen",
  "dateOfBirth": "1998-01-01T00:00:00Z"
}
```

| Field | Ràng buộc |
|-------|-----------|
| `bio` | Max 1000 ký tự |
| `firstName`, `lastName` | Max 100 ký tự |
| `dateOfBirth` | ISO 8601 date |

**Response 201:** Trả `ProfileDetailDto` (gồm cả PII: firstName, lastName, …).

**Lỗi:**
- `409` — đã có profile
- `404` — user không tồn tại
- `401` — thiếu token

---

## PUT /api/userprofile

Cập nhật một phần — chỉ gửi field cần đổi.

**Body (tất cả optional):**
```json
{
  "bio": "Love strategy games",
  "globalElo": 1350,
  "level": 5,
  "firstName": "Alice",
  "lastName": "Tran",
  "dateOfBirth": "1998-01-01T00:00:00Z"
}
```

| Field | Ràng buộc |
|-------|-----------|
| `globalElo` | ≥ 0 |
| `level` | ≥ 1 |

**Response 200:** Profile đã cập nhật.

---

## POST /api/userprofile/progress

Cập nhật riêng Elo và level (dùng khi có kết quả trận đấu).

**Body (bắt buộc cả hai):**
```json
{
  "globalElo": 1350,
  "level": 5
}
```

**Response 200:** Profile với Elo/level mới.

---

## PUT /api/userprofile/me/avatar

**Body:**
```json
{
  "avatarUrl": "https://example.com/avatars/alice.png"
}
```

| Field | Ràng buộc |
|-------|-----------|
| `avatarUrl` | Bắt buộc, phải là URL hợp lệ |

**Response 200:** Profile với `avatarUrl` mới.

---

## GET /api/userprofile/me/karma-history

Trả **trạng thái karma hiện tại** (không phải lịch sử từng sự kiện).

**Response 200:**
```json
{
  "data": {
    "userId": "guid",
    "username": "alice",
    "karmaPoints": 120,
    "gamerTier": "Silver",
    "avatarUrl": "https://...",
    "updatedAt": "2026-06-08T12:00:00Z"
  }
}
```

**Lỗi:** `401` thiếu/sai token, `403` tài khoản bị chặn, `404` user không tồn tại.

---

## DELETE /api/userprofile

Vô hiệu hóa (soft-delete) profile của user đang đăng nhập.

**Response 200:** `data: null`

**Lỗi:** `401` thiếu token.

---

## GET /api/userprofile/me/location

Vị trí **gần nhất** đã lưu trên server — **chỉ user đăng nhập** xem được (không có trên public profile).

**Response 200:**
```json
{
  "data": {
    "latitude": 10.776889,
    "longitude": 106.700806,
    "updatedAt": "2026-06-14T10:00:00Z",
    "source": "Gps",
    "hasLocation": true
  }
}
```

Chưa từng lưu → `hasLocation: false`, các field tọa độ `null`.

---

## PUT /api/userprofile/me/location

Cập nhật vị trí khi app mở map / lấy GPS. Backend:
- Ghi **LastKnown** trên `UserProfiles` (đọc nhanh)
- Append **PlayerLocationHistories** (audit)

**Body:**
```json
{
  "latitude": 10.776889,
  "longitude": 106.700806,
  "source": 0
}
```

| `source` | Ý nghĩa |
|----------|---------|
| `0` | `Gps` — thiết bị |
| `1` | `Manual` — chọn trên map |

**Response 200:** cùng shape `PlayerLocationDto` như GET.

**Lỗi:** `400` tọa độ ngoài [-90,90] / [-180,180].

**Luồng gợi ý:** App mở → `PUT me/location` → gọi `GET /api/cafes/nearby/me?gameTemplateId=...` hoặc `GET /api/cafes/nearby?latitude=...&longitude=...&gameTemplateId=...`.

---

## DELETE /api/userprofile/me/location

Xóa vị trí trên profile (opt-out privacy). **Không** xóa bảng history audit.

**Response 200:** `data: null`

---

## Lưu ý khi tích hợp frontend

1. Sau `login`, lưu `token` vào memory/localStorage
2. Gắn `Authorization: Bearer <token>` vào mọi request profile
3. Nếu `GET /api/userprofile` trả lỗi hoặc profile trống → hiện form tạo profile (`POST`)
4. Khi token hết hạn (`401`) → gọi `POST /api/auth/refresh-token` rồi thử lại
5. `PUT` chỉ gửi field thay đổi — không cần gửi toàn bộ object
