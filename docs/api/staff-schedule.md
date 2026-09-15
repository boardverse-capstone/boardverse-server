# Staff Work Schedule API

API quản lý lịch làm việc cho staff, nghỉ phép, đổi ca, điểm danh check-in/out và ngày nghỉ cố định.

> **Domain:** Manager quản lý lịch cho staff của quán mình. Staff tự quản lý yêu cầu nghỉ phép, đổi ca, check-in/out và ngày nghỉ cố định.
>
> **Tính năng chính:**
> - Nhiều ca/ngày (vd: sáng 6-12h, chiều 14-22h).
> - Ca qua đêm (StartTime > EndTime, vd: 22h-06h).
> - Copy lịch tuần (template).
> - Phát hiện xung đột ca khi tạo/cập nhật.
> - Phân loại ca (Regular/Overtime/OnCall/GameMaster).
> - Yêu cầu nghỉ phép (Manager duyệt → tự động tạo StaffUnavailableDate).
> - Yêu cầu đổi ca giữa 2 staff (Manager duyệt → tự động hoán đổi StaffUserId của 2 lịch).
> - Điểm danh check-in/out (tính LateMinutes / EarlyLeaveMinutes, xử lý đúng cho ca qua đêm).
> - Ngày nghỉ cố định (cảnh báo khi lên lịch trùng).
> - Thông báo push notification cho khi có thay đổi.

> **Authorization chặt (đã fix GAP C5):** Mọi endpoint `[Authorize(Roles = "Admin,Manager")]` đều check thêm
> `cafe.ManagerId == callerId` (Admin bypass). Manager của cafe A không thể truy cập resource của cafe B.

> **Giới hạn độ dài ca (đã fix GAP M3):** Mỗi ca tối đa 16 giờ (960 phút). Tạo / cập nhật lịch dài hơn → 400 BadRequest.

> **Copy template tuần (đã fix GAP C4):** Mặc định chỉ copy các template mà staff thực sự đã đi làm trong tuần nguồn
> (có ShiftAttendance). Nếu tuần nguồn không có attendance nào → fallback copy toàn bộ template Active (backward-compatible).

> **Bulk create atomic (đã fix GAP H1):** `POST /staff-schedules/bulk` wrap trong 1 transaction Serializable; validate tất cả schedules trước khi insert bất kỳ record nào. Fail 1 = rollback toàn bộ.

> **Staff delete isolation (đã fix GAP C6):** Khi gỡ staff khỏi cafe, tất cả `StaffSchedule.Status = Cancelled` (soft-cancel trong transaction). `StaffUnavailableDate` giữ nguyên — staff tự xóa nếu cần.

> **Cross-cafe IDOR (đã fix GAP-IDOR-01):** Mọi endpoint theo `scheduleId` re-resolve cafe qua FK và áp dụng `EnsureCafeManagerAsync`. Manager A không thể đọc/sửa `StaffSchedule` của cafe B dù có GUID.

> **FK Restrict (đã fix GAP H7):** `ShiftAttendances.ScheduleId` ON DELETE RESTRICT (không Cascade). Manager xóa nhầm schedule có attendance → 409 `ShiftAttendanceExistCannotDeleteSchedule`. Bảo vệ audit trail lịch sử điểm danh.

> **Migration ID comment (đã fix GAP M1):** Comment trong SQL + Configuration tham chiếu đúng migration ID `20260915113350_RestrictAttendanceScheduleDelete`.

> **EF/SQL migration conflict (đã fix GAP M2):** Raw SQL `staff_schedule_migration.sql` ghi cả 2 migration ID (`20260915113350` + `20260915170000`) vào `__EFMigrationsHistory` với `ON CONFLICT DO NOTHING`. `dotnet ef database update` không re-apply.

---

## 0. TỔNG HỢP 9 GAPS ĐÃ ĐÓNG (2026-09-15)

