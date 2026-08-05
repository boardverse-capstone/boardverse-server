# CafeScheduleController

**Base route:** `/api/v1/cafes/{cafeId:guid}/schedule-overrides`
**Controller:** `CafeScheduleController.cs`
**Role:** Cafe Manager (chỉ chủ quán của cafe tương ứng)

API cho phép cafe manager tùy chỉnh **khung giờ mở cửa** cho 4 `TimeSlot` cố định (Morning / Afternoon / Evening / Night), hoặc **đóng** hẳn một khung giờ. Dùng để hỗ trợ cafe mở khuya, cafe 24/24, hoặc cafe chỉ hoạt động ban ngày.

> **Liên quan:**
> - [lobby-booking-deposit-bvc.mdc](../.cursor/rules/lobby-booking-deposit-bvc.mdc) §7.1 (BR-NEW-15) — định nghĩa 4 slot cố định.
> - [booking.md](./booking.md) — luồng reservation sử dụng schedule đã resolve.

---

## Nguyên tắc

1. **`TimeSlot` enum là cố định** — 4 giá trị `Morning` (08-13), `Afternoon` (13-18), `Evening` (18-24), `Night` (00-08, qua đêm). JSON serialize vẫn giữ `"morning" / "afternoon" / "evening" / "night"`, không đổi API contract.
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
| `/` | GET | Lấy lịch hiện tại của cafe (4 slot + overrides) |
| `/` | POST | Tạo / cập nhật override cho 1 slot |
| `/{timeSlot}` | DELETE | Xóa override, cafe quay về dùng default |

**Header:** `Authorization: Bearer <manager-token>`

---

## GET /api/v1/cafes/{cafeId}/schedule-overrides

Trả về lịch tổng hợp của cafe: 4 slot, mỗi slot có `startTime` / `endTime` / `isClosed` / `hasOverride`.

**Response 200:**

```json
{
  "cafeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "slots": [
    {
      "timeSlot": "morning",
      "startTime": "08:00:00",
      "endTime": "13:00:00",
      "isClosed": false,
      "hasOverride": false
    },
    {
      "timeSlot": "afternoon",
      "startTime": "13:00:00",
      "endTime": "18:00:00",
      "isClosed": false,
      "hasOverride": false
    },
    {
      "timeSlot": "evening",
      "startTime": "18:00:00",
      "endTime": "00:00:00",
      "isClosed": false,
      "hasOverride": true
    },
    {
      "timeSlot": "night",
      "startTime": "22:00:00",
      "endTime": "06:00:00",
      "isClosed": false,
      "hasOverride": true
    }
  ]
}
```

> **Lưu ý:** `endTime = "00:00:00"` cho `evening` có nghĩa là 24:00 (cuối ngày).

**Response codes:**

- `200` — Lấy lịch thành công
- `401` — Thiếu / sai token
- `403` — Không phải chủ cafe
- `404` — Không tìm thấy cafe

---

## POST /api/v1/cafes/{cafeId}/schedule-overrides

Tạo mới hoặc cập nhật override cho **một** `TimeSlot`. Mỗi `(cafeId, timeSlot)` chỉ có tối đa 1 override (upsert).

**Body:**

```json
{
  "timeSlot": "evening",
  "startTime": "17:00:00",
  "endTime": "00:00:00",
  "isClosed": false,
  "effectiveFrom": "2026-08-05",
  "effectiveTo": null
}
```

| Field | Type | Required | Mô tả |
|-------|------|----------|-------|
| `timeSlot` | enum | Yes | `Morning` / `Afternoon` / `Evening` / `Night` |
| `startTime` | `TimeOnly?` | No | Null = giữ default |
| `endTime` | `TimeOnly?` | No | Null = giữ default |
| `isClosed` | bool | No, default `false` | Đóng slot, không cho player tạo lobby |
| `effectiveFrom` | `DateOnly?` | No | Bắt đầu áp dụng |
| `effectiveTo` | `DateOnly?` | No | Kết thúc áp dụng (null = vô hạn) |

**Validation:**

- Khi `isClosed = false`:
  - Nếu cả `startTime` và `endTime` đều null → dùng default cho slot đó.
  - Nếu một trong hai null → giữ default của field đó.
  - Nếu cả hai không null và **bằng nhau** → lỗi `400` (range không hợp lệ).
- Khi `isClosed = true`: `startTime` / `endTime` có thể null (chỉ dùng để đánh dấu đóng).

