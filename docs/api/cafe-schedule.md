# CafeScheduleController

**Base route:** `/api/v1/cafes/{cafeId:guid}/schedule-overrides`
**Controller:** `BoardVerse.API/Controllers/CafeScheduleController.cs`
**Auth:** `[Authorize]` (Cafe Manager hoặc Staff của cafe tương ứng)
**Service:** `ICafeScheduleService` (`BoardVerse.Services/Services/CafeScheduleService.cs`)

API cho phép Cafe Manager tùy chỉnh **giờ mở/đóng cửa** cho từng ngày cụ thể, hoặc **đánh dấu đóng cửa** một ngày. Dùng để hỗ trợ cafe mở khuya, cafe **24/7** (override `OpenTime=00:00`, `CloseTime=23:59`), hoặc cafe nghỉ lễ.

> **Liên quan:**
> - [lobby-booking-deposit-bvc.mdc](../.cursor/rules/lobby-booking-deposit-bvc.mdc) §7.1 (BR-NEW-15) — bỏ `TimeSlot` enum, dùng `ApplyDate/OpenTime/CloseTime` trực tiếp.
> - [booking.md](./booking.md), [reservation.md](./reservation.md) — luồng reservation dùng schedule đã resolve để validate `preferredStartTime` / `preferredEndTime`.
> - `BoardVerse.Core/Constants/CafeSchedule.cs` — `DefaultOpenTime` (06:00) / `DefaultCloseTime` (23:00).

---

## Nguyên tắc

1. **`ApplyDate`** là khóa chính — mỗi ngày chỉ có tối đa 1 override (DB unique constraint `(CafeId, ApplyDate)`).
2. **Không còn `TimeSlot` enum** (deprecated 2026-08-18 theo BR-NEW-15). Mỗi ngày có duy nhất 1 cặp `OpenTime/CloseTime`.
3. **Default schedule** lấy từ `CafeSchedule.DefaultOpenTime` (06:00) / `CafeSchedule.DefaultCloseTime` (23:00). Áp dụng cho ngày không có override.
4. **`CafeScheduleOverride`** cho phép tùy chỉnh per-day:
   - Set `OpenTime` / `CloseTime` riêng cho ngày đó.
   - Đánh dấu `IsClosed: true` để chặn player tạo lobby/booking vào ngày đó.
5. **Resolve logic** (`CafeScheduleResolver` / `IScheduleResolver`):
   - Ưu tiên override nếu tồn tại cho `ApplyDate`.
   - Fallback `DefaultOpenTime` / `DefaultCloseTime` nếu không có override.
   - `IsClosed = true` → trả về `IsClosed = true`, player không tạo được lobby.
6. **Overnight session** được hỗ trợ: `OpenTime > CloseTime` (VD 10:00 → 02:00) → phiên kết thúc lúc 02:00 ngày hôm sau. `preferredEndTime` validate với schedule ngày kế tiếp.

---

## Endpoints

| Endpoint | Method | Auth | Mô tả |
|---|---|---|---|
| `/` | GET | Cafe Manager / Staff | Lấy schedule tổng hợp của cafe (default + toàn bộ overrides) |
| `/{applyDate}` | GET | Cafe Manager / Staff | Lấy override cho 1 ngày. Trả `null` nếu dùng default |
| `/` | POST | Cafe Manager / Staff | Tạo / cập nhật override cho 1 ngày (upsert) |
| `/bulk` | POST | Cafe Manager / Staff | Bulk upsert nhiều override trong 1 transaction |
| `/{applyDate}` | DELETE | Cafe Manager / Staff | Xóa override cho ngày (idempotent — cafe quay về default) |

**Header chung:** `Authorization: Bearer <manager-or-staff-token>`

---

## GET /api/v1/cafes/{cafeId}/schedule-overrides

Trả về schedule tổng hợp của cafe: default schedule + danh sách overrides đã cấu hình.

