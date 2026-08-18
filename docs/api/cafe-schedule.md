# CafeScheduleController

**Base route:** `/api/v1/cafes/{cafeId:guid}/schedule-overrides`
**Controller:** `CafeScheduleController.cs`
**Role:** Cafe Manager (chỉ chủ quán của cafe tương ứng)

API cho phép cafe manager tùy chỉnh **khung giờ mở cửa** cho 4 `TimeSlot` cố định (Morning / Afternoon / Evening / LateNight), hoặc **đóng** hẳn một khung giờ. Dùng để hỗ trợ cafe mở khuya, cafe 24/24, hoặc cafe chỉ hoạt động ban ngày.

> **Liên quan:**
> - [lobby-booking-deposit-bvc.mdc](../.cursor/rules/lobby-booking-deposit-bvc.mdc) §7.1 (BR-NEW-15) — định nghĩa 4 slot cố định.
> - [booking.md](./booking.md) — luồng reservation sử dụng schedule đã resolve.

---

## Nguyên tắc

1. **`ApplyDate`** là khóa chính — mỗi ngày chỉ có 1 override. Không còn `TimeSlot` enum.
2. **Default schedule** dùng `CafeSchedule.GetOpenTime / GetCloseTime` (theo weekday/weekend) — không cần override nếu cafe mở đúng giờ chuẩn.
3. **`CafeScheduleOverride`** cho phép cafe tùy chỉnh cho từng ngày:
   - Set `OpenTime` / `CloseTime` riêng cho ngày đó.
   - Đánh dấu `IsClosed: true` để chặn player tạo lobby vào ngày đó.
   - `ApplyDate` xác định ngày áp dụng (duy nhất per `(CafeId, ApplyDate)`).
4. **Resolve logic** (`CafeScheduleResolver`):
   - Ưu tiên override nếu tồn tại cho `ApplyDate`.
   - Fallback default weekday/weekend schedule nếu không có override.
   - `IsClosed = true` → trả về `IsClosed = true`, player không tạo được lobby.

---

## Endpoints

| Endpoint | Method | Mô tả |
|----------|--------|--------|
| `/` | GET | Lấy lịch hiện tại của cafe (default schedule + overrides) |
| `/` | POST | Tạo / cập nhật override cho 1 ngày |
| `/{applyDate}` | DELETE | Xóa override cho ngày, cafe quay về dùng default |

**Header:** `Authorization: Bearer <manager-token>`

---

## GET /api/v1/cafes/{cafeId}/schedule-overrides

Trả về lịch tổng hợp của cafe: schedule mặc định (weekday/weekend) + overrides đã cấu hình.

**Response 200:**

```json
{
  "cafeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "weekdaySchedule": {
    "openTime": "08:00:00",
    "closeTime": "22:00:00"
  },
  "weekendSchedule": {
    "openTime": "09:00:00",
    "closeTime": "23:00:00"
  },
  "overrides": [
    {
      "applyDate": "2026-08-15",
      "openTime": "10:00:00",
      "closeTime": "00:00:00",
      "isClosed": false
    },
    {
      "applyDate": "2026-08-20",
      "openTime": null,
      "closeTime": null,
      "isClosed": true
    }
  ]
}
```

**Response codes:**

- `200` — Lấy lịch thành công
- `401` — Thiếu / sai token
- `403` — Không phải chủ cafe
- `404` — Không tìm thấy cafe

---

## POST /api/v1/cafes/{cafeId}/schedule-overrides

Tạo mới hoặc cập nhật override cho **một ngày**. Mỗi `(cafeId, applyDate)` chỉ có tối đa 1 override (upsert).

**Body:**

```json
{
  "applyDate": "2026-08-15",
  "openTime": "09:00:00",
  "closeTime": "23:00:00",
  "isClosed": false
}
```

| Field | Type | Required | Mô tả |
|-------|------|----------|-------|
| `applyDate` | date | Yes | Ngày cần override |
| `openTime` | `TimeOnly?` | No | Giờ mở cửa (null = giữ default của weekday/weekend) |
| `closeTime` | `TimeOnly?` | No | Giờ đóng cửa (null = giữ default) |
| `isClosed` | bool | No, default `false` | Đánh dấu ngày đóng cửa |

**Validation:**

- Khi `isClosed = false`:
  - Nếu cả `openTime` và `closeTime` đều null → dùng default cho ngày đó.
  - Nếu một trong hai null → giữ default của field đó.
  - Nếu cả hai không null và **bằng nhau** → lỗi `400` (range không hợp lệ).