**Response 200:**

```json
{
  "id": "8c8a4f3e-9b1d-4f2e-bc71-1d5b9a8e7c90",
  "cafeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "timeSlot": "evening",
  "startTime": "17:00:00",
  "endTime": "00:00:00",
  "isClosed": false,
  "effectiveFrom": "2026-08-05",
  "effectiveTo": null,
  "createdAt": "2026-08-05T13:30:00Z",
  "updatedAt": "2026-08-05T13:30:00Z"
}
```

**Response codes:**

- `200` — Tạo / cập nhật thành công
- `400` — `timeSlot` không hợp lệ, `startTime == endTime` (khi `!isClosed`), format ngày sai
- `401` — Thiếu / sai token
- `403` — Không phải chủ cafe
- `404` — Không tìm thấy cafe

---

## DELETE /api/v1/cafes/{cafeId}/schedule-overrides/{timeSlot}

Xóa override cho slot, cafe quay về dùng default schedule.

**Path param:** `timeSlot` ∈ `Morning` / `Afternoon` / `Evening` / `Night` (URL: `morning`, `afternoon`, `evening`, `night`).

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
| `POST /api/v1/reservations/quote` | Nếu slot bị đóng (`isClosed = true` và `EffectiveFrom <= playDate <= EffectiveTo`) → trả `400` "Quán đã đóng khung giờ này cho ngày đã chọn". |
| `POST /api/v1/reservations/confirm` | Tương tự — chặn tạo reservation. |
| `POST /api/v1/lobbies` (qua reservation flow) | `scheduledTime` và `recruitmentDeadline` được tính từ override (nếu có), không phải default. |
| POS check-in | `ValidateCheckInTimeWindow` sử dụng resolved schedule — chặn check-in ngoài giờ mở cửa của cafe (override nếu có). |

---

## Ví dụ

### Cafe 24/24 (mở cả ngày, không cần override)

Không cần gọi API. Default 4 slot đã cover 00:00 → 24:00 liên tục.

### Cafe mở từ 10:00 → 02:00 sáng hôm sau

```
Morning (08-13) → đóng → POST { isClosed: true }
Afternoon (13-18) → giữ default
Evening (18-24) → custom 18-02 → POST { startTime: "18:00", endTime: "02:00" }
Night (00-08) → custom 00-02 → POST { startTime: "00:00", endTime: "02:00" }
```

> **Lưu ý:** `endTime = 02:00` cho slot Evening có nghĩa là đóng cửa lúc 02:00 **ngày hôm sau**. Logic nghiệp vụ (`ReservationService`, `LobbyService`) sẽ tự hiểu là overnight.

### Cafe chỉ mở ban ngày (đóng Night)

```
Night → POST { isClosed: true }
```

Player cố tạo lobby với `timeSlot = "night"` → API trả `400`.

---

## Quy tắc nghiệp vụ bổ sung

1. **Idempotent**: `POST` luôn upsert theo `(cafeId, timeSlot)` — gọi 2 lần với cùng body chỉ update `UpdatedAt`, không tạo row thứ 2.
2. **Audit**: Thay đổi override không ghi audit log riêng (chỉ `CreatedAt` / `UpdatedAt` trên row).
3. **Hiệu lực tức thì**: Override mới tạo áp dụng cho cả lobby/reservation **chưa khởi tạo**. Lobby / reservation **đã tồn tại** giữ schedule snapshot tại thời điểm tạo.
4. **Cache**: Backend không cache schedule — mỗi request resolve trực tiếp từ DB. Nếu cần scale, nên thêm cache layer ở phase sau.

---

## Liên kết

- **Domain rule:** [lobby-booking-deposit-bvc.mdc](../.cursor/rules/lobby-booking-deposit-bvc.mdc) §7.1 + §XIII
- **Source code:**
  - `BoardVerse.Core/Entities/CafeScheduleOverride.cs`
  - `BoardVerse.Data/Configurations/CafeScheduleOverrideConfiguration.cs`
  - `BoardVerse.Core/Constants/IScheduleResolver.cs`
  - `BoardVerse.Services/Services/CafeScheduleResolver.cs`
  - `BoardVerse.API/Controllers/CafeScheduleController.cs`
- **Tests:** `BoardVerse.Tests/Services/CafeScheduleTests.cs`, `CafeScheduleResolverTests.cs`
