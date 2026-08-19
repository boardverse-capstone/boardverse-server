# CafeShiftController

**Base route:** `/api/shifts`
**Controller:** `CafeShiftController.cs`
**Role:** Manager, CafeStaff

API quản lý ca làm việc của quán. Mỗi ca có số dư tiền mặt đầu ca (`OpeningCashBalance`) và cuối ca (`ClosingCashBalance`) để đối soát.

## Endpoints

| Endpoint | Method | Role | Mô tả |
|----------|--------|------|--------|
| `/` | POST | Manager, Staff | Mở ca làm việc mới |
| `/{shiftId}/close` | POST | Manager, Staff | Đóng ca làm việc |
| `/current` | GET | Manager, Staff | Lấy ca đang mở |
| `/` | GET | Manager, Staff | Lấy lịch sử các ca (phân trang) |

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

## GET /api/shifts

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
  "message": "ShiftsRetrieved",
  "data": {
    "items": [
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
```

---

## Liên quan

- [cafe.md](./cafe.md) — Cafe management
- [cafe-pos.md](./cafe-pos.md) — POS operations trong ca
