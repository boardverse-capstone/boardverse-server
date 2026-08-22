# Player Session APIs

Player-facing APIs cho phép player quản lý phiên chơi của mình trên mobile app.
Cho phép: xem phiên hiện tại, gia hạn thời gian, thanh toán bằng BVC, xem lịch sử.

**Base path**: `/api/v1/sessions`
**Role**: Player — yêu cầu JWT Bearer token hợp lệ.

---

## 1. GET /api/v1/sessions/me/current

Lấy thông tin phiên chơi hiện tại của player (đang active/playing/suspended-mutation).

### Response 200

```json
{
  "status": 200,
  "message": "Lấy phiên hiện tại thành công.",
  "data": {
    "sessionId": "guid",
    "cafeName": "BoardVerse Cafe Thủ Đức",
    "cafeId": "guid",
    "lobbyId": "guid-or-null",
    "memberStatus": "Playing",
    "sessionStatus": "Active",
    "joinedAt": "2026-08-22T10:00:00Z",
    "joinedAtOffset": "2026-08-22T17:00:00+07:00",
    "elapsedMinutes": 35,
    "totalMinutesPlayed": 35,
    "costEstimate": {
      "baseMinutes": 60,
      "subtotal": 60000,
      "penaltyAmount": 0,
      "depositApplied": 0,
      "totalDue": 60000,
      "currency": "VND"
    },
    "gameName": "Catan",
    "totalGroupMembers": 4,
    "canExtend": true,
    "canPay": false,
    "isPaid": false,
    "lastExtensionRequest": {
      "requestId": "guid",
      "requestedMinutes": 30,
      "approvedMinutes": null,
      "estimatedAdditionalCostVnd": 30000,
      "status": "Pending",
      "rejectionReason": null,
      "requestedAt": "2026-08-22T10:30:00Z",
      "requestedAtUtc": "2026-08-22T10:30:00Z",
      "processedAt": null,
      "processedAtOffset": null
    }
  }
}
```

### Response 404 — không có phiên active

```json
{
  "status": 404,
  "code": "NOT_FOUND",
  "message": "Bạn không có phiên chơi nào đang hoạt động."
}
```

### Fields giải thích

| Field | Mô tả |
|----|----|
| `lobbyId` | Null = walk-in (không qua lobby). Có giá trị = lobby session. |
| `memberStatus` | `Playing` / `SuspendedMutation` / `Finished`. |
| `sessionStatus` | `Active` / `Checking` / `Unpaid` / `Paid`. |
| `joinedAt` | UTC timestamp. |
| `joinedAtOffset` | ISO 8601 với timezone (UTC+7 cho VN). FE dùng để hiển thị local time. |
| `lastExtensionRequest` | Yêu cầu gia hạn gần nhất (Pending/Approved/Rejected/Expired). Null nếu chưa từng yêu cầu. |

---

## 2. POST /api/v1/sessions/me/extend

Player yêu cầu gia hạn thêm thời gian chơi. Yêu cầu được staff duyệt trước khi áp dụng.

### Request Body

```json
{
  "extensionMinutes": 30
}
```

### Response 200 — tạo request thành công

```json
{
  "status": 200,
  "message": "Đã gửi yêu cầu gia hạn. Vui lòng chờ staff duyệt.",
  "data": {
    "requestId": "guid",
    "sessionId": "guid",
    "requestedMinutes": 30,
    "estimatedAdditionalCostVnd": 30000,
    "status": "Pending",
    "success": true,
    "message": null,
    "newEndTime": null,
    "totalMinutesBooked": 0,
    "estimatedAdditionalCost": 30000
  }
}
```

### Response 409 — phiên không gia hạn được

| Lý do | Message |
|----|----|
| Phiên đang tạm dừng | `Phiên chơi đang tạm dừng, vui lòng liên hệ nhân viên để tiếp tục trước khi gia hạn.` |
| Phiên đã thanh toán | `Phiên chơi đã hoàn tất thanh toán, không thể gia hạn thêm.` |
| Phiên đang chờ thanh toán | `Phiên chơi đang chờ thanh toán. Vui lòng thanh toán trước khi gia hạn.` |

### Response 404 — player không còn trong phiên (đã rời)

```json
{
  "status": 404,
  "message": "Bạn không tham gia phiên chơi này hoặc đã rời."
}
```

### Validation