**Authz:** Service tự gọi `ICafeRepository.IsManagerOrStaffAsync(cafeId, userId)` — chỉ manager/staff của cafe đó mới xem được.

### Response 200

```json
{
  "statusCode": 200,
  "message": "Lấy lịch cafe thành công.",
  "data": {
    "cafeId": "881348c9-ac61-4715-a437-7aac95aa4cca",
    "defaultOpenTime": "06:00:00",
    "defaultCloseTime": "23:00:00",
    "days": [
      {
        "id": "8c8a4f3e-9b1d-4f2e-bc71-1d5b9a8e7c90",
        "cafeId": "881348c9-ac61-4715-a437-7aac95aa4cca",
        "applyDate": "2026-10-03",
        "openTime": "00:00:00",
        "closeTime": "23:59:59",
        "isClosed": false,
        "hasOverride": true,
        "createdAt": "2026-10-03T10:15:00Z",
        "updatedAt": "2026-10-03T10:15:00Z"
      }
    ]
  }
}
```

> **Lưu ý:** `Days` chỉ chứa ngày **đã có override**. Ngày không có → dùng `defaultOpenTime`/`defaultCloseTime`.

### Response codes

- `200` — Lấy schedule thành công.
- `401` — Thiếu / sai token.
- `403` — User không phải manager/staff của cafe này.
- `404` — Không tìm thấy cafe với `cafeId` cho trước.

---

## GET /api/v1/cafes/{cafeId}/schedule-overrides/{applyDate}

Lấy override cho **một ngày cụ thể**. Trả về `null` nếu ngày đó chưa có override (sẽ dùng default).

**Path param:**

| Name | Type | Format | Mô tả |
|---|---|---|---|
| `cafeId` | Guid (route) | UUID | Mã cafe |
| `applyDate` | DateOnly (route) | `YYYY-MM-DD` | Ngày cần lấy override |

**Ví dụ URL:** `GET /api/v1/cafes/881348c9-ac61-4715-a437-7aac95aa4cca/schedule-overrides/2026-10-05`

### Response 200 — có override

```json
{
  "statusCode": 200,
  "message": "Lấy override thành công.",
  "data": {
    "id": "8c8a4f3e-9b1d-4f2e-bc71-1d5b9a8e7c90",
    "cafeId": "881348c9-ac61-4715-a437-7aac95aa4cca",
    "applyDate": "2026-10-05",
    "openTime": "00:00:00",
    "closeTime": "23:59:59",
    "isClosed": false,
    "hasOverride": true,
    "createdAt": "2026-10-03T10:15:00Z",
    "updatedAt": "2026-10-03T10:15:00Z"
  }
}
```

### Response 200 — không có override (dùng default)

```json
{
  "statusCode": 200,
  "message": "Lấy override thành công.",
  "data": null
}
```

### Response codes

- `200` — Lấy thành công (kể cả khi `data` là `null`).
- `401` — Thiếu / sai token.
- `403` — User không phải manager/staff của cafe này.
- `404` — Không tìm thấy cafe.

> ⚠️ **Khác biệt với GET `/`:** Endpoint này **không thực hiện authz check** (chỉ verify cafe tồn tại). Dùng để debug/admin view nhanh 1 ngày.

---

## POST /api/v1/cafes/{cafeId}/schedule-overrides

Tạo mới hoặc cập nhật override cho **một ngày**. Mỗi `(cafeId, applyDate)` chỉ có tối đa 1 override (upsert theo DB unique constraint).

### Request body

```json
{
  "applyDate": "2026-10-05",
  "openTime": "00:00:00",
  "closeTime": "23:59:59",
  "isClosed": false
}
```

| Field | Type | Required | Mô tả |
|---|---|---|---|
| `applyDate` | `DateOnly` | Yes | Ngày cần override. Phải **>= hôm nay** (UTC) |
| `openTime` | `TimeOnly?` | No | Giờ mở cửa. `null` = giữ default `06:00` |
| `closeTime` | `TimeOnly?` | No | Giờ đóng cửa. `null` = giữ default `23:00` |
| `isClosed` | `bool` | No (default `false`) | `true` = đóng cửa ngày này. Khi `true`, `openTime`/`closeTime` có thể `null` |

