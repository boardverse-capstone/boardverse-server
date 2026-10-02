# M2 Member Payment + Receipt + Force-Close

> **Phần bổ sung 2026-10-01** — `docs/design/host-deposit-discount-and-bvc-payment-design.md` §C2.

Tổng cộng **6 endpoints mới** phục vụ M2 + M1 member-merge:

| # | Endpoint | Mô tả | Doc |
|---|---|---|---|
| 1 | `GET /sessions/{sessionId}/members/{memberId}/bill-preview` | Preview bill cá nhân của 1 member (trước khi trả) | [`wallet.md` §M2](./wallet.md#m2-member-bvc-bill-payment-case-2) |
| 2 | `POST /sessions/{sessionId}/members/{memberId}/pay-bill` | Member thanh toán bill bằng BVC (Case 2) | [`wallet.md` §M2](./wallet.md#m2-member-bvc-bill-payment-case-2) |
| 3 | `POST /sessions/{sessionId}/refund-bill/{memberPaymentAuditLogId}` | Staff refund BVC bill đã thanh toán (Gap #11) | [`wallet.md` §M2](./wallet.md#m2-member-bvc-bill-payment-case-2) |
| 4 | `GET /sessions/{sessionId}/members/{memberId}/receipt` | Receipt riêng cho 1 member (Gap #32) | §[Member receipt](#get-apiv1sessionssessionidmembersmemberidreceipt) dưới đây — xem thêm [`receipt.md`](./receipt.md) |
| 5 | `POST /sessions/{sessionId}/force-close` | Manager force-close phiên có unpaid members (Gap #33) | §[Force-close](#post-apiv1sessionssessionidforce-close) dưới đây |
| 6 | `POST /cafes/{cafeId}/member-merge/handle` | Staff xử lý merge member giữa lobby (M1 Option A, refund per-member deposit) | §[Member merge](#post-apiv1cafescafeidmember-mergehandle) dưới đây |

**Phân bố doc:**

- **Bill payment flow (1, 2, 3)** — document trong [`wallet.md`](./wallet.md) §M2 (vì liên quan trực tiếp đến `BvcLedgerEntry`).
- **Member receipt (4)** — document trong cả file này và [`receipt.md`](./receipt.md) (file này tóm tắt, `receipt.md` có đầy đủ response examples).
- **Force-close (5)** — document trong file này (endpoint duy nhất của `ForceCloseController`).
- **Member merge (6)** — document trong file này (endpoint duy nhất của `MergeController`).

---

## GET `/api/v1/sessions/{sessionId}/members/{memberId}/receipt`

Receipt riêng cho 1 member trong session đã thanh toán. Dùng cho expense report / khiếu nại.

> **Xem chi tiết response schema (MemberReceiptDto)** tại [`receipt.md`](./receipt.md#get-apiv1sessionssessionidmembersmemberidreceipt) — file đó có đầy đủ JSON examples cho từng trường hợp (PaidBvc / PartialBvc / Guest_Slot / RefundedBvc).

### Authorization

| Role | Quyền |
|---|---|
| `Admin` | ✅ luôn |
| `Manager` | ✅ nếu là chủ quán (`cafe.ManagerId == currentUser.Id`) |
| `CafeStaff` | ✅ nếu là staff của cafe của session |
| `Player` | ✅ nếu là member đó (`member.UserId == currentUser.Id`) hoặc là host của session |

### Path

| Param | Type | Description |
|---|---|---|
| `sessionId` | Guid | ID phiên chơi (đã `Paid`) |
| `memberId` | Guid | ID thành viên |

### Query

| Param | Type | Default | Description |
|---|---|---|---|
| `format` | string | `json` | Hiện tại chỉ hỗ trợ `json` (PDF/PNG sẽ được tích hợp khi bổ sung QuestPDF ở release sau). Request `pdf`/`png` trả về 400 BadRequest. |

### Response `200`

`File(bytes, "application/json", "receipt-{sessionId}-{memberId}.json")`.

Trả về `MemberReceiptDto` rendered thành JSON bytes. Schema đầy đủ xem ở [`receipt.md`](./receipt.md#response).

### Error responses

| Code | Message |
|---|---|
| `400` | `Định dạng receipt không được hỗ trợ: 'pdf'. Chỉ chấp nhận 'json' hiện tại (PDF/PNG sẽ được hỗ trợ khi tích hợp QuestPDF ở release sau).` |
| `403` | - Player không phải member này và không phải host.<br>- Manager không phải chủ quán của session (`cafe.ManagerId ≠ currentUser.Id`).<br>- CafeStaff không phải staff của cafe của session.<br>- Role khác ngoài Admin/Manager/CafeStaff/Player. |
| `404` | Session hoặc member không tồn tại. |
| `409` | Session chưa thanh toán (`status != Paid`). |

> **Roadmap**: PDF/PNG support qua QuestPDF sẽ được tích hợp ở release sau. Hiện tại JSON là format duy nhất được hỗ trợ để tránh giả lập MIME type không khớp với nội dung file.

---

## POST `/api/v1/sessions/{sessionId}/force-close`

Manager/Admin force-close session khi có unpaid members không thể chờ `all-paid → Paid`. Đặc biệt cho no-show scenarios (Gap #33).

### Authorization

`[Authorize(Roles = "Manager,Admin")]` — chỉ manager quán hoặc admin hệ thống. (JWT role `Manager`, không phải `CafeManager`.)

### Request body — `ForceCloseRequestDto`

```json
{
  "unpaidMemberHandling": "MarkNoShow",
  "reason": "Mất liên lạc với khách sau 30 phút",
  "allowLatePayment": true
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `unpaidMemberHandling` | string | ✅ | `MarkNoShow` \| `MarkAsDebt` \| `CompensationByHost`. |
| `reason` | string | ✅ | Lý do force-close, ≤ 500 ký tự. |
| `allowLatePayment` | bool | No (default `true`) | `true` = unpaid members vẫn có thể trả sau; `false` = close hẳn. |

### Handling modes

| Mode | Hành vi |
|---|---|
| `MarkNoShow` | `member.Status = IndividualSessionStatus.NoShow`, set `NoShowAt` + `NoShowReason`. Member vẫn có thể trả sau nếu `AllowLatePayment=true`. |
| `MarkAsDebt` | `member.Status = IndividualSessionStatus.Finished`. Insert `MemberDebt` row (status=Pending). |
| `CompensationByHost` | Validate host đã paid. Set `member.PaymentStatus = PaidByHost`. |

### Side effects

1. Validate `session.Status == GroupSessionStatus.Unpaid` (throw 409 nếu đã Paid).
2. Find unpaid members (`PaymentStatus == NotPaid`). Throw 409 nếu không có unpaid → nên dùng `/pay` thay.
3. Switch theo `UnpaidMemberHandling` cho mỗi unpaid member.
4. Nếu `AllowLatePayment=false` VÀ vẫn còn unpaid → throw 409 (chọn `MarkAsDebt` hoặc `CompensationByHost`).
5. Nếu tất cả unpaid đã resolved:
   - `session.Status = GroupSessionStatus.Paid`, `PaidAt = now`.
   - Release table/box/inventory (existing pay flow).
6. Ngược lại (`AllowLatePayment=true`):
   - `session.Status = GroupSessionStatus.UnpaidForced`.
7. Insert `ForceCloseAuditLog` row (audit trail — who, when, why, which members).

### Response `200` — `ForceCloseResponseDto`

```json
{
  "sessionId": "...",
  "status": "Paid",
  "forceClosedAt": "2026-10-01T17:00:00Z",
  "unpaidHandling": "MarkNoShow",
  "unpaidMemberCount": 1,
  "unpaidMembers": [
    {
      "memberId": "...",
      "displayName": "Khách vô danh",
      "amount": 60000,
      "handling": "MarkedNoShow",
      "debtId": null
    }
  ],
  "auditLogId": "..."
}
```

| Field | Type | Description |
|---|---|---|
| `sessionId` | Guid | Session đã force-close. |
| `status` | string | Status cuối: `"Paid"` (nếu hết unpaid) \| `"UnpaidForced"` (nếu còn). |
| `forceClosedAt` | DateTime | Thời điểm force-close (UTC). |
| `unpaidHandling` | string | Echo lại handling từ request: `MarkNoShow` \| `MarkAsDebt` \| `CompensationByHost`. |
| `unpaidMemberCount` | int | Số unpaid members đã xử lý. |
| `unpaidMembers` | array | Chi tiết từng unpaid member (xem bảng dưới). |
| `auditLogId` | Guid | ID của `ForceCloseAuditLog` row insert (cho audit/trace). |

`unpaidMembers[]`:

| Field | Type | Description |
|---|---|---|
| `memberId` | Guid | ID member đã xử lý. |
| `displayName` | string | Username hoặc `GuestDisplayName`. |
| `amount` | decimal | Số tiền còn nợ (= `Subtotal + Penalty - DepositApplied`). |
| `handling` | string | Handling applied cho member: `"MarkedNoShow"` \| `"MarkedAsDebt"` \| `"CoveredByHost"`. |
| `debtId` | Guid? | ID của `MemberDebt` row (chỉ có khi `handling = MarkedAsDebt`). |

### Error responses

| Code | Message |
|---|---|
| `403` | Không phải Manager/Admin. |
| `404` | Session không tồn tại. |
| `409` | Session không ở Unpaid; không có unpaid members; `AllowLatePayment=false` mà vẫn còn unpaid. |

### Enum mới (M2/C2.16)

| Enum | Value mới |
|---|---|
| `MemberPaymentStatus.PaidByHost` | 6 |
| `IndividualSessionStatus.NoShow` | 3 |
| `GroupSessionStatus.UnpaidForced` | 5 |

Project dùng `HasConversion<int>()` → KHÔNG CẦN `ALTER TYPE` trên DB.

---

## POST `/api/v1/cafes/{cafeId}/member-merge/handle`

M1 / Option A — Staff xử lý merge member giữa lobby (Exception 4, boardverse-business-context.mdc §4).
Refund per-member deposit (nếu có) về ví của member đó. Host deposit FOLLOWS merged members qua merge chain (xem `LobbyMergeService.ApproveMergeAsync`).

### Authorization

`[Authorize(Roles = "Manager,CafeStaff,Admin")]` — staff/manager phải thuộc cafe đang vận hành; Admin bypass ownership check.

### Request body — `HandleMemberMergeRequestDto`

```json
{
  "memberId": "...",
  "fromLobbyId": "...",
  "toLobbyId": "..."
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `memberId` | Guid | ✅ | ID của `ActiveSessionMember` cần merge. |
| `fromLobbyId` | Guid | ✅ | Lobby mà member đang ở (lobby nguồn). |
| `toLobbyId` | Guid? | No | Lobby đích. `null` = member tách khỏi lobby cũ, stand-alone. |

### Response `200` — `MergeMemberRefundResponseDto`

```json
{
  "memberId": "...",
  "userId": "...",
  "depositRefunded": true,
  "depositRefundedBvc": 50000,
  "ledgerEntryId": "...",
  "auditLogId": "...",
  "action": "Refunded_OnMerge",
  "reason": "Member merge từ Lobby ... → ...",
  "processedAt": "2026-10-01T17:00:00Z"
}
```

Các giá trị `action` có thể:

| Action | Ý nghĩa |
|---|---|
| `Refunded_OnMerge` | Member có deposit chưa apply, refund thành công. |
| `GuestSlot_NoDeposit` | Member là Guest_Slot — không có deposit để refund. |
| `DepositConsumed_BeforeMerge` | Member có deposit đã apply vào bill trước merge → không refund. |
| `NoDepositToRefund` | Member không có DepositId (BR-22 chưa active). |
| `IdempotentReplay` | Member đã refund trước đó (DepositRefundedAt != null). |

### Error responses

| Code | Message |
|---|---|
| `400` | `MemberId` hoặc `FromLobbyId` rỗng. |
| `403` | Staff/Manager không thuộc cafe đang vận hành. |
| `404` | Member không tồn tại. |
| `500` | Lỗi hệ thống không mong đợi. |

---

## Tables mới (M2/C2.16)

### `MemberDebts`

| Column | Type | Description |
|---|---|---|
| `Id` | UUID PK | |
| `MemberId` | UUID FK → ActiveSessionMembers | |
| `UserId` | UUID NULL FK → Users | Guest thì null |
| `SessionId` | UUID FK → ActiveSessions | |
| `AmountBvc` | BIGINT | legacy support (M2 chỉ dùng cash) |
| `AmountCash` | DECIMAL(18,0) | Số tiền nợ |
| | INT | 0=Pending, 1=Resolved, 2=WrittenOff |
| `Reason` | VARCHAR(500) | Lý do ghi nợ |
| `CreatedAt` | TIMESTAMPTZ | |
| `ResolvedAt` | TIMESTAMPTZ NULL | |
| `ResolvedByUserId` | UUID NULL FK → Users | |

Indexes: `MemberId`, `SessionId`, `Status`, `(UserId, Status)`.

### `ForceCloseAuditLogs`

| Column | Type | Description |
|---|---|---|
| `Id` | UUID PK | |
| `SessionId` | UUID FK → ActiveSessions | |
| `TriggeredByUserId` | UUID FK → Users | |
| `UnpaidHandling` | VARCHAR(30) | `MarkNoShow` \| `MarkAsDebt` \| `CompensationByHost` |
| `Reason` | VARCHAR(500) | |
| `CreatedAt` | TIMESTAMPTZ | |
| `UnpaidMemberIds` | UUID[] | Snapshot tại thời điểm close |
| `SessionClosedAfter` | BOOL | True nếu flip → Paid |

Indexes: `SessionId`, `TriggeredByUserId`, `CreatedAt`.

### `MemberPaymentAuditLogs`

Audit trail cho mỗi lần member trả bill (CASH / BVC / BVC_PARTIAL). Refund flow rely hoàn toàn vào bảng này.

| Column | Type | Description |
|---|---|---|
| `Id` | UUID PK | |
| `MemberId` | UUID FK → ActiveSessionMembers | |
| `UserId` | UUID NULL FK → Users | Guest thì null |
| `PaymentMethod` | VARCHAR(20) | `Bvc` \| `BvcPartial` \| `Cash` |
| `AmountBvc` | BIGINT | Số BVC đã trừ (0 nếu cash) |
| `AmountCash` | NUMERIC(18,0) | Phần cash (cho PartialBvc) |
| `WalletTxnId` | UUID NULL FK → BvcLedgerEntries | Ledger entry cho BVC debit (null nếu cash) |
| `IdempotencyKey` | VARCHAR(100) | UNIQUE — chống double-tap |
| `CreatedByStaffId` | UUID NULL FK → Users | Staff trigger (null nếu member tự trả) |
| `CreatedAt` | TIMESTAMPTZ | |
| `RefundedAt` | TIMESTAMPTZ NULL | Set khi refund (Gap #11) |
| `RefundReason` | VARCHAR(500) | Lý do refund |
| `RefundLedgerEntryId` | UUID NULL FK → BvcLedgerEntries | Ledger entry cho refund |

Indexes: `MemberId`, `UserId`, `CreatedAt`, `PaymentMethod`, `WalletTxnId`, `RefundLedgerEntryId`, `CreatedByStaffId`, `(PaymentMethod, CreatedAt)`, `(UserId, CreatedAt)`, **UNIQUE** `IdempotencyKey`.

### `MemberDepositAuditLogs`

Audit trail cho per-member deposit refund (M1 / Option A — Exception 4 merge flow).

| Column | Type | Description |
|---|---|---|
| `Id` | UUID PK | |
| `MemberId` | UUID FK → ActiveSessionMembers | |
| `UserId` | UUID NULL FK → Users | |
| `Action` | VARCHAR(50) | `Refunded_OnMerge` \| `GuestSlot_NoDeposit` \| `DepositConsumed_BeforeMerge` \| `NoDepositToRefund` |
| `AmountBvc` | BIGINT | Số BVC refund (0 nếu skip) |
| `DepositId` | UUID NULL FK → BookingDeposits | Deposit gốc |
| `FromLobbyId` | UUID NULL FK → Lobbies | Lobby nguồn |
| `FromSessionId` | UUID NULL FK → ActiveSessions | Session gốc của member |
| `ToLobbyId` | UUID NULL FK → Lobbies | Lobby đích (null nếu stand-alone) |
| `ToSessionId` | UUID NULL FK → ActiveSessions | Session đích |
| `MergedAt` | TIMESTAMPTZ NULL | |
| `Reason` | VARCHAR(500) | |
| `IdempotencyKey` | VARCHAR(100) | UNIQUE |
| `LedgerEntryId` | UUID NULL FK → BvcLedgerEntries | Refund ledger entry |
| `CreatedByUserId` | UUID NULL FK → Users | Staff trigger |
| `CreatedAt` | TIMESTAMPTZ | |

Indexes: `MemberId`, `UserId`, `CreatedAt`, `Action`, `(UserId, Action, CreatedAt)`, **UNIQUE** `IdempotencyKey`.