| Rule | Error |
|----|----|
| `extensionMinutes <= 0` | `Số phút gia hạn phải lớn hơn 0.` |
| `extensionMinutes > 480` | `Số phút gia hạn không được vượt quá 480 phút (8 giờ).` |
| BVC không đủ | `Số dư BVC của bạn có thể không đủ để thanh toán phần gia hạn này. Vui lòng nạp thêm BVC trước khi staff duyệt yêu cầu.` (warning, không block) |

---

## 3. POST /api/v1/sessions/me/pay

Player thanh toán phiên chơi bằng BVC. Chỉ áp dụng cho phiên đang ở trạng thái `Unpaid`.

### Rate limit

**10 lần / user / 5 phút** (GAP-20). Vượt quá trả 429.

### Request Body

```json
{
  "sessionId": "guid"
}
```

### Response 200 — thanh toán thành công

```json
{
  "status": 200,
  "message": "Thanh toán thành công. Phiên chơi đã hoàn tất.",
  "data": {
    "success": true,
    "message": "Thanh toán thành công. Phiên chơi đã hoàn tất.",
    "invoice": {
      "sessionId": "guid",
      "totalMinutes": 60,
      "subtotal": 60000,
      "penaltyAmount": 0,
      "depositApplied": 0,
      "totalDue": 60000,
      "currency": "VND",
      "lineItems": [
        {
          "type": "BaseHourly",
          "description": "Phí giờ chơi",
          "minutes": 60,
          "ratePerMinute": 1000,
          "amount": 60000
        }
      ]
    },
    "bvcDeducted": 60,
    "remainingBvcBalance": 440,
    "paymentMethod": "BVC"
  }
}
```

### Response 429 — vượt rate limit

```json
{
  "status": 429,
  "code": "RATE_LIMIT_EXCEEDED",
  "message": "Bạn đã thử thanh toán quá nhiều lần. Vui lòng thử lại sau vài phút.",
  "retryAfterSeconds": 60
}
```

### Response 402 — không đủ BVC

```json
{
  "status": 402,
  "code": "INSUFFICIENT_BVC",
  "message": "Số dư BVC không đủ để thanh toán. Bạn cần 60 BVC nhưng chỉ có 30 BVC. Nhấn 'Nạp BVC' để nạp thêm nhé!"
}
```

### Response 409 — phiên không ở trạng thái Unpaid

```json
{
  "status": 409,
  "message": "Phiên chơi phải ở trạng thái chờ thanh toán để thanh toán."
}
```

### Response 403 — guest slot

```json
{
  "status": 403,
  "message": "Khách vô danh không thể thanh toán qua ứng dụng. Vui lòng thanh toán tại quầy."
}
```

### Audit

Mỗi lần `PlayerPaySessionAsync` thành công, ghi 1 dòng `PlayerActionHistory` với:
- `ActionType = SessionPaymentBvc`
- `ActionBy = userId` (self)
- `Metadata`: sessionId, cafeId, subtotal, penalty, deposit, total, bvcDeducted.

---

## 4. GET /api/v1/sessions/me/history

Lấy lịch sử các phiên đã chơi (đã thanh toán xong).

### Query params

| Param | Type | Default | Mô tả |
|----|----|----|----|
| `limit` | int (1-100) | 20 | Số phiên tối đa trả về. |
| `beforePaidAt` | DateTime (UTC) | null | Cursor: lấy các phiên cũ hơn mốc này. Dùng cho load-more pagination. |
| `fromDate` | DateTime (UTC) | null | Lọc các phiên từ ngày này trở đi. |
| `toDate` | DateTime (UTC) | null | Lọc các phiên đến ngày này. |

### Response 200

```json
{
  "status": 200,
  "message": "Lấy lịch sử phiên thành công.",
  "data": [
    {
      "sessionId": "guid",
      "cafeName": "BoardVerse Cafe Thủ Đức",
      "cafeId": "guid",
      "lobbyId": "guid-or-null",
      "gameName": "Catan",
      "sessionStatus": "Paid",
      "joinedAt": "2026-08-15T10:00:00Z",
      "joinedAtOffset": "2026-08-15T17:00:00+07:00",
      "paidAt": "2026-08-15T11:00:00Z",
      "paidAtOffset": "2026-08-15T18:00:00+07:00",
      "totalMinutesPlayed": 60,
      "totalAmountDue": 60000,
      "memberStatus": "Finished",
      "currency": "VND"
    }
  ]
}
```