### Validation rules

| Điều kiện | Mã lỗi | Message |
|---|---|---|
| `applyDate < today` (UTC) | `400` | "Ngày áp dụng không được ở quá khứ." |
| `!isClosed` và `openTime == closeTime` (cùng giờ mở/đóng) | `400` | "Giờ mở cửa và giờ đóng cửa không được bằng nhau." |
| `!isClosed` và cả `openTime` + `closeTime` đều `null` | OK | Hệ thống dùng default (06:00 / 23:00) |
| `isClosed = true` | OK | `openTime` / `closeTime` có thể `null` |

### Overnight

Hỗ trợ overnight session: `openTime > closeTime` (VD 10:00 → 02:00). `closeTime` được hiểu là giờ đóng của **ngày hôm sau**. Logic nghiệp vụ (`CafeScheduleValidator.ValidatePreferredTimeRangeAsync`) sẽ validate `preferredEndTime` với schedule ngày kế tiếp khi áp dụng cho reservation.

### Response 200

```json
{
  "statusCode": 200,
  "message": "Cập nhật override lịch cafe thành công.",
  "data": {
    "id": "8c8a4f3e-9b1d-4f2e-bc71-1d5b9a8e7c90",
    "cafeId": "881348c9-ac61-4715-a437-7aac95aa4cca",
    "applyDate": "2026-10-05",
    "openTime": "00:00:00",
    "closeTime": "23:59:59",
    "isClosed": false,
    "hasOverride": true,
    "createdAt": "2026-10-03T10:15:00Z",
    "updatedAt": "2026-10-03T10:15:00Z"
  }
}
```

### Response codes

- `200` — Tạo / cập nhật thành công.
- `400` — Validation fail (xem bảng trên).
- `401` — Thiếu / sai token.
- `403` — User không phải manager/staff của cafe này.
- `404` — Không tìm thấy cafe.

---

## POST /api/v1/cafes/{cafeId}/schedule-overrides/bulk

Bulk upsert nhiều override trong **1 transaction**. Dùng khi cafe muốn set lịch cho cả tuần / cả tháng (VD set 30 ngày 24/7 cho mục đích demo/test).

### Request body

```json
[
  { "applyDate": "2026-10-03", "openTime": "00:00:00", "closeTime": "23:59:59", "isClosed": false },
  { "applyDate": "2026-10-04", "openTime": "00:00:00", "closeTime": "23:59:59", "isClosed": false },
  { "applyDate": "2026-10-05", "openTime": "00:00:00", "closeTime": "23:59:59", "isClosed": false }
]
```

| Field | Type | Required | Mô tả |
|---|---|---|---|
| (array of) `applyDate` | `DateOnly` | Yes | Ngày override |
| (array of) `openTime` | `TimeOnly?` | No | Giờ mở (null = default) |
| (array of) `closeTime` | `TimeOnly?` | No | Giờ đóng (null = default) |
| (array of) `isClosed` | `bool` | No | `true` = đóng cửa |

### Validation rules (áp dụng cho TỪNG phần tử)

Giống hệt POST `/` — nếu **bất kỳ** phần tử nào fail validate, **toàn bộ transaction rollback** (không persist phần tử nào).

| Điều kiện | Mã lỗi |
|---|---|
| Array rỗng | Trả về `200` với data `[]` (không lỗi) |
| `applyDate < today` | `400` rollback toàn bộ |
| `!isClosed` và `openTime == closeTime` | `400` rollback toàn bộ |

### Response 200

