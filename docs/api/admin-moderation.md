# AdminModerationController

**Base route:** `/api/v1/admin`  
**Controller:** `AdminModerationController.cs`  
**Role:** Admin

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/karma-logs` | GET | Lịch sử biến động Karma (phân trang, lọc) |
| `/users/alerts` | GET | User có Karma &lt; 50 |
| `/users/{id}/punish` | POST | Warning / Suspend / Ban (audit `PlayerActionHistory`) |
| `/users/{id}/adjust-karma` | POST | Điều chỉnh điểm Karma thủ công (audit `PlayerActionHistory`) |
| `/users/action-history` | GET | **Phase 8 (2026-08-12) — BR-RISK-05:** Lịch sử admin actions đã ghi vào `PlayerActionHistory`, filter theo user/action/date |
| `/alerts` | GET | **Phase 8 (2026-08-12) — BR-RISK-02:** List `PlayerAlert` (Open/Acknowledged/Resolved) |
| `/alerts/metrics` | GET | **Phase 8 (2026-08-12):** Counts theo severity/status để render dashboard |
| `/alerts/{alertId}/acknowledge` | POST | **Phase 8 (2026-08-12):** Admin đánh dấu đã xem alert |
| `/alerts/{alertId}/resolve` | POST | **Phase 8 (2026-08-12):** Đóng alert kèm note (audit `PlayerActionHistory`) |
| `/alerts/{alertId}/dismiss` | POST | **Phase 8 (2026-08-12):** Dismiss alert (false positive, audit `PlayerActionHistory`) |
| `/players/{userId}/risk-history` | GET | **Phase 8 (2026-08-12) — BR-RISK-11:** Lịch sử riskScore 365 ngày cho chart trend |
| `/cooling-off` | GET | BR-NEW-10 §XI: List users đang trong cooling-off |
| `/cooling-off/{userId}/release` | POST | BR-NEW-10 §XI.2: Release cooling-off manually |
| `/cooling-off/{userId}/extend` | POST | **Phase 7 (2026-08-12):** Admin extend cooling-off 1..90 ngày |
| `/players/{userId}/risk` | GET | **Phase 7 (2026-08-12) — BR-RISK-09:** Xem RiskScore, RiskMultiplier, Signals, CoolingOff (admin only) |

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

## GET /api/v1/admin/cooling-off

Lấy danh sách user đang trong trạng thái **cooling-off** (A-05).

**Query:**

| Param | Mô tả |
|-------|--------|
| `page` | Số trang (mặc định 1) |
| `pageSize` | Kích thước trang (mặc định 20) |

**Response 200:**

```json
{
  "data": {
    "items": [
      {
        "userId": "guid",
        "username": "user1",
        "karmaPoints": 20,
        "gamerTier": "Bronze",
        "isCoolingOff": true,
        "coolingOffExpiresAt": "2026-08-14T15:00:00Z",
        "riskMultiplier": 2.0,
        "failedLobbyCount": 3,
        "coolingOffTriggerReason": "3 lobby failures trong 7 ngày"
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalCount": 5,
    "totalPages": 1
  }
}
```

**Lỗi:** `401`, `403`.

---

## DELETE /api/v1/admin/cooling-off/{userId}

Release thủ công cooling-off cho một user (A-05).

**Response 200:**

```json
{
  "data": {
    "userId": "guid",
    "username": "user1",
    "isCoolingOff": false,
    "coolingOffExpiresAt": null,
    "riskMultiplier": 1.0
  }
}
```

**Lỗi:** `401`, `403`, `404` user không tồn tại.

---

## POST /api/v1/admin/cooling-off/{userId}/extend

**Phase 7 (2026-08-12) — BR-NEW-10 §XI.2:** Admin manually extend cooling-off thêm N ngày cho 1 user.
Dùng cho customer support hoặc escalation thủ công (khác với auto-extend `EscalateAsync` ở service).
Ghi audit log với `AdminActionType.AccountStatusChange` + metadata JSON (`adminUserId`, `targetUserId`, `previousExpiresAt`, `newExpiresAt`, `additionalDays`, `reason`, `action=ExtendCoolingOff`).

**Body:**

```json
{
  "additionalDays": 14,
  "reason": "Customer support extended for VIP user after manual review of dispute ticket #4231"
}
```

| Field | Mô tả |
|-------|--------|
| `additionalDays` | Số ngày gia hạn thêm (1..90) |
| `reason` | Lý do extend (10..1000 ký tự, ghi audit log) |

**Response 200:**

```json
{
  "data": {
    "userId": "guid",
    "previousExpiresAt": "2026-08-22T15:00:00Z",
    "newExpiresAt": "2026-09-05T15:00:00Z",
    "additionalDays": 14,
    "reason": "Customer support extended for VIP user after manual review of dispute ticket #4231",
    "extendedBy": "admin-guid",
    "extendedAt": "2026-08-22T15:00:00Z"
  }
}
```

**Validation:**

- `additionalDays ∈ [1, 90]`.
- `reason` ≥ 10 ký tự.
- User phải đang trong cooling-off (`isCoolingOff = true`).
- Validate trước khi extend: nếu `previousExpiresAt` còn hạn thì extend từ đó (`previous + additionalDays`), nếu đã quá hạn thì extend từ `now + additionalDays`.

**Audit log (`PlayerActionHistory`):**

```json
{
  "userId": "target-user-guid",
  "actionType": "AccountStatusChange",
  "actionBy": "admin-guid",
  "reason": "Admin extend cooling-off thêm 14 ngày: Customer support extended for VIP user...",
  "metadata": {
    "adminUserId": "admin-guid",
    "targetUserId": "target-user-guid",
    "previousExpiresAt": "2026-08-22T15:00:00Z",
    "newExpiresAt": "2026-09-05T15:00:00Z",
    "additionalDays": 14,
    "reason": "Customer support extended for VIP user after manual review of dispute ticket #4231",
    "action": "ExtendCoolingOff"
  }
}
```

**Lỗi:**

- `400`: `additionalDays` ngoài khoảng, `reason` quá ngắn.
- `401`: Thiếu token.
- `403`: Không có quyền Admin.
- `404`: Không tìm thấy ví của user.
- `409`: User không đang trong cooling-off.

---

## GET /api/v1/admin/players/{userId}/risk

**Phase 7 (2026-08-12) — BR-RISK-09:** Admin xem risk detail của 1 user (RiskScore, RiskMultiplier, Signals, CoolingOff).
**User bình thường KHÔNG được gọi endpoint này** — chỉ Admin mới thấy RiskScore + Signals. User thường chỉ thấy `RiskLevel` qua API riêng (xem `user-management.md`).

**Response 200:**

```json
{
  "data": {
    "userId": "target-user-guid",
    "username": "alice",
    "riskScore": 78,
    "riskLevel": "critical",
    "riskMultiplier": 2.0,
    "accountStatus": "restricted",
    "isCoolingOff": true,
    "coolingOffExpiresAt": "2026-09-12T15:00:00Z",
    "signals": {
      "SIG-01": 15,
      "SIG-03": 40,
      "SIG-08": 25
    },
    "actionHistoryCount": 7,
    "lastUpdated": "2026-08-12T15:00:00Z"
  }
}
```

**Field mapping (BR-RISK-01/03/04):**

| Field | Nguồn | Ý nghĩa |
|-------|-------|---------|
| `riskScore` | `Wallet.RiskScore` (0..100) | Điểm rủi ro |
| `riskLevel` | `Wallet.RiskLevel` enum | `low` (0..29), `medium` (30..49), `high` (50..74), `critical` (75..100) |
| `riskMultiplier` | `Wallet.RiskMultiplier` (1.0..2.0) | Hệ số nhân cọc |
| `accountStatus` | `Wallet.AccountStatus` enum | `active`, `warning`, `restricted`, `suspended`, `banned` |
| `isCoolingOff` | `Wallet.IsCoolingOff` | BR-NEW-10 |
| `coolingOffExpiresAt` | `Wallet.CoolingOffExpiresAt` | null nếu không cooling-off |
| `signals` | Parse từ `PlayerActionHistory.Metadata` (JSON) | Snapshot signals từ audit log gần nhất có `"signals"` key |
| `actionHistoryCount` | `COUNT(*)` `PlayerActionHistory` cho user | Tổng số audit entries (BR-RISK-05/06) |

**Lỗi:**

- `401`: Thiếu token.
- `403`: Không có quyền Admin.
- `404`: Không tìm thấy ví của user.

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

---

## GET /api/v1/admin/users/action-history *(Phase 8 — BR-RISK-05)*

Lấy lịch sử admin actions đã ghi vào `PlayerActionHistory`. Audit vĩnh viễn theo BR-RISK-05.

**Query:**

| Param | Type | Required | Mô tả |
|-------|------|----------|--------|
| `userId` | Guid | No | Lọc theo user bị áp dụng action |
| `actionType` | string | No | `Warning`/`Suspend`/`Ban`/`RiskScoreReset`/`VerifyRequired`/`PlayedTimeDisputed`/`PlayedTimeOverridden`/`AccountStatusChange` |
| `fromUtc` | DateTime | No | Ngày bắt đầu (UTC) |
| `toUtc` | DateTime | No | Ngày kết thúc (UTC) |
| `pageNumber` | int | No | Trang (mặc định 1) |
| `pageSize` | int | No | Kích thước trang (mặc định 20, max 100) |

**Response 200:**

```json
{
  "data": {
    "items": [
      {
        "id": "guid",
        "userId": "guid",
        "username": "player_a",
        "actionType": "Suspend",
        "actionBy": "guid",
        "actionByUsername": "admin_x",
        "reason": "Repeated no-show in lobbies",
        "metadata": "{\"beforeStatus\":\"Active\",\"afterStatus\":\"Suspended\",\"durationDays\":7}",
        "createdAt": "2026-08-12T10:00:00Z",
        "expiresAt": "2026-08-19T10:00:00Z"
      }
    ],
    "pageNumber": 1,
    "pageSize": 20,
    "totalCount": 47
  }
}
```

**Lỗi:** `401` thiếu token, `403` không phải Admin, `400` invalid date range.

---

## GET /api/v1/admin/alerts *(Phase 8 — BR-RISK-02)*

List `PlayerAlert` (BR-RISK-02 — auto-trigger khi riskScore vượt 30/50/75).

**Query:**

| Param | Type | Required | Mô tả |
|-------|------|----------|--------|
| `status` | string | No | `Open`/`Acknowledged`/`Resolved`/`Dismissed` |
| `severity` | string | No | `Info`/`Warning`/`Critical` |
| `alertType` | string | No | `AutoThresholdCrossed`/`MultiAccountDetected`/`ManualReport`/`AdminFlagged` |
| `pageNumber` | int | No | Trang (mặc định 1) |
| `pageSize` | int | No | Size (mặc định 20) |

**Response 200:** items gồm `id`, `userId`, `alertType`, `severity`, `signals` (JSON), `riskScoreSnapshot`, `createdAt`, `acknowledgedBy`, `acknowledgedAt`, `status`, `resolutionNote`.

---

## GET /api/v1/admin/alerts/metrics *(Phase 8)*

Counts để render dashboard (mục 18.12 `lobby-booking-deposit-bvc.mdc`):

```json
{
  "data": {
    "openCritical": 12,
    "openWarning": 47,
    "openInfo": 134,
    "acknowledgedAwaitingResolve": 23,
    "resolvedLast24h": 5
  }
}
```

---

## POST /api/v1/admin/alerts/{alertId}/acknowledge *(Phase 8)*

Admin đánh dấu đã xem alert. Update `Status = Acknowledged`, set `AcknowledgedBy`/`AcknowledgedAt`.

**Lỗi:** `404` alert không tồn tại, `409` alert đã `Resolved`/`Dismissed`.

---

## POST /api/v1/admin/alerts/{alertId}/resolve *(Phase 8)*

Đóng alert + ghi `PlayerActionHistory` audit. Update `Status = Resolved`, lưu `ResolutionNote`.

**Body:**

```json
{ "note": "User acknowledged warning, suspended 7 days as preventive measure" }
```

**Lỗi:** `400` note trống, `404` alert không tồn tại, `409` đã resolved/dismissed.

---

## POST /api/v1/admin/alerts/{alertId}/dismiss *(Phase 8)*

Dismiss alert (false positive). Ghi `PlayerActionHistory` audit. Update `Status = Dismissed`, lưu `ResolutionNote` với prefix `[False positive]`.

**Body:** giống `/resolve`.

---

## GET /api/v1/admin/players/{userId}/risk-history *(Phase 8 — BR-RISK-11)*

Lịch sử riskScore 365 ngày (BR-RISK-11) cho chart trend admin dashboard.

**Query:**

| Param | Type | Required | Mô tả |
|-------|------|----------|--------|
| `fromUtc` | DateTime | No | Mặc định now - 30 ngày |
| `toUtc` | DateTime | No | Mặc định now |

**Response 200:** array gồm `riskScore`, `riskLevel`, `snapshotDate`, `signals` (JSON), `createdAt`.
