# TimeSlotController (Manager)

**Base route:** `/api/v1/manager/time-slots`
**Controller:** `TimeSlotController.cs`
**Role:** Manager (chỉ chủ cafe của `cafeId` tương ứng)

API "hybrid" cho phép manager:
- **Xem** 4 `TimeSlot` cố định của hệ thống (read-only metadata).
- **CRUD** các override theo từng cafe qua `CafeScheduleOverride`.

> **Lưu ý về naming:**
> Controller này **khác** với `CafeScheduleController` (`/api/v1/cafes/{cafeId}/schedule-overrides`):
> - `CafeScheduleController` chỉ quản lý override (POST upsert, DELETE).
> - `TimeSlotController` cung cấp góc nhìn "TimeSlot" từ phía manager:
>   - `GET /defaults` — metadata 4 slot cố định (dùng cho UI form chọn slot).
>   - `GET/POST/PUT/DELETE /cafes/{cafeId}/...` — CRUD override chuẩn RESTful.

> **Liên quan:**
> - [lobby-booking-deposit-bvc.mdc](../.cursor/rules/lobby-booking-deposit-bvc.mdc) §7.1 (BR-NEW-15) — định nghĩa 4 slot cố định.
> - [cafe-schedule.md](./cafe-schedule.md) — API override song song (POST upsert, DELETE).

---

## Nguyên tắc

1. **`TimeSlot` enum là cố định** — 4 giá trị `Morning` (06-12), `Afternoon` (12-17), `Evening` (17-23), `LateNight` (23-06, qua đêm, endTime = ngày hôm sau). Manager **không được thêm slot mới**.
2. **Default schedule** dùng `CafeSchedule.GetStartTime / GetEndTime` — không cần override nếu cafe mở đúng khung giờ chuẩn.
3. **`CafeScheduleOverride`** cho phép cafe tùy chỉnh **từng slot**:
   - Set `startTime` / `endTime` riêng.
   - Đánh dấu `isClosed: true` để chặn player tạo lobby trong slot đó.
   - Có thể giới hạn theo `effectiveFrom` / `effectiveTo` (vd: tăng giờ mở cửa dịp lễ).
4. **Resolve logic** (`CafeScheduleResolver`):
   - Ưu tiên override nếu `EffectiveFrom <= playDate <= EffectiveTo`.
   - Fallback default nếu không có override.
   - `IsClosed = true` → trả về `IsClosed = true`, player không tạo được lobby.

