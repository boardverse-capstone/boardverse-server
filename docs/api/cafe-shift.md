# CafeShiftController

**Base route:** `/api/shifts`
**Controller:** `CafeShiftController.cs`
**Role:** Admin, Manager, CafeStaff

API quản lý ca làm việc của quán. Mỗi ca có số dư tiền mặt đầu ca (`OpeningCashBalance`) và cuối ca (`ClosingCashBalance`) để đối soát. Ca đang mở còn theo dõi `TotalRevenue` và `TotalSessions` được cộng dồn theo thời gian thực mỗi khi một `ActiveSession` chuyển sang `Paid` (BR-CAFE-SHIFT-01, gọi từ `CafeShiftService.RecordSessionPaymentAsync`).

## Endpoints

| Endpoint | Method | Role | Mô tả |
|----------|--------|------|--------|
| `/` | POST | Admin, Manager, CafeStaff | Mở ca làm việc mới |
| `/{shiftId}/close` | POST | Admin, Manager, CafeStaff | Đóng ca làm việc |
| `/{shiftId}/recalculate` | POST | Admin, Manager, CafeStaff | Tính lại `TotalRevenue` + `TotalSessions` từ ActiveSession (idempotent) |
| `/current` | GET | Admin, Manager, CafeStaff | Lấy ca đang mở |
| `/history` | GET | Admin, Manager, CafeStaff | Lấy lịch sử các ca (phân trang) |

**Header:** `Authorization: Bearer <token>`

---

## POST /api/shifts

Mở ca làm việc mới cho quán.

### Business Rules

- Mỗi quán chỉ có **1 ca đang mở** tại một thời điểm.
- Phải đóng ca hiện tại trước khi mở ca mới.
- Chỉ Manager hoặc CafeStaff của quán mới được thực hiện.

### Body

