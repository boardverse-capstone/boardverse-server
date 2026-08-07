# ReceiptController

> **P-01**: Receipt Generation API
> **P-02**: Revenue Report API

Tài liệu mô tả 2 endpoint: lấy receipt cho phiên đã thanh toán và báo cáo doanh thu theo kỳ.

---

## Base route

`/api/v1`

---

## GET /api/v1/sessions/{sessionId}/receipt

### Mục đích

Lấy receipt chi tiết cho một phiên chơi đã thanh toán (`GroupSessionStatus = Paid`).

### Authorization

| Role | Allowed |
|------|---------|
| `Admin` | ✅ |
| `Manager` | ✅ |
| `CafeStaff` | ✅ |

JWT token bắt buộc trong header `Authorization: Bearer <token>`.

### Path parameters

| Name | Type | Required | Mô tả |
|------|------|----------|--------|
| `sessionId` | Guid | ✅ | Mã phiên chơi |

### Response

#### 200 — Receipt chi tiết

```json
{
  "statusCode": 200,
  "message": "Tạo receipt thành công.",
  "data": {
    "sessionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "cafeName": "BoardVerse Cafe Thủ Đức",
    "cafeAddress": "123 Đường ABC, Thủ Đức, TP.HCM",
    "sessionStart": "2026-08-07T09:00:00Z",
    "sessionEnd": "2026-08-07T13:30:00Z",
    "durationMinutes": 270,
    "gameName": "Catan",
    "tableName": "Bàn 3",
    "members": [
      {
        "memberId": "...",
        "userId": "...",
        "displayName": "player_a",
        "isGuestSlot": false,
        "durationMinutes": 270,
        "subtotal": 60000,
        "depositApplied": 50000,
        "penalty": 0,
        "total": 10000
      },
      {
        "memberId": "...",
        "userId": null,
        "displayName": "Khách vô danh",
        "isGuestSlot": true,
        "durationMinutes": 240,
        "subtotal": 60000,
        "depositApplied": 0,
        "penalty": 15000,
        "total": 75000
      }
    ],
    "totalSubtotal": 120000,
    "totalDepositApplied": 50000,
    "totalPenalty": 15000,
    "grandTotal": 85000,
    "paidAt": "2026-08-07T13:35:00Z"
  },
  "timestamp": "2026-08-07T13:35:00Z",
  "path": "/api/v1/sessions/.../receipt"
}
```

#### 401 — Thiếu hoặc token không hợp lệ

```json
{ "statusCode": 401, "message": "Unauthorized." }
```

#### 403 — Không có quyền

```json
{ "statusCode": 403, "message": "Forbidden." }
```

#### 404 — Không tìm thấy phiên chơi

```json
{ "statusCode": 404, "message": "Phiên chơi với ID ... không tìm thấy." }
```

#### 409 — Phiên chưa được thanh toán

```json
{ "statusCode": 409, "message": "Receipt chỉ có thể tạo cho phiên đã thanh toán. Trạng thái hiện tại: ..." }
```

---

## GET /api/v1/cafes/{cafeId}/revenue

### Mục đích

Lấy báo cáo doanh thu cho một quán trong khoảng thời gian xác định, với các mức chi tiết: **daily**, **weekly**, hoặc **monthly**.

### Authorization

| Role | Allowed |
|------|---------|
| `Admin` | ✅ |
| `Manager` | ✅ |

JWT token bắt buộc.

### Path parameters

| Name | Type | Required | Mô tả |
|------|------|----------|--------|
| `cafeId` | Guid | ✅ | Mã quán |

### Query parameters

| Name | Type | Required | Default | Mô tả |
|------|------|----------|---------|--------|
| `startDate` | DateOnly | ✅ | — | Ngày bắt đầu (`yyyy-MM-dd`) |
| `endDate` | DateOnly | ✅ | — | Ngày kết thúc (`yyyy-MM-dd`) |
| `granularity` | string | ❌ | `daily` | `daily` \| `weekly` \| `monthly` |

### Response

#### 200 — Báo cáo doanh thu

```json
{
  "statusCode": 200,
  "message": "Lấy báo cáo doanh thu thành công.",
  "data": {
    "cafeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "cafeName": "BoardVerse Cafe Thủ Đức",
    "startDate": "2026-08-01",
    "endDate": "2026-08-07",
    "granularity": "daily",
    "totalRevenue": 850000,
    "totalDepositsApplied": 400000,
    "totalPenalties": 50000,
    "totalSessions": 12,
    "totalMembers": 35,
    "periods": [
      {
        "periodKey": "2026-08-01",
        "periodStart": "2026-08-01",
        "periodEnd": "2026-08-01",
        "revenue": 120000,
        "depositsApplied": 50000,
        "penalties": 0,
        "sessionCount": 2,
        "memberCount": 5,
        "byGame": [
          {
            "gameTemplateId": "...",
            "gameName": "Catan",
            "sessionCount": 1,
            "revenue": 60000
          }
        ]
      },
      {
        "periodKey": "W32",
        "periodStart": "2026-08-03",
        "periodEnd": "2026-08-09",
        "revenue": 450000,
        "depositsApplied": 200000,
        "penalties": 30000,
        "sessionCount": 6,
        "memberCount": 18,
        "byGame": [
          {
            "gameTemplateId": "...",
            "gameName": "Catan",
            "sessionCount": 3,
            "revenue": 220000
          },
          {
            "gameTemplateId": "...",
            "gameName": "Splendor",
            "sessionCount": 3,
            "revenue": 230000
          }
        ]
      }
    ]
  }
}
```

#### 400 — Dữ liệu không hợp lệ

```json
{ "statusCode": 400, "message": "Ngày kết thúc phải lớn hơn hoặc bằng ngày bắt đầu." }
```

```json
{ "statusCode": 400, "message": "Granularity phải là 'daily', 'weekly' hoặc 'monthly'." }
```

#### 401 — Thiếu hoặc token không hợp lệ

#### 403 — Không có quyền (chỉ Admin/Manager)

#### 404 — Không tìm thấy quán

---

## Chi tiết tính tiền (BR-15, BR-16)

Receipt sử dụng cùng logic tính tiền với `ActiveSessionService.PaySessionAsync`:

### Mô hình Flat Entry

```csharp
// BR-16: Giá giờ đầu = giá vé vào cổng; các block tiếp theo = 0
Subtotal = Cafe.BasePrice
```

### Mô hình Time-based

```csharp
// BR-16: Giờ đầu + block lũy tiến
if (minutes <= 60)
    Subtotal = BasePrice;
else
    Subtotal = BasePrice + additionalBlocks × TieredBlockRate;
```

### Tổng hợp (BR-15)

```csharp
// Mỗi thành viên: Total = Subtotal + Penalty - DepositAppliedAmount
// DepositAppliedAmount là tiền cọc giữ chỗ (KHÔNG trừ vào hóa đơn theo BR-09)
Member.Total = Math.Max(0, subtotal + penalty - depositApplied);
```

---

## Liên quan

- [cafe-pos.md](./cafe-pos.md) — POS controller, có endpoint `/pay` tạo phiên `Paid`.
- [boardverse.mdc](../../.cursor/rules/boardverse.mdc) — BR-15, BR-16, BR-09.
- `ReceiptService.cs` — triển khai business logic.
- `SessionReceiptDto.cs` — DTO cho receipt.
- `RevenueReportDto.cs` — DTO cho báo cáo doanh thu.
