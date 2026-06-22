# AdminModerationController

**Base route:** `/api/v1/admin`  
**Controller:** `AdminModerationController.cs`  
**Role:** Admin

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/karma-logs` | GET | Lịch sử biến động Karma (phân trang, lọc) |
| `/users/alerts` | GET | User có Karma &lt; 50 |
| `/users/{id}/punish` | POST | Warning / Suspend / Ban |
| `/users/{id}/adjust-karma` | POST | Điều chỉnh điểm Karma thủ công |

**Header:** `Authorization: Bearer <admin-token>`

> Ban nhanh (không suspend có thời hạn): [User Management](./user-management.md) `POST .../block`.  
> Suspend có `lockoutEndDate`: dùng endpoint `punish` với `actionType: Suspend`.

---

## GET /api/v1/admin/karma-logs

**Query:**

| Param | Mô tả |
|-------|--------|
| `userId` | Lọc theo user bị ảnh hưởng |
| `violationCategory` | `CrossRating`, `NoShow`, `LateDepositCancel`, `KickedFromLobby`, `AdminManual`, `AdminWarning` |
| `fromUtc` | Thời điểm bắt đầu (UTC) |
| `toUtc` | Thời điểm kết thúc (UTC) |
| `pageNumber` | Trang (mặc định 1) |
| `pageSize` | Kích thước trang (mặc định 20) |

**Response 200 — mỗi log:**

| Field | Mô tả |
|-------|--------|
| `userId` | User **bị ảnh hưởng** (target) |
| `username` | Username của target |
| `violationCategory` | Loại vi phạm / sự kiện |
| `source` | `PlayerCrossRating`, `SystemAutomatic`, `AdminManual` |
| `karmaPointsChange` | Số điểm karma **thay đổi** (+/-). `0` nếu chỉ warning |
| `karmaBefore` | Điểm trước sự kiện |
| `karmaAfter` | Điểm sau sự kiện |
| `reason` | Mô tả chi tiết |
| `relatedLobbyId` | Lobby liên quan (nếu có) |
| `performedByUserId` | User **thực hiện** hành động (admin, người rate) |
| `isAdminAdjustment` | `true` khi admin điều chỉnh karma thủ công |
| `createdAt` | Thời điểm ghi log |

```json
{
  "data": {
    "data": [
      {
        "id": "guid",
        "userId": "target-user-guid",
        "username": "alice",
        "violationCategory": "AdminManual",
        "source": "AdminManual",
        "karmaPointsChange": -5,
        "karmaBefore": 100,
        "karmaAfter": 95,
        "reason": "Toxic chat in lobby",
        "relatedLobbyId": null,
        "performedByUserId": "admin-guid",
        "isAdminAdjustment": true,
        "createdAt": "2026-06-17T10:00:00Z"
      }
    ],
    "meta": { "currentPage": 1, "pageSize": 20, "totalItems": 1, "totalPages": 1 }
  }
}
```

**Breaking change (frontend):** field cũ `deltaAmount` → `karmaPointsChange`, `actorUserId` → `performedByUserId`.

**Lỗi:** `400` violationCategory không hợp lệ, `401`, `403`.

---

## GET /api/v1/admin/users/alerts

Trả danh sách user có `karmaPoints < 50` (cảnh báo admin).

**Response 200:** mảng user alert (id, username, karmaPoints, gamerTier, …).

---

## POST /api/v1/admin/users/{id}/punish

**Body:**

```json
{
  "actionType": "Suspend",
  "durationDays": 7,
  "reason": "Repeated no-show in lobbies"
}
```

| Field | Mô tả |
|-------|--------|
| `actionType` | `Warning`, `Suspend`, `Ban` |
| `durationDays` | **Bắt buộc** khi `Suspend` (1–365) |
| `reason` | 5–1000 ký tự |

**Hành vi:**

| actionType | User | Karma log |
|------------|------|-----------|
| `Warning` | Không đổi `accountStatus` | Ghi log, `karmaPointsChange = 0` |
| `Suspend` | `accountStatus = Suspended`, set `lockoutEndDate`, `blockReason`, `blockedAt` | Không ghi karma log |
| `Ban` | `accountStatus = Banned`, `lockoutEndDate = null` | Không ghi karma log |

**Response 200:**

```json
{
  "data": {
    "userId": "guid",
    "actionType": "Suspend",
    "accountStatus": "Suspended",
    "lockoutEndDate": "2026-06-24T10:00:00Z",
    "reason": "Repeated no-show in lobbies"
  }
}
```

**Lỗi:** `400` thiếu `durationDays` khi Suspend, `403` target là Admin, `404` user không tồn tại.

---

## POST /api/v1/admin/users/{id}/adjust-karma

**Body:**

```json
{
  "amount": -10,
  "reason": "Manual correction after appeal"
}
```

| Field | Mô tả |
|-------|--------|
| `amount` | ±1 đến ±100, **không được 0** |
| `reason` | 5–1000 ký tự |

Ghi `KarmaLog` với `isAdminAdjustment = true`, `performedByUserId` = admin đang gọi API.

**Response 200:** karma mới, tier, log id.

**Lỗi:** `400` amount = 0, `404` profile không tồn tại.