```json
{
  "cafeId": "guid",
  "openingCashBalance": 500000
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `cafeId` | guid | ✅ | Quán cần mở ca |
| `openingCashBalance` | decimal | ✅ | Số dư tiền mặt đầu ca (≥ 0) |

### Response 201

```json
{
  "statusCode": 201,
  "message": "ShiftOpened",
  "data": {
    "id": "guid",
    "cafeId": "guid",
    "cafeName": "BoardGame Cafe A",
    "openedByUserId": "guid",
    "openedByUsername": "manager1",
    "openedAt": "2026-08-07T08:00:00Z",
    "closingCashBalance": null,
    "totalRevenue": 0,
    "totalSessions": 0,
    "status": "Open"
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `400` | Dữ liệu không hợp lệ |
| `401` | Thiếu token |
| `403` | Không có quyền vận hành quán này |
| `404` | Không tìm thấy quán |
| `409` | Đã có ca đang mở. Cần đóng ca hiện tại trước. |
| `500` | Lỗi hệ thống |

---

## POST /api/shifts/{shiftId}/close

Đóng ca làm việc đang mở.

### Business Rules

- Chỉ ca có `Status = Open` mới đóng được.
- Tính `TotalRevenue` và `TotalSessions` trong ca.
- Lưu `ClosingCashBalance` để đối soát.

### Body

```json
{
  "closingCashBalance": 850000
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `closingCashBalance` | decimal | ✅ | Số dư tiền mặt cuối ca (≥ 0) |

### Response 200

```json
{
  "statusCode": 200,
  "message": "ShiftClosed",
  "data": {
    "id": "guid",
    "cafeId": "guid",
    "cafeName": "BoardGame Cafe A",
    "openedByUserId": "guid",
    "openedByUsername": "manager1",
    "closedByUserId": "guid",
    "closedByUsername": "staff1",
    "openedAt": "2026-08-07T08:00:00Z",
    "closedAt": "2026-08-07T23:00:00Z",
    "openingCashBalance": 500000,
    "closingCashBalance": 850000,
    "totalRevenue": 350000,
    "totalSessions": 15,
    "status": "Closed"
  }
}
```

Trong đó:
- `totalRevenue = closingCashBalance - openingCashBalance + cash_withdrawn`

### Error Codes

| Status | Description |
|--------|-------------|
| `400` | Dữ liệu không hợp lệ |
| `401` | Thiếu token |
| `403` | Không có quyền vận hành quán này |
| `404` | Không tìm thấy ca |
| `409` | Ca đã được đóng trước đó |
| `500` | Lỗi hệ thống |

---

## POST /api/shifts/{shiftId}/recalculate

Tính lại `TotalRevenue` + `TotalSessions` cho ca bằng cách SUM `TotalAmount` + COUNT các `ActiveSession` đã `Paid` (Status=3) thuộc cùng `CafeId` trong cửa sổ `[shift.OpenedAt, shift.ClosedAt ?? UtcNow]`. **Idempotent** — gọi nhiều lần vẫn cho cùng kết quả.

### Business Rules

- Endpoint **chỉ ghi đè** 2 field: `TotalRevenue`, `TotalSessions`. Các field còn lại (`OpeningCashBalance`, `ClosingCashBalance`, `Status`, `OpenedAt`, `ClosedAt`, `OpenedByUserId`, `ClosedByUserId`) **không bị đụng**.
- Validate ownership: Admin bypass; Manager chỉ recalc shift của cafe mình (`cafe.ManagerId == callerUserId`); CafeStaff chỉ recalc shift của cafe mình (có dòng trong `CafeStaff`).
- Áp dụng cho cả ca `Open` lẫn ca `Closed`. Cửa sổ lấy `ClosedAt ?? UtcNow` cho ca đang mở.
- Inclusive ở cả 2 đầu cửa sổ: session vừa paid ngay lúc `OpenedAt` hay trước khi `ClosedAt` đều được tính.

### Use cases

| Case | Cách dùng |
|------|-----------|
| **Backfill** | Ca đã mở trước khi áp dụng fix `RecordSessionPaymentAsync` → totals bị lệch. Admin gọi endpoint này để tính lại. |
| **Drift detection** | `ShiftDriftDetectionJob` chạy hàng giờ so sánh `shift.TotalRevenue` với `SUM(ActiveSession.TotalAmount)`. Nếu khác → auto-reconcile bằng method này. |
| **Manual audit** | Staff nghi ngờ ca bị lệch → bấm 1 phát để đối soát. |

### Request

Không có body. Chỉ cần `shiftId` trên path.

```
POST /api/shifts/{shiftId}/recalculate
Authorization: Bearer <token>
```

### Response 200

```json
{
  "statusCode": 200,
  "message": "ShiftRecalculated",
  "data": {
    "id": "guid",
    "cafeId": "guid",
    "openedByUserId": "guid",
    "closedByUserId": null,
    "openedAt": "2026-08-07T08:00:00Z",
    "closedAt": null,
    "openingCashBalance": 500000,
    "closingCashBalance": 0,
    "totalRevenue": 2150000,
    "totalSessions": 12,
    "status": "Open"
  }
}
```

> **Lưu ý**: response trả về totals **sau khi đã ghi đè**. So sánh với totals trước recalc để biết mức drift. Log diff (prev → new) được ghi ở `CafeShiftService.RecalculateShiftTotalsAsync` với level `Information`.

### Error Codes

| Status | Description |
|--------|-------------|
| `401` | Thiếu token, token hết hạn hoặc token không hợp lệ |
| `403` | Không có quyền vận hành quán của ca này |
| `404` | Không tìm thấy ca làm việc |
| `500` | Lỗi hệ thống không mong đợi |

---

## GET /api/shifts/current

Lấy ca đang mở của quán.

### Query

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `cafeId` | guid | ✅ | Quán cần kiểm tra |

### Response 200

```json
{
  "statusCode": 200,
  "message": "CurrentShiftRetrieved",
  "data": {
    "id": "guid",
    "cafeId": "guid",
    "cafeName": "BoardGame Cafe A",
    "openedByUserId": "guid",
    "openedByUsername": "manager1",
    "openedAt": "2026-08-07T08:00:00Z",
    "closingCashBalance": null,
    "totalRevenue": 125000,
    "totalSessions": 8,
    "status": "Open"
  }
}
```

Nếu không có ca nào đang mở:

```json
{
  "statusCode": 200,
  "message": "CurrentShiftRetrieved",
  "data": null
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `400` | Thiếu cafeId |
| `401` | Thiếu token |
| `403` | Không có quyền truy cập quán này |
| `500` | Lỗi hệ thống |

---

## GET /api/shifts/history

Lấy lịch sử các ca làm việc của quán (phân trang).

### Query

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `cafeId` | guid | ✅ | Quán cần xem |
| `page` | int | No | Số trang (≥ 1). Mặc định 1 |
| `pageSize` | int | No | Số item/trang (1-100). Mặc định 10 |

### Response 200

```json
{
  "statusCode": 200,
  "message": "ShiftHistoryRetrieved",
  "data": {
    "shifts": [
      {
        "id": "guid",
        "cafeId": "guid",
        "openedByUserId": "guid",
        "openedByUsername": "manager1",
        "closedByUserId": "guid",
        "closedByUsername": "staff1",
        "openedAt": "2026-08-06T08:00:00Z",
        "closedAt": "2026-08-06T23:00:00Z",
        "openingCashBalance": 400000,
        "closingCashBalance": 750000,
        "totalRevenue": 350000,
        "totalSessions": 14,
        "status": "Closed"
      },
      {
        "id": "guid",
        "cafeId": "guid",
        "openedByUserId": "guid",
        "openedByUsername": "manager1",
        "openedAt": "2026-08-07T08:00:00Z",
        "closingCashBalance": null,
        "totalRevenue": 125000,
        "totalSessions": 8,
        "status": "Open"
      }
    ],
    "page": 1,
    "pageSize": 10,
    "totalCount": 45,
    "totalPages": 5
  }
}
```

### Error Codes

| Status | Description |
|--------|-------------|
| `400` | Thiếu cafeId hoặc tham số không hợp lệ |
| `401` | Thiếu token |
| `403` | Không có quyền truy cập quán này |
| `500` | Lỗi hệ thống |

---

## Shift Status

| Status | Description |
|--------|-------------|
| `Open` | Ca đang hoạt động |
| `Closed` | Ca đã kết thúc |

## State Machine

```
Open (OpenedBy)
   ↓ POST /{shiftId}/close
Closed (OpenedBy + ClosedBy)

Open | Closed
   ↓ POST /{shiftId}/recalculate (idempotent)
Open | Closed   # totals = SUM(ActiveSession Paid) trong cửa sổ ca
```

### BR-CAFE-SHIFT-01 — Cộng doanh thu real-time

Trong khi ca đang `Open`, mỗi khi một `ActiveSession` chuyển sang `Paid` thì `CafeShiftService.RecordSessionPaymentAsync` được gọi từ `ActiveSessionService.PaySessionCoreAsync` (POS pay) và `PlayerPaySessionAsync` (player pay BVC) để cộng dồn:

```
shift.TotalRevenue += session.TotalAmount
shift.TotalSessions += 1
```

- **Best-effort**: nếu quán chưa mở ca thì skip + log warning, **không throw** — payment vẫn commit.
- **Idempotency**: caller đã re-check `Status != Unpaid` trong transaction, nên chỉ session pay đầu tiên mới chạm method này.
- **Drift recovery**: nếu ca bị lệch (do deploy trước fix, webhook silent skip,...) → dùng `POST /{shiftId}/recalculate` để tính lại.

---

## Liên quan

- [cafe.md](./cafe.md) — Cafe management
- [cafe-pos.md](./cafe-pos.md) — POS operations trong ca