| # | Gap | Severity | Fix tóm tắt |
|---|---|---|---|
| C5 | Manager cafe A truy cập resource cafe B (IDOR ngang) | HIGH | `EnsureCafeManagerAsync` guard trên mọi Manager endpoint |
| M3 | Không validate max duration 16h | MEDIUM | Validate `duration <= 960 phút` → throw `DurationExceeds16Hours` |
| C4 | Copy template copy cả lịch staff không đi làm tuần nguồn | MEDIUM | Filter `ShiftAttendance` tuần nguồn; fallback copy Active nếu rỗng |
| H1 | Bulk create không atomic | HIGH | 1 transaction Serializable; validate tất cả trước insert |
| C6 | `DELETE staff` không xóa schedule cũ | MEDIUM | Soft-cancel tất cả `StaffSchedule.Status = Cancelled` trong transaction |
| IDOR-01 | Cross-cafe access qua `scheduleId` route | HIGH | Re-resolve cafe qua FK + `EnsureCafeManagerAsync` |
| H7 | FK `ShiftAttendances` ON DELETE CASCADE | HIGH | Migration `20260915113350_RestrictAttendanceScheduleDelete` đổi sang RESTRICT |
| M1 | Comment trong SQL tham chiếu migration ID sai | LOW | Sửa comment trong SQL + Configuration |
| M2 | EF migration chưa ghi `__EFMigrationsHistory` | HIGH | Raw SQL INSERT cả 2 migration ID với `ON CONFLICT DO NOTHING` |

**Tổng: 9/9 gaps closed. 1820/1820 tests PASS. Build 0 Error.**

Chi tiết kỹ thuật: [`docs/technical/gaps-fix-2026-09-15.md`](../technical/gaps-fix-2026-09-15.md).
Chi tiết migration fix (M1, M2): [`docs/technical/staff-schedule-migration-fix-2026-09-15.md`](../technical/staff-schedule-migration-fix-2026-09-15.md).

---

## I. ROLE & AUTHORIZATION

| Endpoint Group | Role |
|---|---|
| Manager APIs (`/api/v1/cafes/{cafeId}/...`) | `Admin`, `Manager` |
| Staff Self-Service (`/api/staff/...`) | `Admin`, `Manager`, `CafeStaff` |

---

## II. MANAGER: STAFF SCHEDULES

### 1. GET /api/v1/cafes/{cafeId}/staff-schedules

Lấy danh sách lịch làm việc của quán. Lọc theo staff và/hoặc thứ trong tuần.

**Query params:**

| Name | Type | Required | Description |
|---|---|---|---|
| `staffUserId` | UUID | No | Lọc theo staff |
| `dayOfWeek` | int (0-6) | No | Lọc theo thứ (0=Sunday, 1=Monday, ...) |

**Response 200:**
```json
{
  "statusCode": 200,
  "message": "Lấy danh sách lịch làm việc thành công.",
  "data": {
    "cafeId": "cafe-guid",
    "filterStaffUserId": null,
    "filterDayOfWeek": null,
    "totalCount": 14,
    "schedules": [
      {
        "id": "schedule-guid",
        "cafeId": "cafe-guid",
        "staffUserId": "user-guid",
        "staffName": "staff_username",
        "dayOfWeek": 1,
        "dayOfWeekName": "Thứ 2",
        "startTime": "06:00:00",
        "endTime": "14:00:00",
        "isOvernight": false,
        "durationMinutes": 480,
        "shiftType": "Regular",
        "isRecurring": true,
        "note": "Ca sáng chính",
        "status": "Active",
        "createdAt": "2026-09-15T...",
        "updatedAt": "2026-09-15T..."
      }
    ]
  }
}
```

---

### 3. POST /api/v1/cafes/{cafeId}/staff-schedules

Tạo 1 lịch làm việc mới.

**Body:**
```json
{
  "staffUserId": "user-guid",
  "dayOfWeek": 1,
  "startTime": "06:00:00",
  "endTime": "14:00:00",
  "shiftType": "Regular",
  "isRecurring": true,
  "note": "Ca sáng chính"
}
```

> **Ca qua đêm:** `endTime < startTime` → ví dụ `startTime: "22:00:00"`, `endTime: "06:00:00"` → `isOvernight: true`, `durationMinutes: 480`.

**Response 201:** Trả về `StaffScheduleResponseDto`.

**Errors:**
- 400: `StartTimeEqualsEndTime`, `NotCafeStaff`.
- 409: `OverlappingSchedule` (trùng ca khác trong cùng DayOfWeek).

---

### 4. POST /api/v1/cafes/{cafeId}/staff-schedules/bulk

Tạo nhiều lịch cùng lúc.

**Body:**
```json
{
  "schedules": [
    { "staffUserId": "...", "dayOfWeek": 1, "startTime": "06:00:00", "endTime": "14:00:00", ... },
    { "staffUserId": "...", "dayOfWeek": 3, "startTime": "14:00:00", "endTime": "22:00:00", ... }
  ]
}
```

**Response 201:** Danh sách `StaffScheduleResponseDto[]`.

---

### 5. PUT /api/v1/cafes/{cafeId}/staff-schedules/{scheduleId}

Cập nhật lịch làm việc (giờ, loại ca, trạng thái, ghi chú).