- Khi `isClosed = true`: `openTime` / `closeTime` có thể null (chỉ dùng để đánh dấu đóng cửa).

**Response 200:**

```json
{
  "id": "8c8a4f3e-9b1d-4f2e-bc71-1d5b9a8e7c90",
  "cafeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "applyDate": "2026-08-15",
  "openTime": "09:00:00",
  "closeTime": "23:00:00",
  "isClosed": false,
  "createdAt": "2026-08-05T13:30:00Z",
  "updatedAt": "2026-08-05T13:30:00Z"
}
```

**Response codes:**

- `200` — Tạo / cập nhật thành công
- `400` — `applyDate` không hợp lệ, `openTime == closeTime` (khi `!isClosed`), format ngày sai
- `401` — Thiếu / sai token
- `403` — Không phải chủ cafe
- `404` — Không tìm thấy cafe

---

## DELETE /api/v1/cafes/{cafeId}/schedule-overrides/{applyDate}

Xóa override cho ngày, cafe quay về dùng default schedule.

**Path param:** `applyDate` ∈ `YYYY-MM-DD` (URL: `2026-08-15`).

**Response 200:**

```json
{
  "success": true,
  "message": "Xóa override, cafe quay về dùng lịch mặc định thành công."
}
```

**Response codes:**

- `200` — Xóa thành công (kể cả khi không có override sẵn — idempotent)
- `401` — Thiếu / sai token
- `403` — Không phải chủ cafe
- `404` — Không tìm thấy cafe

---

## Ảnh hưởng tới các API khác

| API bị ảnh hưởng | Hành vi |
|------------------|---------|
| `POST /api/v1/reservations/quote` | Nếu ngày bị đóng (`isClosed = true`) → trả `400` "Quán đã đóng cửa ngày đã chọn." |
| `POST /api/v1/reservations/confirm` | Tương tự — chặn tạo reservation. |
| `POST /api/v1/lobbies` (qua reservation flow) | `scheduledStartTime`/`scheduledEndTime` được tính từ override (nếu có), không phải default. |
| POS check-in | `ValidateCheckInTimeWindow` sử dụng resolved schedule — chặn check-in ngoài giờ mở cửa của cafe (override nếu có). |

---

## Ví dụ

### Cafe mở từ 10:00 → 02:00 sáng hôm sau (1 ngày)

```
POST /api/v1/cafes/{cafeId}/schedule-overrides
{
  "applyDate": "2026-08-20",
  "openTime": "10:00:00",
  "closeTime": "02:00:00",
  "isClosed": false
}
```

> **Lưu ý:** `closeTime = 02:00:00` có nghĩa là đóng cửa lúc 02:00 **ngày hôm sau**. Logic nghiệp vụ tự hiểu là overnight.

### Cafe đóng cửa ngày lễ

```
POST /api/v1/cafes/{cafeId}/schedule-overrides
{
  "applyDate": "2026-09-02",
  "isClosed": true
}
```

Player cố tạo lobby cho ngày 2026-09-02 → API trả `400`.

---

## Quy tắc nghiệp vụ bổ sung

1. **Idempotent**: `POST` luôn upsert theo `(cafeId, applyDate)` — gọi 2 lần với cùng body chỉ update `UpdatedAt`, không tạo row thứ 2.
2. **Audit**: Thay đổi override không ghi audit log riêng (chỉ `CreatedAt` / `UpdatedAt` trên row).
3. **Hiệu lực tức thì**: Override mới tạo áp dụng cho reservation **đang chờ**. Reservation đã tồn tại giữ schedule snapshot tại thời điểm tạo.
4. **Cache**: Backend không cache schedule — mỗi request resolve trực tiếp từ DB. Nếu cần scale, nên thêm cache layer ở phase sau.

---

## Liên kết

- **Domain rule:** [lobby-booking-deposit-bvc.mdc](../.cursor/rules/lobby-booking-deposit-bvc.mdc) §7.1 + §XIII (BR-NEW-15)
- **Source code:**
  - `BoardVerse.Core/Entities/CafeScheduleOverride.cs`
  - `BoardVerse.Data/Configurations/CafeScheduleOverrideConfiguration.cs`
  - `BoardVerse.Core/Constants/IScheduleResolver.cs`
  - `BoardVerse.Services/Services/CafeScheduleResolver.cs`
  - `BoardVerse.API/Controllers/CafeScheduleController.cs`
- **Tests:** `BoardVerse.Tests/Services/CafeScheduleTests.cs`, `CafeScheduleResolverTests.cs`