### Response 400 — `fromDate > toDate`

```json
{
  "status": 400,
  "message": "fromDate phải nhỏ hơn hoặc bằng toDate."
}
```

### Filter logic (GAP-2 + GAP-8)

- Bao gồm cả **walk-in sessions** (không filter theo `LobbyId`).
- Filter theo `member.Status == Finished` (player đã rời phiên).
- **KHÔNG** filter theo `session.Status == Paid` (walk-in có thể không chuyển Paid nhưng member Finished vẫn hiển thị).
- Date range filter dựa trên `PaidAt ?? StartedAt`.

### Cursor pagination example

```
GET /api/v1/sessions/me/history?limit=20
 → trả 20 phiên mới nhất

GET /api/v1/sessions/me/history?limit=20&beforePaidAt=2026-08-01T10:00:00Z
 → trả 20 phiên cũ hơn 2026-08-01 10:00 UTC

GET /api/v1/sessions/me/history?limit=20&fromDate=2026-07-01&toDate=2026-07-31
 → trả các phiên trong tháng 7/2026
```

---

## Error codes chung

| Status | Code | Mô tả |
|----|----|----|
| 400 | `BAD_REQUEST` | Validation input. |
| 401 | `UNAUTHORIZED` | Thiếu token, token hết hạn. |
| 402 | `INSUFFICIENT_BVC` | Không đủ BVC để thanh toán. |
| 403 | `FORBIDDEN` | Guest slot, không có quyền. |
| 404 | `NOT_FOUND` | Phiên không tồn tại / player không tham gia. |
| 409 | `CONFLICT` | Trạng thái phiên không cho phép thao tác. |
| 429 | `RATE_LIMIT_EXCEEDED` | Vượt quá rate limit. |
| 500 | `INTERNAL_SERVER_ERROR` | Lỗi hệ thống không mong đợi. |

---

## Timezone

Mọi timestamp trả về có **2 dạng**:

1. `joinedAt` / `paidAt`: UTC DateTime (backward compatible).
2. `joinedAtOffset` / `paidAtOffset`: DateTimeOffset với timezone offset (UTC+7 cho VN).

FE nên ưu tiên dùng `*Offset` field để hiển thị local time chính xác.

---

## Tests

- `BoardVerse.Tests/Services/PlayerSessionGapsTests.cs` — unit tests cho GAP-1, GAP-3, GAP-8, GAP-9, GAP-12, GAP-13.

---

## Versioning

| Version | Date | Changes |
|----|----|----|
| 1.0 | 2026-08-12 | Initial — me/current, me/extend, me/pay, me/history. |
| 1.1 | 2026-08-22 | GAP-3 timezone, GAP-7 cursor, GAP-8 date range, GAP-9 lastExtensionRequest, GAP-11 invoice breakdown, GAP-13 LeftAt, GAP-18 response 500 docs, GAP-19 audit, GAP-20 rate-limit. |
| 1.2 | 2026-08-22 | Controller path `/api/v1/sessions` (đồng bộ với `PlayerSessionController.cs`). DTO `GetCurrentSessionResponseDto` thêm `TotalGroupMembers` và `IsPaid`; `LastExtensionRequestDto` thêm `RequestedAtUtc` + `ProcessedAtOffset`. ExtendSession trả `ExtendSessionResponseDto` với `Success`, `Message`, `NewEndTime`, `TotalMinutesBooked`, `EstimatedAdditionalCost`. PaySession trả `PlayerPaySessionResponseDto` với `Invoice.LineItems` (`InvoiceLineItemDto`: Type/Description/Minutes/RatePerMinute/Amount), `BvcDeducted`, `RemainingBvcBalance`, `PaymentMethod`. History trả `SessionHistoryResponseDto` (TotalAmountDue thay vì TotalAmount, MemberStatus cho biết về sớm/no-show). Rate-limit policy = `PaymentPolicy` (10 lần/5 phút). |

---

## Liên quan

- Controller: `BoardVerse.API/Controllers/PlayerSessionController.cs`
- DTOs: `BoardVerse.Core/DTOs/Session/PlayerSessionDtos.cs`
- POS-side staff approve/reject extension: [cafe-pos.md](./cafe-pos.md#extension-requests)
- Realtime notification cho POS khi player request extend: [pos-hub.md](./pos-hub.md#session-extension-events)