**Body:**
```json
{
  "startTime": "08:00:00",
  "endTime": "16:00:00",
  "shiftType": "Regular",
  "isRecurring": true,
  "note": "Ca sáng đã cập nhật",
  "status": "Active"
}
```

**Response 200:** Trả về `StaffScheduleResponseDto` đã cập nhật.

---

### 6. DELETE /api/v1/cafes/{cafeId}/staff-schedules/{scheduleId}

Xóa 1 lịch làm việc.

**Response 200:**
```json
{ "id": "schedule-guid" }
```

---

### 7. POST /api/v1/cafes/{cafeId}/staff-schedules/copy-template

Copy lịch từ tuần nguồn sang tuần đích.

**Body:**
```json
{
  "fromWeekStart": "2026-09-14",
  "toWeekStart": "2026-09-21",
  "staffUserIdFilter": null
}
```

> `staffUserIdFilter = null` → copy toàn bộ staff; set GUID → chỉ copy 1 staff.

**Response 200:**
```json
{
  "copiedCount": 14,
  "fromWeekStart": "2026-09-14",
  "toWeekStart": "2026-09-21",
  "newSchedules": [...]
}
```

---

### 8. GET /api/v1/cafes/{cafeId}/staff-schedules/hours-summary

Tổng hợp số giờ làm của 1 staff.

**Query params:**

| Name | Type | Required | Description |
|---|---|---|---|
| `staffUserId` | UUID | Yes | Staff cần tổng hợp |
| `startDate` | date | Yes | Ngày bắt đầu (yyyy-MM-dd) |
| `endDate` | date | Yes | Ngày kết thúc (yyyy-MM-dd) |

**Response 200:** `WorkHoursSummaryDto` gồm:
- `totalScheduledMinutes` (giờ lịch dự kiến).
- `totalWorkedMinutes` (giờ thực tế từ ShiftAttendance).
- `totalLateMinutes`, `totalEarlyLeaveMinutes`.
- `presentDays`, `absentDays`, `inProgressDays`.

---

## III. MANAGER: TIME-OFF REQUESTS

### 1. GET /api/v1/cafes/{cafeId}/time-off-requests?status=Pending

Danh sách yêu cầu nghỉ phép của quán (lọc theo status).

**Response 200:** Danh sách `TimeOffRequestResponseDto[]`.

---

### 2. POST /api/v1/cafes/{cafeId}/time-off-requests/{id}/review

Manager duyệt / từ chối yêu cầu nghỉ phép. **Khi duyệt, hệ thống tự động tạo `StaffUnavailableDate` cho từng ngày trong khoảng StartDate..EndDate.**

**Body:**
```json
{
  "status": "Approved",
  "reviewNote": "Đồng ý — bạn nghỉ ở nhà nhé."
}
```

**Response 200:** Trả về `TimeOffRequestResponseDto` sau khi cập nhật.

**Errors:**
- 404: `TimeOffRequestNotFound`.
- 409: `TimeOffAlreadyReviewed`.

---

## IV. MANAGER: SHIFT SWAPS

### 1. GET /api/v1/cafes/{cafeId}/shift-swaps?status=Pending

Danh sách yêu cầu đổi ca của quán.

**Response 200:** Danh sách `ShiftSwapRequestResponseDto[]` (kèm thông tin 2 lịch).

---

### 2. POST /api/v1/cafes/{cafeId}/shift-swaps/{id}/review

Manager duyệt / từ chối yêu cầu đổi ca. **Khi duyệt, 2 lịch StaffSchedule được hoán đổi StaffUserId.**

**Body:**
```json
{
  "status": "Approved",
  "reviewNote": "OK, bạn A và B đổi ca cho nhau."
}
```

**Response 200:** Trả về `ShiftSwapRequestResponseDto`.

---

## V. STAFF SELF-SERVICE

### 1. My Schedules

#### GET /api/staff/my-schedules/{cafeId}
Lấy lịch làm việc của tôi tại 1 quán (tất cả template theo DayOfWeek).

#### GET /api/staff/my-schedules/{cafeId}/range?startDate=...&endDate=...
Lấy lịch của tôi trong khoảng ngày (expanded theo từng ngày).

**Query params:**

| Name | Type | Required | Description |
|---|---|---|---|
| `startDate` | date | Yes | Ngày bắt đầu |
| `endDate` | date | Yes | Ngày kết thúc |

**Response 200:** `StaffScheduleListResponseDto`.

---

### 2. Attendance (Check-in/out)

#### GET /api/staff/my-attendance/{cafeId}?startDate=...&endDate=...
Lấy danh sách điểm danh của tôi.