```json
{
  "statusCode": 200,
  "message": "Bulk upsert thành công 3 ngày.",
  "data": [
    {
      "id": "8c8a4f3e-9b1d-4f2e-bc71-1d5b9a8e7c90",
      "cafeId": "881348c9-ac61-4715-a437-7aac95aa4cca",
      "applyDate": "2026-10-03",
      "openTime": "00:00:00",
      "closeTime": "23:59:59",
      "isClosed": false,
      "hasOverride": true,
      "createdAt": "2026-10-03T10:15:00Z",
      "updatedAt": "2026-10-03T10:15:00Z"
    }
  ]
}
```

### Response codes

- `200` — Bulk upsert thành công.
- `400` — Validation fail (1 phần tử sai → rollback toàn bộ).
- `401` — Thiếu / sai token.
- `403` — User không phải manager/staff của cafe này.
- `404` — Không tìm thấy cafe.

---

## DELETE /api/v1/cafes/{cafeId}/schedule-overrides/{applyDate}

Xóa override cho **một ngày**. Sau khi xóa, ngày đó sẽ dùng `DefaultOpenTime` / `DefaultCloseTime`.

**Idempotent:** Nếu ngày đó chưa có override, response vẫn trả `200` (không lỗi).

**Path param:**

| Name | Type | Format | Mô tả |
|---|---|---|---|
| `cafeId` | Guid (route) | UUID | Mã cafe |
| `applyDate` | DateOnly (route) | `YYYY-MM-DD` | Ngày cần xóa override |

**Ví dụ URL:** `DELETE /api/v1/cafes/881348c9-ac61-4715-a437-7aac95aa4cca/schedule-overrides/2026-10-05`

### Response 200

```json
{
  "statusCode": 200,
  "message": "Xóa override, cafe quay về dùng lịch mặc định thành công.",
  "data": null
}
```

### Response codes

- `200` — Xóa thành công (kể cả khi không có override sẵn — idempotent).
- `401` — Thiếu / sai token.
- `403` — User không phải manager/staff của cafe này.
- `404` — Không tìm thấy cafe.

---

## Ảnh hưởng tới các API khác

| API bị ảnh hưởng | Hành vi |
|---|---|
| `POST /api/v1/reservations/quote` | Nếu ngày bị đóng (`isClosed = true`) → trả `400` `ApiErrorMessages.Reservation.CafeScheduleClosedForPlayDate`. Validate `preferredStartTime >= OpenTime` + `preferredEndTime <= CloseTime` qua `CafeScheduleValidator.ValidatePreferredTimeRangeAsync` (xử lý overnight: end thuộc ngày kế tiếp validate với schedule ngày kế). |
| `POST /api/v1/reservations/confirm` | Tương tự — chặn tạo reservation + validate preferred times với `CafeSchedule`. |
| `POST /api/v1/reservations/walkin` | Validate `preferredStartTime` của walk-in booking với resolved schedule. |
| `POST /api/v1/lobbies` (qua reservation flow) | `ScheduledStartTime` / `ScheduledEndTime` được tính từ override (nếu có), không phải default. |
| POS check-in (`POST /api/cafes/{cafeId}/pos/check-in`) | `ValidateCheckInTimeWindow` sử dụng resolved schedule — chặn check-in ngoài giờ mở cửa của cafe (override nếu có). |
| `GET /api/v1/lobbies/search` | Lobby của cafe đóng cửa ngày `playDate` → bị filter ra. |

---

## Ví dụ

### Cafe 24/7 (1 ngày)

```
POST /api/v1/cafes/881348c9-ac61-4715-a437-7aac95aa4cca/schedule-overrides
{
  "applyDate": "2026-10-05",
  "openTime": "00:00:00",
  "closeTime": "23:59:59",
  "isClosed": false
}
```

> Sau khi set, player có thể book khung giờ bất kỳ trong ngày này (bao gồm 02:00 → 05:00).

### Cafe đóng cửa ngày lễ

```
POST /api/v1/cafes/881348c9-ac61-4715-a437-7aac95aa4cca/schedule-overrides
{
  "applyDate": "2026-09-02",
  "isClosed": true
}
```

Player cố tạo lobby cho ngày 2026-09-02 → API trả `400` "Quán đóng cửa vào ngày bạn chọn. Vui lòng chọn ngày khác."

