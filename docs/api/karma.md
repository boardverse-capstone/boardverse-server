# KarmaController

**Base route:** `/api/v1/users/{userId}/karma` và `/api/v1/users/{userId}/karma/appeal`
**Controller:** `KarmaController.cs`
**Role:** Authenticated — user xem karma của chính mình; Admin xem của user khác.

Endpoints quản lý Karma violation (BR-KARMA-01 §4.3 + §9.5 + BR-KARMA-01..05):

- **BR-KARMA-01**: `riskScore` từ short-play violations.
- **BR-KARMA-02**: 3-4 active violations → warning notification.
- **BR-KARMA-03**: 5+ active violations → restrict 30 ngày (chỉ cho đặt slot >= 4 giờ).
- **BR-KARMA-04**: Monthly reset — violations cũ hơn 30 ngày tự expire.
- **BR-KARMA-05**: User submit appeal cho 1 violation; admin review.

---

## Mục lục

- [GET /users/{userId}/karma](#get-usersuseridkarma)
- [POST /users/{userId}/karma/appeal](#post-usersuseridkarmaappeal)

---

## GET /users/{userId}/karma

Lấy thông tin karma hiện tại của user.

### Request

- Method: `GET`
- Path: `/api/v1/users/{userId}/karma`
- Auth: Authenticated — chỉ xem của chính mình, hoặc Admin xem user khác.

### Response — 200 OK

```json
{
  "userId": "guid",
  "karmaPoints": 85,
  "karmaLevel": "Good"
}
```

| Field | Type | Description |
|---|---|---|
| userId | guid | UserId được query |
| karmaPoints | int | Điểm karma hiện tại (default 100) |
| karmaLevel | string | Excellent/Good/Average/Low/Poor/Critical |

### Errors

| Code | Description |
|---|---|
| 401 | Thiếu token hoặc token không hợp lệ |
| 403 | Không có quyền xem karma của user khác |
| 500 | Lỗi hệ thống |

---

## POST /users/{userId}/karma/appeal

User gửi khiếu nại cho 1 KarmaShortPlayRecord cụ thể.

### Request

- Method: `POST`
- Path: `/api/v1/users/{userId}/karma/appeal`
- Auth: Authenticated — chỉ gửi cho record của chính mình.

### Body

```json
{
  "recordId": "guid",
  "reason": "Tôi đã chơi đủ thời gian, có bằng chứng screenshot..."
}
```

| Field | Required | Description |
|---|---|---|
| recordId | Yes | KarmaShortPlayRecord.Id cần khiếu nại |
| reason | Yes | Lý do (không được trống) |

### Response — 200 OK

```json
{ "submitted": true }
```

### Errors

| Code | Description |
|---|---|
| 400 | Lý do trống hoặc record không thuộc user / đã được review |
| 401 | Thiếu token hoặc token không hợp lệ |
| 500 | Lỗi hệ thống |

### Workflow

1. User nhận notification từ server (warning hoặc restriction)
2. User gửi POST /appeal với `recordId` + `reason`
3. Server set `KarmaShortPlayRecord.AppealRequested = true`, `AppealReason = reason`
4. Admin review trong queue; approve → `Cleared`, reject → giữ nguyên
5. Nếu approve → restore `UserProfile.KarmaPoints`

---

## BR Mapping

| BR | Endpoint liên quan |
|---|---|
| BR-KARMA-01 | (tính toán nội bộ trong `KarmaService`) |
| BR-KARMA-02 | `KarmaService.SendWarningIfNeededAsync` (background trigger) |
| BR-KARMA-03 | `KarmaService.ApplyRestrictionIfNeededAsync` |
| BR-KARMA-04 | `KarmaService.ResetMonthlyAsync` (chạy mỗi ngày) |
| BR-KARMA-05 | `POST /users/{userId}/karma/appeal` |
| BR-KARMA-09 | `GET /users/{userId}/karma` chỉ trả `karmaLevel`, không trả `karmaPoints` chi tiết cho non-admin |

---

## KarmaViolationCategory enum (GAP-4 fix 2026-08-27)

Các violation type được ghi nhận qua `KarmaShortPlayRecord`:

|| ViolationType | Trigger | Karma penalty | ReservationId |
||---|---|---|---|
|| `ShortPlay` | Member checkout early với `playedRatio < 50%` | −20 | nullable |
|| `NoShow` | Member không check-in sau grace | −30 | nullable |
|| `HostDissolve` | **MỚI** — Host dissolve lobby sau grace period (2026-08-27) | −10 | nullable |

**HostDissolve (GAP-4 fix):**
- Trigger: Host gọi `DELETE /api/v1/lobbies/{lobbyId}` (dissolve) sau grace period.
- Grace period: 15 phút từ tạo lobby HOẶC trước khi có member join (BR-REFUND-03).
- Ngoài grace: `PlayerKarmaService.RecordHostDissolveAsync` ghi `KarmaShortPlayRecord` với `ViolationType = HostDissolve`.
- `ReservationId` được set nếu lobby có reservation (nullable cho legacy dissolve không reservation).
- Karma aggregation chạy `TriggerKarmaAggregationAsync` sau persist → warning (3-4 violations) hoặc restriction (5+) theo BR-KARMA-03.

---

## Tham chiếu

- `BoardVerse.Services/Services/KarmaService.cs` — high-level operations
- `BoardVerse.Services/Services/PlayerKarmaService.cs` — short-play recording
- `BoardVerse.Core/Entities/KarmaShortPlayRecord.cs` — entity
- `BoardVerse.Core/Enum/KarmaLevel.cs` — enum levels
- `BoardVerse.Core/Enum/KarmaRecordStatus.cs` — record status enum
- `BoardVerse.Core/Enum/KarmaViolationCategory.cs` — violation category enum