---

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/defaults` | GET | Lấy 4 TimeSlot mặc định của hệ thống (read-only) |
| `/cafes/{cafeId}` | GET | Lấy 4 TimeSlot của cafe (merge default + override) |
| `/cafes/{cafeId}/{timeSlot}` | GET | Lấy chi tiết 1 TimeSlot của cafe |
| `/cafes/{cafeId}` | POST | Tạo override cho 1 TimeSlot (409 nếu đã tồn tại) |
| `/cafes/{cafeId}/{timeSlot}` | PUT | Cập nhật partial override (404 nếu chưa tồn tại) |
| `/cafes/{cafeId}/{timeSlot}` | DELETE | Xóa override (idempotent) |

**Header:** `Authorization: Bearer <manager-token>`

---

## GET /api/v1/manager/time-slots/defaults

Lấy metadata 4 TimeSlot mặc định (read-only). Dùng cho UI form manager chọn slot trước khi tạo override.

**Auth:** Manager (role check).

**Response 200:**

```json
{
  "statusCode": 200,
  "success": true,
  "message": "Lấy danh sách khung giờ mặc định thành công.",
  "data": [
    {
      "slot": "Morning",
      "displayName": "Sáng",
      "defaultStartTime": "06:00:00",
      "defaultEndTime": "12:00:00",
      "durationMinutes": 360,
      "description": "Phiên sáng (06:00 – 12:00)"
    },
    {
      "slot": "Afternoon",
      "displayName": "Chiều",
      "defaultStartTime": "12:00:00",
      "defaultEndTime": "17:00:00",
      "durationMinutes": 300,
      "description": "Phiên chiều (12:00 – 17:00)"
    },
    {
      "slot": "Evening",
      "displayName": "Tối",
      "defaultStartTime": "17:00:00",
      "defaultEndTime": "23:00:00",
      "durationMinutes": 360,
      "description": "Phiên tối (17:00 – 23:00)"
    },
    {
      "slot": "LateNight",
      "displayName": "Khuya",
      "defaultStartTime": "23:00:00",
      "defaultEndTime": "06:00:00",
      "durationMinutes": 420,
      "description": "Phiên khuya qua đêm (23:00 – 06:00 hôm sau)"
    }
  ]
}
```

**Response codes:**

- `200` — Trả về 4 TimeSlot.
- `401` — Thiếu / sai token.
- `403` — Không có role Manager.
- `500` — Lỗi hệ thống.

---

## GET /api/v1/manager/time-slots/cafes/{cafeId}

Lấy toàn bộ 4 TimeSlot của cafe (đã merge với override nếu có). Manager chỉ xem được cafe mình sở hữu.

**Auth:** Manager + ownership check (`cafe.ManagerId == currentUserId`).

**Path params:**

| Name | Type | Required | Mô tả |
|------|------|----------|-------|
| `cafeId` | Guid | Yes | Mã định danh cafe |

**Response 200:**

```json
{
  "statusCode": 200,
  "success": true,
  "message": "Lấy danh sách khung giờ của quán thành công.",
  "data": [
    {
      "id": "00000000-0000-0000-0000-000000000000",
      "cafeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "timeSlot": "Morning",
      "startTime": "06:00:00",
      "endTime": "12:00:00",
      "defaultStartTime": "06:00:00",
      "defaultEndTime": "12:00:00",
      "isClosed": false,
      "hasOverride": false,
      "isCustomized": false,
      "effectiveFrom": null,
      "effectiveTo": null,
      "createdAt": null,
      "updatedAt": null
    },
    {
      "id": "8c8a4f3e-9b1d-4f2e-bc71-1d5b9a8e7c90",
      "cafeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "timeSlot": "Evening",
      "startTime": "18:00:00",
      "endTime": "23:30:00",
      "defaultStartTime": "17:00:00",
      "defaultEndTime": "23:00:00",
      "isClosed": false,
      "hasOverride": true,
      "isCustomized": true,
      "effectiveFrom": "2026-08-05",
      "effectiveTo": null,
      "createdAt": "2026-08-05T13:30:00Z",
      "updatedAt": "2026-08-10T08:15:00Z"
    }
  ]
}
```

**Field meaning:**

- `id` = `Guid.Empty` nếu `hasOverride == false` (đang dùng default).
- `startTime` / `endTime` = giá trị hiệu lực (override hoặc default).
- `defaultStartTime` / `defaultEndTime` = giá trị gốc từ `CafeSchedule`.
- `isCustomized` = `hasOverride && (startTime != defaultStartTime || endTime != defaultEndTime || isClosed)`.

**Response codes:**

- `200` — Trả về danh sách 4 TimeSlot.
- `401` — Thiếu / sai token.
- `403` — Không sở hữu cafe này.
- `404` — Không tìm thấy cafe.
- `500` — Lỗi hệ thống.

---

## GET /api/v1/manager/time-slots/cafes/{cafeId}/{timeSlot}

Lấy chi tiết 1 TimeSlot của cafe (override hoặc default).

**Auth:** Manager + ownership check.

**Path params:**

| Name | Type | Required | Mô tả |
|------|------|----------|-------|
| `cafeId` | Guid | Yes | Mã định danh cafe |
| `timeSlot` | string | Yes | Enum: `morning` / `afternoon` / `evening` / `lateNight` (case-insensitive) |

**Response 200:** [`ManagerTimeSlotResponseDto`](#managertimeslotresponsedto) — `hasOverride = false` nếu cafe chưa override.

**Response codes:**

- `200` — Trả về chi tiết TimeSlot.
- `400` — `timeSlot` không hợp lệ (không thuộc 4 enum).
- `401` — Thiếu / sai token.
- `403` — Không sở hữu cafe này.
- `404` — Không tìm thấy cafe.
- `500` — Lỗi hệ thống.

---

## POST /api/v1/manager/time-slots/cafes/{cafeId}

Tạo override cho 1 TimeSlot. Nếu đã tồn tại → 409 Conflict (dùng PUT để update).

**Auth:** Manager + ownership check.

**Body:** `CreateTimeSlotOverrideRequestDto`

```json
{
  "timeSlot": "Evening",
  "startTime": "18:00:00",
  "endTime": "23:30:00",
  "isClosed": false,
  "effectiveFrom": "2026-08-05",
  "effectiveTo": null
}
```

| Field | Type | Required | Mô tả |
|-------|------|----------|-------|
| `timeSlot` | enum | Yes | `Morning` / `Afternoon` / `Evening` / `LateNight` |
| `startTime` | `TimeOnly?` | No | Null = giữ default |
| `endTime` | `TimeOnly?` | No | Null = giữ default |
| `isClosed` | bool | No, default `false` | Đóng slot, không cho player tạo lobby |
| `effectiveFrom` | `DateOnly?` | No | Bắt đầu áp dụng (inclusive). Null = vô hạn |
| `effectiveTo` | `DateOnly?` | No | Kết thúc áp dụng (inclusive). Null = vô hạn |

**Validation:**

- `timeSlot` phải thuộc 4 enum (case-insensitive).
- Khi `isClosed = false`:
  - Nếu cả `startTime` và `endTime` đều null → dùng default cho slot đó.
  - Nếu cả hai không null và **bằng nhau** → `400` (range không hợp lệ).
- Khi `isClosed = true`: `startTime` / `endTime` có thể null / bằng nhau.
- `effectiveFrom <= effectiveTo` nếu cả hai đều có giá trị.

**Response 201:**

```json
{
  "statusCode": 201,
  "success": true,
  "message": "Tạo override khung giờ cho quán thành công.",
  "data": {
    "id": "8c8a4f3e-9b1d-4f2e-bc71-1d5b9a8e7c90",
    "cafeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "timeSlot": "Evening",
    "startTime": "18:00:00",
    "endTime": "23:30:00",
    "defaultStartTime": "17:00:00",
    "defaultEndTime": "23:00:00",
    "isClosed": false,
    "hasOverride": true,
    "isCustomized": true,
    "effectiveFrom": "2026-08-05",
    "effectiveTo": null,
    "createdAt": "2026-08-05T13:30:00Z",
    "updatedAt": "2026-08-05T13:30:00Z"
  }
}
```

**Response codes:**

- `201` — Tạo override thành công.
- `400` — `timeSlot` không hợp lệ, `startTime == endTime` (khi `!isClosed`), `effectiveFrom > effectiveTo`.
- `401` — Thiếu / sai token.
- `403` — Không sở hữu cafe này.
- `404` — Không tìm thấy cafe.
- `409` — Cafe đã có override cho slot này (dùng PUT để cập nhật).
- `500` — Lỗi hệ thống.

---

## PUT /api/v1/manager/time-slots/cafes/{cafeId}/{timeSlot}

Cập nhật partial override. Field null = giữ nguyên giá trị hiện tại. Nếu chưa có override → 404 (dùng POST để tạo).

**Auth:** Manager + ownership check.

**Path params:**

| Name | Type | Required | Mô tả |
|------|------|----------|-------|
| `cafeId` | Guid | Yes | Mã định danh cafe |
| `timeSlot` | string | Yes | Enum TimeSlot |

**Body:** `UpdateTimeSlotOverrideRequestDto`

```json
{
  "startTime": null,
  "endTime": "00:00:00",
  "isClosed": false,
  "effectiveFrom": null,
  "effectiveTo": "2026-12-31"
}
```

| Field | Type | Required | Mô tả |
|-------|------|----------|-------|
| `startTime` | `TimeOnly?` | No | Null = giữ nguyên |
| `endTime` | `TimeOnly?` | No | Null = giữ nguyên |
| `isClosed` | `bool?` | No | Null = giữ nguyên |
| `effectiveFrom` | `DateOnly?` | No | Null = giữ nguyên |
| `effectiveTo` | `DateOnly?` | No | Null = giữ nguyên |

**Validation:**

- Phải gửi **ít nhất 1 field** không null → `400 NoFieldsToUpdate` nếu tất cả null.
- Giá trị mới (sau khi merge partial) phải pass validation giống POST.

**Response 200:** [`ManagerTimeSlotResponseDto`](#managertimeslotresponsedto) đã cập nhật.

**Response codes:**

- `200` — Cập nhật thành công.
- `400` — `timeSlot` không hợp lệ, không có field nào để update, hoặc giá trị mới không hợp lệ.
- `401` — Thiếu / sai token.
- `403` — Không sở hữu cafe này.
- `404` — Cafe chưa có override cho slot này (dùng POST để tạo).
- `500` — Lỗi hệ thống.

---

## DELETE /api/v1/manager/time-slots/cafes/{cafeId}/{timeSlot}

Xóa override cho 1 TimeSlot → cafe quay về dùng default. Idempotent (gọi 2 lần vẫn OK).

**Auth:** Manager + ownership check.

**Path params:**

| Name | Type | Required | Mô tả |
|------|------|----------|-------|
| `cafeId` | Guid | Yes | Mã định danh cafe |
| `timeSlot` | string | Yes | Enum TimeSlot |

**Response 204:** No content (delete thành công, kể cả khi chưa có override sẵn).

**Response codes:**

- `204` — Xóa thành công (idempotent).
- `400` — `timeSlot` không hợp lệ.
- `401` — Thiếu / sai token.
- `403` — Không sở hữu cafe này.
- `404` — Không tìm thấy cafe.
- `500` — Lỗi hệ thống.

---

## DTOs

### DefaultTimeSlotDto

```json
{
  "slot": "Morning",
  "displayName": "Sáng",
  "defaultStartTime": "06:00:00",
  "defaultEndTime": "12:00:00",
  "durationMinutes": 360,
  "description": "Phiên sáng (06:00 – 12:00)"
}
```

| Field | Type | Mô tả |
|-------|------|-------|
| `slot` | string | Enum name: `Morning` / `Afternoon` / `Evening` / `LateNight` |
| `displayName` | string | Tên tiếng Việt cho UI |
| `defaultStartTime` | `TimeOnly` | Giờ bắt đầu mặc định |
| `defaultEndTime` | `TimeOnly` | Giờ kết thúc mặc định |
| `durationMinutes` | int | Duration (LateNight = 420 phút overnight) |
| `description` | string | Mô tả ngắn cho UI manager |

### ManagerTimeSlotResponseDto

```json
{
  "id": "8c8a4f3e-9b1d-4f2e-bc71-1d5b9a8e7c90",
  "cafeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "timeSlot": "Evening",
  "startTime": "18:00:00",
  "endTime": "23:30:00",
  "defaultStartTime": "17:00:00",
  "defaultEndTime": "23:00:00",
  "isClosed": false,
  "hasOverride": true,
  "isCustomized": true,
  "effectiveFrom": "2026-08-05",
  "effectiveTo": null,
  "createdAt": "2026-08-05T13:30:00Z",
  "updatedAt": "2026-08-10T08:15:00Z"
}
```

| Field | Type | Mô tả |
|-------|------|-------|
| `id` | Guid | `Guid.Empty` nếu chưa có override |
| `cafeId` | Guid | Mã cafe |
| `timeSlot` | string | Enum name |
| `startTime` | `TimeOnly` | Giờ bắt đầu hiệu lực (override hoặc default) |
| `endTime` | `TimeOnly` | Giờ kết thúc hiệu lực (override hoặc default) |
| `defaultStartTime` | `TimeOnly` | Giờ bắt đầu gốc (từ `CafeSchedule`) |
| `defaultEndTime` | `TimeOnly` | Giờ kết thúc gốc |
| `isClosed` | bool | `true` nếu cafe đóng slot này |
| `hasOverride` | bool | `true` nếu có override row trong DB |
| `isCustomized` | bool | `true` khi override có giá trị khác default |
| `effectiveFrom` | `DateOnly?` | Bắt đầu áp dụng (null = vô hạn) |
| `effectiveTo` | `DateOnly?` | Kết thúc áp dụng (null = vô hạn) |
| `createdAt` | DateTime | Thời điểm tạo override (DateTime.MinValue nếu chưa có) |
| `updatedAt` | DateTime | Lần cập nhật cuối (DateTime.MinValue nếu chưa có) |

---

## Ảnh hưởng tới các API khác

| API bị ảnh hưởng | Hành vi |
|------------------|---------|
| `POST /api/v1/reservations/quote` | Nếu slot bị đóng (`isClosed = true` và `EffectiveFrom <= playDate <= EffectiveTo`) → trả `400` "Quán đã đóng khung giờ này cho ngày đã chọn". Ngoài ra, validate `preferredStartTime >= OpenTime` + `preferredEndTime <= CloseTime` qua `CafeScheduleValidator` (xử lý overnight: end thuộc ngày kế tiếp validate với schedule ngày kế). (G3 fix 2026-09-01.) |
| `POST /api/v1/reservations/confirm` | Tương tự — chặn tạo reservation + validate preferred times với `CafeSchedule`. |
| `POST /api/v1/lobbies` (qua reservation flow) | `scheduledTime` và `recruitmentDeadline` được tính từ override (nếu có), không phải default. |
| POS check-in | `ValidateCheckInTimeWindow` sử dụng resolved schedule — chặn check-in ngoài giờ mở cửa của cafe (override nếu có). Có thêm `TimeWindowGuard` (per-request bypass) và `DemoGuard` (demo mode bypass). |

---

## Ví dụ

### Cafe 24/24 (mở cả ngày, không cần override)

Không cần gọi API. Default 4 slot đã cover 00:00 → 24:00 liên tục.

### Cafe mở từ 10:00 → 02:00 sáng hôm sau

```
Morning (06-12) → đóng → POST { timeSlot: "Morning", isClosed: true }
Afternoon (12-17) → giữ default
Evening (17-23) → custom 18-02 → POST { timeSlot: "Evening", startTime: "18:00", endTime: "02:00" }
LateNight (23-06) → custom 00-02 → POST { timeSlot: "LateNight", startTime: "00:00", endTime: "02:00" }
```

> **Lưu ý:** `endTime = 02:00` cho slot Evening có nghĩa là đóng cửa lúc 02:00 **ngày hôm sau**. Logic nghiệp vụ (`ReservationService`, `LobbyService`) sẽ tự hiểu là overnight.

### Cafe chỉ mở ban ngày (đóng LateNight)

```
POST { timeSlot: "LateNight", isClosed: true }
```

Player cố tạo lobby với `timeSlot = "LateNight"` → API trả `400`.

### Update partial: chỉ đổi effectiveTo

```http
PUT /api/v1/manager/time-slots/cafes/{cafeId}/Evening
{
  "effectiveTo": "2026-12-31"
}
```

`startTime` / `endTime` / `isClosed` / `effectiveFrom` giữ nguyên giá trị hiện tại.

### Reset về default

```http
DELETE /api/v1/manager/time-slots/cafes/{cafeId}/Evening
```

Cafe quay về dùng `CafeSchedule.GetStartTime(Evening)` / `GetEndTime(Evening)` (17:00 - 23:00).

---

## Quy tắc nghiệp vụ bổ sung

1. **CRUD chuẩn RESTful**:
   - `POST` = create (fail nếu đã tồn tại)
   - `PUT` = update (fail nếu chưa tồn tại)
   - `DELETE` = idempotent (success kể cả khi chưa có)
   - Đây là pattern **khác** với `CafeScheduleController` (POST upsert). Manager UI nên dùng controller này.
2. **Audit**: Thay đổi override không ghi audit log riêng (chỉ `CreatedAt` / `UpdatedAt` trên row).
3. **Hiệu lực tức thì**: Override mới tạo áp dụng cho cả lobby/reservation **chưa khởi tạo**. Lobby / reservation **đã tồn tại** giữ schedule snapshot tại thời điểm tạo.
4. **Cache**: Backend không cache schedule — mỗi request resolve trực tiếp từ DB. Nếu cần scale, nên thêm cache layer ở phase sau.
5. **Resolve logic** dùng chung `CafeScheduleResolver` — không có 2 implementation song song.

---

## Liên kết

- **Domain rule:** [lobby-booking-deposit-bvc.mdc](../.cursor/rules/lobby-booking-deposit-bvc.mdc) §7.1 + §XIII
- **API song song:** [cafe-schedule.md](./cafe-schedule.md) — `CafeScheduleController` (POST upsert, DELETE-by-slot, không read by cafeId).
- **Source code:**
  - `BoardVerse.Core/Entities/CafeScheduleOverride.cs`
  - `BoardVerse.Core/DTOs/TimeSlotOverride/TimeSlotDtos.cs`
  - `BoardVerse.Core/IRepositories/ICafeScheduleOverrideRepository.cs`
  - `BoardVerse.Services/IServices/ITimeSlotService.cs`
  - `BoardVerse.Services/Services/TimeSlotService.cs`
  - `BoardVerse.API/Controllers/TimeSlotController.cs`
- **Tests:** `BoardVerse.Tests/Services/TimeSlotServiceTests.cs` (47 tests), `CafeScheduleTests.cs`, `CafeScheduleResolverTests.cs`