### Cafe overnight (mở 18:00 → 02:00)

```
POST /api/v1/cafes/881348c9-ac61-4715-a437-7aac95aa4cca/schedule-overrides
{
  "applyDate": "2026-10-05",
  "openTime": "18:00:00",
  "closeTime": "02:00:00",
  "isClosed": false
}
```

> `closeTime = 02:00:00` = đóng cửa lúc 02:00 **ngày hôm sau** (06/10). Khi player chọn `preferredStart=22:00` + `preferredEnd=01:30`, validator validate end với schedule 06/10.

### Set 30 ngày 24/7 (demo / test)

```
POST /api/v1/cafes/881348c9-ac61-4715-a437-7aac95aa4cca/schedule-overrides/bulk
[
  { "applyDate": "2026-10-03", "openTime": "00:00:00", "closeTime": "23:59:59", "isClosed": false },
  { "applyDate": "2026-10-04", "openTime": "00:00:00", "closeTime": "23:59:59", "isClosed": false },
  ... 28 ngày tiếp theo
]
```

> Tất cả 30 override được insert/update trong 1 transaction. Nếu 1 phần tử fail validate → rollback toàn bộ.

---

## Quy tắc nghiệp vụ bổ sung

1. **Idempotent**: `POST /` luôn upsert theo `(cafeId, applyDate)` — gọi 2 lần với cùng body chỉ update `UpdatedAt`, không tạo row thứ 2.
2. **Idempotent delete**: `DELETE /{applyDate}` xóa không có row vẫn trả `200`.
3. **Audit**: Thay đổi override không ghi audit log riêng (chỉ `CreatedAt` / `UpdatedAt` trên row).
4. **Hiệu lực tức thì**: Override mới tạo áp dụng cho reservation **đang chờ**. Reservation đã tồn tại giữ schedule snapshot tại thời điểm tạo.
5. **Cache**: Backend không cache schedule — mỗi request resolve trực tiếp từ DB. Nếu cần scale, nên thêm cache layer ở phase sau.
6. **Time zone**: `OpenTime` / `CloseTime` là giờ **local của cafe** (giả định `Asia/Ho_Chi_Minh`). Khi build `ScheduledStartTime` / `ScheduledEndTime` cho reservation, hệ thống convert sang UTC theo `CafeSchedule.VietnamTz` (xem `CafeSchedule.BuildScheduledStartEndFromPreferred`).

---

## Liên kết

- **Domain rule:** [lobby-booking-deposit-bvc.mdc](../.cursor/rules/lobby-booking-deposit-bvc.mdc) §7.1 (BR-NEW-15) + §XIII (BR-NEW-12)
- **Source code:**
  - `BoardVerse.Core/Entities/CafeScheduleOverride.cs`
  - `BoardVerse.Core/DTOs/CafeSchedule/CafeScheduleOverrideDtos.cs`
  - `BoardVerse.Data/Configurations/CafeScheduleOverrideConfiguration.cs`
  - `BoardVerse.Data/Repositories/CafeScheduleOverrideRepository.cs`
  - `BoardVerse.Core/Constants/CafeSchedule.cs` — `DefaultOpenTime` / `DefaultCloseTime` / `VietnamTz` / `BuildScheduledStartEndFromPreferred`
  - `BoardVerse.Services/Services/CafeScheduleResolver.cs` — `IScheduleResolver` implementation
  - `BoardVerse.Services/Services/CafeScheduleService.cs` — CRUD + bulk + authz
  - `BoardVerse.API/Controllers/CafeScheduleController.cs` — REST surface
- **Tests:** `BoardVerse.Tests/Services/CafeScheduleTests.cs`, `CafeScheduleServiceTests.cs`, `CafeScheduleResolverTests.cs`
- **API liên quan:** [reservation.md](./reservation.md) — `POST /reservations/quote` / `/confirm` / `/walkin` validate với resolved schedule.