#### POST /api/staff/my-attendance/{cafeId}/check-in?scheduleId={scheduleId}

**Body:**
```json
{
  "checkInTime": "2026-09-15T08:00:00Z",
  "note": "Đến đúng giờ"
}
```

> `checkInTime` optional — nếu không truyền, dùng `DateTime.UtcNow`.

**Response 201:** `ShiftAttendanceResponseDto` kèm `lateMinutes` tính sẵn.

#### POST /api/staff/my-attendance/{id}/check-out

**Body:**
```json
{
  "checkOutTime": "2026-09-15T14:05:00Z",
  "note": "Về muộn 5 ph"
}
```

**Response 200:** `ShiftAttendanceResponseDto` kèm `earlyLeaveMinutes` tính sẵn.

---

### 3. Time-off (Yêu cầu nghỉ phép)

#### GET /api/staff/my-time-off
Lấy danh sách yêu cầu nghỉ phép của tôi.

#### POST /api/staff/my-time-off/{cafeId}

**Body:**
```json
{
  "startDate": "2026-09-20",
  "endDate": "2026-09-22",
  "reason": "Về quê có việc gia đình"
}
```

**Response 201:** `TimeOffRequestResponseDto` (status = `Pending`).

---

### 4. Unavailable Dates (Ngày nghỉ cố định)

#### GET /api/staff/my-unavailable/{cafeId}?from=2026-09-01&to=2026-12-31

Lấy danh sách ngày nghỉ cố định của tôi.

#### POST /api/staff/my-unavailable/{cafeId}

**Body:**
```json
{
  "date": "2026-10-01",
  "reason": "Khám sức khỏe định kỳ"
}
```

#### DELETE /api/staff/my-unavailable/{id}

Xóa ngày nghỉ cố định.

---

### 5. Shift Swaps (Đổi ca)

#### GET /api/staff/shift-swaps/my-requests
Danh sách yêu cầu đổi ca tôi đã gửi.

#### GET /api/staff/shift-swaps/incoming
Danh sách yêu cầu đổi ca gửi đến tôi (Pending).

#### POST /api/staff/shift-swaps/{cafeId}

**Body:**
```json
{
  "targetStaffId": "user-guid-of-target",
  "requesterScheduleId": "my-schedule-id",
  "targetScheduleId": "target-schedule-id"
}
```

**Validation:**
- `requesterScheduleId != targetScheduleId` → lỗi.
- `requesterScheduleId` phải thuộc staff gửi yêu cầu.
- `targetScheduleId` phải thuộc `targetStaffId`.

**Response 201:** `ShiftSwapRequestResponseDto` (status = `Pending`).

---

## VI. RESPONSE SHAPES

Xem chi tiết tại `BoardVerse.Core/DTOs/StaffSchedule/`:
- `StaffScheduleRequestDtos.cs`
- `StaffScheduleResponseDtos.cs`

---

## VII. ENUM SHORTCUTS

| Enum | Values |
|---|---|
| `StaffScheduleStatus` | `Active`, `Inactive`, `Cancelled` |
| `ShiftType` | `Regular`, `Overtime`, `OnCall`, `GameMaster` |
| `TimeOffStatus` | `Pending`, `Approved`, `Rejected`, `Cancelled` |
| `ShiftSwapStatus` | `Pending`, `Approved`, `Rejected`, `Cancelled` |
| `ShiftAttendanceStatus` (string) | `Present`, `Late`, `Absent`, `OnLeave`, `EarlyLeave` |

---

## VIII. NOTES & CAVEATS

1. **Ca qua đêm**: Khi `endTime < startTime`, ca kéo dài qua ngày hôm sau. `durationMinutes` được tính wrap-around 24h.
2. **Multiple shifts/day**: Cho phép nhiều ca trong cùng DayOfWeek (vd: sáng 6-12h + chiều 14-22h). Validation chỉ kiểm tra overlap giữa các ca cùng staff + cùng DayOfWeek.
3. **Time-off approved** tự động tạo `StaffUnavailableDate`. Staff có thể xóa những ngày này thủ công qua `DELETE /api/staff/my-unavailable/{id}`.
4. **Shift swap approved** tự động hoán đổi `StaffUserId` của 2 lịch. Manager cần verify trước khi duyệt để tránh đổi nhầm.
5. **Check-in tự động tính LateMinutes**: dựa trên `StaffSchedule.StartTime` của schedule đó.
6. **Push notification**: Mọi thay đổi (create/update/delete schedule, time-off approve/reject, shift-swap approve/reject) đều gửi push notification cho staff liên quan.