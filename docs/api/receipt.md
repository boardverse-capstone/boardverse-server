# ReceiptController

> **P-01**: Receipt Generation API
> **P-02**: Revenue Report API
> **M2/C2.15**: Member Receipt API — Gap #32 (per-member receipt cho expense report / khiếu nại)

Tài liệu mô tả 3 endpoint:

1. Lấy receipt tổng cho phiên đã thanh toán.
2. Báo cáo doanh thu theo kỳ.
3. **(M2)** Lấy receipt riêng cho 1 thành viên trong phiên — dùng cho expense report, khiếu nại, hoặc in lại bill cá nhân.

Xem thêm: `docs/api/m2-member-payment.md` (M2 hub) và `docs/design/host-deposit-discount-and-bvc-payment-design.md` §C2.15.

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

## GET /api/v1/sessions/{sessionId}/members/{memberId}/receipt

### Mục đích

**M2/C2.15 (Gap #32)** — Lấy receipt riêng cho 1 thành viên trong phiên đã thanh toán. Dùng cho:

- Expense report cá nhân của thành viên sau khi chơi.
- Khiếu nại / dispute (member cần bằng chứng bill của chính mình).
- In lại bill nếu POS rỗ thất lạc giấy in.

Khác với `/receipt` (lấy toàn phiên), endpoint này trả về **chỉ thông tin của 1 member**: tổng bill cá nhân (subtotal + penalty − deposit), phần BVC đã trừ, phần cash còn lại, lý do refund (nếu có).

### Authorization

| Role | Điều kiện |
|------|-----------|
| `Admin` | ✅ luôn |
| `Manager` | ✅ nếu là chủ quán (`cafe.ManagerId == currentUser.Id`) của session |
| `CafeStaff` | ✅ nếu là staff của cafe của session |
| `Player` | ✅ nếu `member.UserId == currentUser.Id` HOẶC là host của session |

JWT token bắt buộc trong header `Authorization: Bearer <token>`.

### Path parameters

| Name | Type | Required | Mô tả |
|------|------|----------|--------|
| `sessionId` | Guid | ✅ | Mã phiên chơi (`ActiveSession.Id`) |
| `memberId` | Guid | ✅ | Mã thành viên (`ActiveSessionMember.Id`) |

### Query parameters

| Name | Type | Required | Default | Mô tả |
|------|------|----------|---------|--------|
| `format` | string | ❌ | `json` | Hiện tại chỉ hỗ trợ `json`. PDF/PNG sẽ được tích hợp khi bổ sung **QuestPDF** ở release sau — request `pdf`/`png` trả 400. |

### Response

#### 200 — File receipt JSON

```
HTTP/1.1 200 OK
Content-Type: application/json
Content-Disposition: attachment; filename="receipt-{sessionId}-{memberId}.json"

<JSON bytes: MemberReceiptDto>
```

`MemberReceiptDto` gồm:

- **Cafe info**: `cafeName`, `cafeAddress`
- **Session info**: `sessionStart`, `sessionEnd`, `durationMinutes`, `gameName`, `tableName`
- **Member info**: `memberId`, `userId`, `displayName`, `isGuestSlot`, `isHost` (true = Host, false = Member), `paidAt`
- **Bill breakdown**:
  - `subtotal` (decimal) — tiền giờ
  - `penaltyAmount` (decimal) — phí phạt linh kiện, **luôn 0 cho `Guest_Slot`** (BR-14)
  - `depositApplied` (decimal) — cọc đã trừ (BR-09: cọc KHÔNG trừ vào hóa đơn)
  - `totalAmount` (decimal) = `subtotal + penalty - depositApplied`
- **M2 BVC breakdown**:
  - `paidBvcAmount` (long) — số BVC đã trừ khi trả bill
  - `paidCashRemainder` (decimal) — phần cash còn lại (chỉ cho `PartialBvc`)
  - `bvcRefundedAt` (DateTime?) — thời điểm refund (nếu có)
  - `bvcRefundReason` (string) — lý do refund
- **Payment info**: `paymentMethod` (`BVC` | `BVC_PARTIAL` | `Cash`), `paymentStatus` (`PaidBvc` | `PartialBvc` | `PaidCash` | `RefundedBvc`), `orderId`, `transactionId`
- `receiptGeneratedAt` (DateTime) — timestamp backend tạo receipt.

Ví dụ member đã thanh toán đủ bằng BVC:

```json
{
  "sessionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "memberId": "...",
  "userId": "...",
  "displayName": "player_a",
  "isHost": false,
  "isGuestSlot": false,
  "cafeName": "BoardVerse Cafe Thủ Đức",
  "cafeAddress": "123 Đường ABC, Thủ Đức, TP.HCM",
  "gameName": "Catan",
  "tableName": "Bàn 3",
  "sessionStart": "2026-10-01T14:00:00Z",
  "sessionEnd": "2026-10-01T16:30:00Z",
  "durationMinutes": 150,
  "subtotal": 60000,
  "depositApplied": 50000,
  "penaltyAmount": 0,
  "totalAmount": 10000,
  "paidAt": "2026-10-01T16:30:00Z",
  "paymentMethod": "BVC",
  "paymentStatus": "PaidBvc",
  "orderId": "BVC-A1B2C3D4E5",
  "transactionId": "...",
  "paidBvcAmount": 100,
  "paidCashRemainder": 0,
  "bvcRefundedAt": null,
  "bvcRefundReason": null,
  "receiptGeneratedAt": "2026-10-01T16:31:00Z"
}
```

Ví dụ member trả 1 phần BVC + 1 phần cash (PartialBvc):

```json
{
  "...": "...",
  "paidBvcAmount": 30,
  "paidCashRemainder": 30000,
  "paymentMethod": "BVC_PARTIAL",
  "paymentStatus": "PartialBvc"
}
```

Ví dụ member Guest_Slot — penalty = 0:

```json
{
  "...": "...",
  "isGuestSlot": true,
  "displayName": "Khách vô danh",
  "userId": null,
  "subtotal": 60000,
  "penaltyAmount": 0,
  "depositApplied": 0,
  "totalAmount": 60000,
  "paymentMethod": "Cash",
  "paymentStatus": "PaidCash"
}
```

#### 400 — Format không hỗ trợ

```json
{ "statusCode": 400, "message": "Định dạng receipt không được hỗ trợ: 'pdf'. Chỉ chấp nhận 'json' hiện tại (PDF/PNG sẽ được hỗ trợ khi tích hợp QuestPDF ở release sau)." }
```

#### 401 — Thiếu hoặc token không hợp lệ

```json
{ "statusCode": 401, "message": "Unauthorized." }
```

#### 403 — Không đủ quyền

- Player không phải member này và không phải host:

  ```json
  { "statusCode": 403, "message": "Bạn không có quyền truy cập receipt của thành viên này." }
  ```

- Manager không phải chủ quán của session, hoặc CafeStaff không phải staff của cafe:

  ```json
  { "statusCode": 403, "message": "Bạn không có quyền thao tác trên cafe này." }
  ```

#### 404 — Không tìm thấy phiên chơi hoặc thành viên

```json
{ "statusCode": 404, "message": "Phiên chơi với ID ... không tìm thấy." }
```

```json
{ "statusCode": 404, "message": "Không tìm thấy thành viên với ID ... trong phiên chơi này." }
```

#### 409 — Phiên chưa được thanh toán

```json
{ "statusCode": 409, "message": "Receipt chỉ có thể tạo cho phiên đã thanh toán. Trạng thái hiện tại: ..." }
```

### Lưu ý

- **Roadmap**: PDF/PNG support qua QuestPDF sẽ được tích hợp ở release sau. Hiện tại `json` là format duy nhất được hỗ trợ để tránh giả lập MIME type không khớp với nội dung file.
- Member bị refund sau khi thanh toán BVC sẽ thấy `bvcRefundedAt` + `bvcRefundReason` được populate, `status = RefundedBvc`. Audit trail xem ở `MemberPaymentAuditLog` (xem `docs/api/m2-member-payment.md`).
- Guest_Slot (`isGuestSlot=true`) **luôn có `penaltyAmount = 0`** (BR-14, Gap #5). Backend enforce rule này ở service layer.

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
- [m2-member-payment.md](./m2-member-payment.md) — M2 hub: bill-preview, pay-bill, refund-bill, force-close.
- [boardverse.mdc](../../.cursor/rules/boardverse.mdc) — BR-15, BR-16, BR-09.
- [host-deposit-discount-and-bvc-payment-design.md](../../docs/design/host-deposit-discount-and-bvc-payment-design.md) — §C2.15 member receipt, §C2.16 force-close.
- `ReceiptService.cs` — triển khai business logic (`GenerateSessionReceiptAsync`, `GenerateMemberReceiptAsync`, `GetRevenueReportAsync`).
- `MemberReceiptDto.cs` — DTO cho per-member receipt.
- `SessionReceiptDto.cs` — DTO cho to-phiên receipt.
- `RevenueReportDto.cs` — DTO cho báo cáo doanh thu.
