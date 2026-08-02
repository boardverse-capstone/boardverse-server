# Booking API

**Controller:** `BookingController.cs` (`/api/bookings`), `BookingRatingController.cs` (`/api/bookings/{bookingId}/...`)

Domain phụ trách: tạo booking, check-in/out, theo dõi trạng thái session, no-show vote, cross-rating.

| Flow liên quan | Doc |
|---|---|
| Payment + SePay deposit | [payment.md](./payment.md) |
| POS session payment + settlement | [active-session.md](./active-session.md), [settlement.md](./settlement.md) |
| Webhook SePay xử lý deposit | [sepay-webhook.md](./sepay-webhook.md) |
| Lobby flow | [lobby.md](./lobby.md) |
| SignalR realtime events | §SignalR realtime events bên dưới |

---

## REST Endpoints — `BookingController`

| Method | Path | Role | Mô tả |
|---|---|---|---|
| `POST` | `/api/bookings` | Player (Host lobby) | Tạo booking từ lobby đã lock |
| `GET` | `/api/bookings/{bookingId}` | Player (owner), Manager, Admin | Chi tiết booking |
| `GET` | `/api/bookings/{bookingId}/session-status` | Player (lobby member đã check-in), Manager, Admin | Realtime session status (Task #8) |
| `GET` | `/api/bookings/lobby/{lobbyId}` | Player (lobby member), Manager, Admin | Booking theo lobby |
| `GET` | `/api/bookings/cafe/{cafeId}` | Player (summary rút gọn), Manager/CafeStaff/Admin (full) | Bookings theo quán (Task #14) |
| `PATCH` | `/api/bookings/{bookingId}` | Player (Host lobby) | Cập nhật bàn/giờ/số người |
| `DELETE` | `/api/bookings/{bookingId}` | Player (Host lobby) | Hủy booking |
| `POST` | `/api/bookings/{bookingId}/check-in` | Manager, CafeStaff | ~~Removed~~ — dùng `POST /api/cafes/{cafeId}/pos/check-in` (BR §21A.7) |
| `POST` | `/api/cafes/{cafeId}/pos/check-in` | Manager, CafeStaff | Check-in tại quán theo `code` (ReservationCode / BookingCode) |
| `POST` | `/api/bookings/{bookingId}/check-out` | Manager, CafeStaff | ~~Removed~~ — `ReservationService.CompleteAndCaptureAsync` khi ActiveSession PAID (BR-REVENUE-01) |

## REST Endpoints — `BookingRatingController`

| Method | Path | Role | Mô tả |
|---|---|---|---|
| `POST` | `/api/bookings/{bookingId}/no-show-votes` | Player (lobby member) | Submit vote vắng mặt (Task #4) |
| `POST` | `/api/bookings/{bookingId}/ratings` | Player (lobby member) | Submit cross-rating (Task #5) |
| `GET` | `/api/bookings/{bookingId}/ratings/status` | Player (lobby member) | Trạng thái rating của voter |

Xem chi tiết tại [booking-rating.md](./booking-rating.md).

---

## Luồng booking — cập nhật

### Happy path

```
1. Mobile: POST /api/v1/lobbies (tạo lobby)
   → Lobby status = Open

2. Members join → LobbyFull
   → Server tạo "intent" để đặt cọc

3. Mobile: POST /api/payments/booking-deposit
   Body: { cafeId, lobbyId?, scheduledStartTime, seatCount, amount }
   → Status: PENDING_DEPOSIT
   → BookingDeposit.QrUrl, QrExpiresAt = Now + 5min
   → SeatSlot: AVAILABLE → HOLDING

4. Customer quét QR → thanh toán qua SePay/VietQR
   → SePay gửi webhook → SePayWebhookController
   → POST /api/payments/sepay/webhook
   → PaymentService.HandleSePayWebhookAsync
   → BookingDeposit.Status = Paid
   → SeatSlot: HOLDING → RESERVED
   → Lobby.Status = Full → Ready for check-in

5. Customer đến quán → POS quét QR booking
   → Kiểm tra Booking.Status == Confirmed (chưa check-in)
   → ActiveSession tạo với DepositAppliedAmount = 0 (không trừ cọc)
   → Booking.Status = CheckedIn (cập nhật sau check-in)
   → SeatSlot: RESERVED → IN_USE
```

### Booking Status Flow (Updated)

```
Confirmed → CheckedIn (khi POS quét mã đặt chỗ thành công)
```

**Bug Fix:** Trước đây `StartSessionFromBookingAsync` không cập nhật `Booking.Status`. Bây giờ:
1. Kiểm tra `Booking.Status == CheckedIn` → ngăn chặn check-in 2 lần
2. Sau khi tạo session thành công → `Booking.Status = CheckedIn`

**P1 Fix #6: Race Condition Prevention**

Để ngăn chặn overbooking khi nhiều user đặt cùng bàn cùng lúc:

1. `CreateBookingAsync` sử dụng **pessimistic locking** với `FOR UPDATE SKIP LOCKED`
2. Toàn bộ flow tạo booking được wrap trong transaction
3. Kiểm tra xung đột chỉ áp dụng cho `BookingStatus != Cancelled` (Confirmed/CheckedIn mới là xung đột)

```csharp
// BookingRepository.GetConflictingBookingsWithLockAsync
SELECT * FROM "Bookings"
WHERE "CafeTableId" = @tableId
AND "Status" != @cancelled
AND "ScheduledStartTime" < @endTime
AND "ScheduleEndTime" > @startTime
FOR UPDATE SKIP LOCKED
```

### Exception paths

| Tình huống | Xử lý |
|------------|-------|
| Quá 5 phút không thanh toán (BR-06) | Background job → `BookingDeposit.Status = Expired`, SeatSlot về `AVAILABLE` |
| Quán hết chỗ thực tế (Exception 1) | Trả 409 + suggest quán thay thế qua `ActiveSessionController.GetAlternativeCafes` |
| Khách đến muộn quá 30 phút (Exception 5) | `BookingDeposit.Status = Expired`, tịch thu cọc theo `DepositRefundPolicy` |
| Webhook SePay timeout/fail | Retry exponential backoff (max 3 lần); fallback VietQR static QR |
| Quán hủy vì bất khả kháng (Exception 9, BR-18) | `BookingDeposit.Status = Refunded`, hoàn 100% cọc |

---

## State machine — `BookingDeposit`

```mermaid
stateDiagram-v2
    [*] --> Pending: POST /booking-deposit
    Pending --> Paid: SePay webhook success
    Pending --> Expired: Quá 5 phút không thanh toán (BR-06)
    Pending --> CancelledByPlayer: Khách hủy
    Paid --> Refunded: Quán hủy vì bất khả kháng (BR-18)
    Paid --> Forfeited: Không đến + hết thời gian cho phép
    Expired --> [*]: Giải phóng ghế
    CancelledByPlayer --> [*]
    Refunded --> [*]: Hoàn 100% cọc
    Forfeited --> [*]: Tịch thu cọc
```

## State machine — `SeatSlot`

```mermaid
stateDiagram-v2
    [*] --> Available
    Available --> Holding: Booking tạo (5 phút giữ)
    Holding --> Reserved: Payment success (BR-05)
    Holding --> Available: Quá 5 phút
    Reserved --> InUse: Check-in tại quán
    Reserved --> Available: Booking EXPIRED/CANCELLED
    InUse --> Available: Session PAID (giải phóng)
```

---

## API liên quan

- **Payment API chi tiết:** [payment.md](./payment.md) — tất cả endpoint của `PaymentController`.
- **Deposit flow:** [sepay-webhook.md](./sepay-webhook.md), [sepay-account.md](./sepay-account.md)
- **Lobby flow:** [lobby.md](./lobby.md)
- **POS session flow:** [cafe-pos.md](./cafe-pos.md), [active-session.md](./active-session.md)
- **Settlement (giải ngân):** [settlement.md](./settlement.md)
- **Business rules:** [sepay-payment-flow.mdc](../../.cursor/rules/sepay-payment-flow.mdc), [boardverse.mdc](../../.cursor/rules/boardverse.mdc) (BR-05, BR-06, BR-09)

---

## Booking — Mobile gaps (bổ sung 2026-08-01)

Các endpoint bổ sung theo `booking-payment-gaps.md` (Tasks #7, #8, #9, #12, #13, #14).

### Coverage status — 10/15 ✅ / 5 còn lại ⚠️

| # | Task | Endpoint / Method | Role | Phân loại | Status |
|---|------|--------------------|------|-----------|--------|
| 1 | Available tables | `GET /api/cafes/{cafeId}/available-tables` | Player | Bắt buộc | ✅ Done (`CafeBookingController.GetAvailableTables`) |
| 2 | Availability (time-slot) | `GET /api/cafes/{cafeId}/availability` | Player | Bắt buộc | ✅ Done (`CafeBookingController.GetAvailability`) |
| 3 | Walk-in booking (`lobbyId=null`) | `POST /api/bookings` (allow `lobbyId=null`) | Player | Bắt buộc | ✅ Done (`BookingService.CreateBookingAsync` chấp nhận `lobbyId=null`) |
| 4 | No-show-votes | `POST /api/bookings/{bookingId}/no-show-votes` | Player | Bắt buộc | ✅ Done (`BookingRatingService.SubmitNoShowVoteAsync`) |
| 5 | Ratings POST + GET status + aggregate | `POST/GET /api/bookings/{bookingId}/ratings` | Player | Bắt buộc | ✅ Done (`BookingRatingService.SubmitRatingsAsync` + `GetRatingStatusAsync` + `AggregateBookingOutcomesAsync`) |
| 6 | AuthZ Player GET deposit | `GET /api/payments/booking-deposit/{id}` | Player | Bắt buộc | ✅ Done (`PaymentController.GetBookingDeposit` cho phép Player owner) |
| 7 | SignalR booking events | realtime hub events | — | Nên có | ✅ Done (`BookingCheckedIn` / `BookingCheckedOut` / `BookingCancelled` / `BookingNoShowMarked`) |
| 8 | Session-status | `GET /api/bookings/{bookingId}/session-status` | Player | Nên có | ✅ Done (`BookingService.GetSessionStatusAsync`) |
| 9 | Push lobby auto-cancel | background job | — | Nên có | ✅ Done (`LobbyTimeoutJob` gọi `IPushNotificationService.SendToUsersAsync`) |
| 12 | Refund-policy | `PATCH /api/cafes/{cafeId}/deposit-refund-policy` | Manager | Nice-to-have | ✅ Done |
| 13 | Push khi Manager đổi giá + Update pricing-config | `PUT /api/cafes/{cafeId}/pricing-config` | Manager | Nice-to-have | ✅ Done (`CafeService.UpdatePricingConfigAsync` + SignalR event + push notification) |
| 14 | Bookings by cafe (Player view) | `GET /api/bookings/cafe/{cafeId}` | Player | Nice-to-have | ✅ Done (summary rút gọn theo role) |
| 15 | Validate `playerQuantity` PATCH | `PATCH /api/bookings/{id}` | Player | Bắt buộc sửa | ✅ Done (validate `playerQuantity ≤ lobby.CurrentMembers` + `≤ seatCount`) |
| 10 | BookingResponseDto fields | DTO bổ sung | — | Nên có | ✅ Done (thêm `CheckedInAt`, `CheckedInByUserId`, `LobbySummary`) |
| 11 | BookingDepositResponseDto fields | DTO bổ sung | — | Nên có | ✅ Done (thêm `BookingId`, `UserId`, `IsWalkIn`, `BookingGroupCode`) |

> **Tasks #9 + #13** đã wired với `IPushNotificationService` qua `FcmPushNotificationService` (Firebase Admin SDK). Cấu hình: `Firebase:Enabled=true` + `Firebase:ProjectId=boardverse-app` + `FIREBASE_CREDENTIALS_JSON` env var trên Render.

### KarmaLog audit trail — Task #5 side effect

Khi Staff thực hiện `POS check-out` (qua `ReservationService.CompleteAndCaptureAsync`) → tự động gọi `BookingRatingService.AggregateBookingOutcomesAsync(bookingId)`. Chi tiết aggregate workflow xem section [Aggregate Karma (Task #5)](#aggregate-karma-task-5) bên dưới.

> **Reuses existing `KarmaLog` table** (không tạo duplicate entity). `UserProfile.KarmaPoints` (int) làm source of truth. `KarmaLog.RelatedLobbyId` được dùng làm correlation id cho booking id. Công thức delta: `(avg - 3.0) * 10`, làm tròn về int qua `(int)Math.Round(delta)`. 12 unit tests passing.

> **No-show-votes validation window (Task #4):** Booking entity đã được bổ sung field `CheckedInAt` (DateTime?, nullable) + `CheckedInByUserId` (Guid?, nullable, FK → Users.Id) qua migration `20260801060358_AddCheckedInAtToBooking`. Khi Staff gọi `POST /api/cafes/{cafeId}/pos/check-in` (Reservation BVC flow) → backend set cả 2 field trên `Booking` entity. Vote window: voter chỉ được vote sau `CheckedInAt + 30 phút` (tránh vote ngay khi vừa check-in) và phải trước `ScheduleEndTime + 24h`.

### `GET /api/bookings/{bookingId}/session-status` — Task #8

**Role:** Player (chỉ member lobby đã check-in), Manager, Admin.

**Mục đích:** Mobile xem realtime ActiveSession status khi Staff thực hiện partial-checkout. Trả về: ai về sớm, bill bao nhiêu, phiên còn bao lâu, bill cuối dự kiến.

**Response 200:**
```json
{
  "statusCode": 200,
  "isSuccess": true,
  "data": {
    "bookingId": "uuid",
    "activeSessionId": "uuid",
    "sessionStatus": "Active",
    "startedAt": "2026-08-01T19:00:00Z",
    "currentDurationMinutes": 75,
    "members": [
      {
        "userId": "uuid",
        "username": "alice",
        "status": "LeftEarly",
        "leftAt": "2026-08-01T20:00:00Z",
        "partialBillAmount": 0,
        "partialBillPaid": false,
        "mergedIntoSessionId": null
      }
    ],
    "estimatedFinalBill": {
      "subtotal": 250000,
      "penalty": 0,
      "depositApplied": 50000,
      "total": 200000
    }
  }
}
```

**Lỗi:** `401`, `403` (không phải member), `404` (booking không tồn tại), `500`.

### `GET /api/bookings/cafe/{cafeId}` — Task #14 (Player view)

**Role:** Player (rút gọn summary), Manager/CafeStaff/Admin (full BookingResponseDto).

**Mục đích:** Mobile Discovery page hiển thị mật độ booking theo khung giờ. Player view trả summary fields — KHÔNG lộ `verificationQRCode`, `paymentRef`, `memberIds` (bảo mật).

**Response 200 (Player view):**
```json
{
  "statusCode": 200,
  "isSuccess": true,
  "data": [
    {
      "id": "uuid",
      "scheduledStartTime": "datetime",
      "scheduleEndTime": "datetime",
      "playerQuantity": 4,
      "status": "Confirmed"
    }
  ]
}
```

**Logic:** Backend tự detect role. Manager/CafeStaff/Admin → full DTO. Player → summary rút gọn.

### `PATCH /api/cafes/{cafeId}/deposit-refund-policy` — Task #12

**Role:** Manager (chỉ chủ quán).

**BR-18:** Manager cấu hình 1 trong 3 chính sách:
- `Full` (0): hoàn 100% cọc khi hủy
- `Partial` (1): hoàn theo bậc thang theo thời gian trước giờ hẹn
- `None` (2): không hoàn, tịch thu cọc về BoardVerse

**Request body:**
```json
{
  "policy": "Partial",
  "partialTiers": [
    { "minHoursBeforeScheduled": 24, "refundPercent": 50 },
    { "minHoursBeforeScheduled": 12, "refundPercent": 25 },
    { "minHoursBeforeScheduled": 0, "refundPercent": 0 }
  ]
}
```

**Validation:**
- `Policy=Partial` mà không có `partialTiers` → 400
- `partialTiers` tối đa 5 bậc
- 2 bậc cùng `minHoursBeforeScheduled` → 400
- `RefundPercent` ngoài 0-100 → 400

**Lỗi:** `400`, `403`, `404`, `500`.

### `PUT /api/cafes/{cafeId}/pricing-config` — Task #13

**Role:** Manager (chỉ chủ quán).

**BR-04:** Chỉ cho phép cập nhật khi quán đóng cửa (`IsPricingLocked=false`). Sau khi update → broadcast SignalR event `CafePricingChanged` cho member có booking trong tuần.

**Request body:**
```json
{
  "billingModel": "TimeBased",
  "basePrice": 90000,
  "tieredBlockRate": 25000,
  "tieredBlockMinutes": 15
}
```

**Response 200:**
```json
{
  "statusCode": 200,
  "isSuccess": true,
  "data": {
    "cafeId": "uuid",
    "billingModel": "TimeBased",
    "basePrice": 90000,
    "tieredBlockRate": 25000,
    "tieredBlockMinutes": 15,
    "isPricingLocked": false,
    "operationalProfileUpdatedAt": "2026-08-01T15:00:00Z",
    "affectedBookingsCount": 12
  }
}
```

**Lỗi:** `400`, `403`, `404`, `409` (quán đang hoạt động), `500`.

---

### `Bookings/{id}/check-out` — Aggregate Karma (Task #5)

**Role:** Manager/Staff (chỉ người có quyền POS).

**Mục đích:** Khi Staff check-out booking, backend tự động gọi `BookingRatingService.AggregateBookingOutcomesAsync(bookingId)` để:

1. **Cross-rating:** Đọc các `BookingRating` rows chưa aggregate (`IsAggregated = false`). Với mỗi user được rate, tính `avgScore = (attitude + sportsmanship + punctuality) / 3` của tất cả voters → `delta = (avgScore - 3.0) * 10`. Cộng/trừ `UserProfile.KarmaPoints` và ghi `KarmaLog` (Source = `PlayerCrossRating`, Category = `CrossRating`).
2. **No-show confirm:** Đọc các `BookingNoShowVote`. Với mỗi userId xuất hiện trong `> 50%` votes → trừ 10 `KarmaPoints` (Source = `SystemAutomatic`, Category = `NoShow`).
3. **Forfeit deposit:** Nếu no-show user có `BookingDeposit` ở `Paid` và `RefundPolicy = None` → set `Status = Forfeited` + ghi `KarmaLog` audit row (Source = `SystemAutomatic`, Category = `NoShow`).
4. **Idempotent:** Set `IsAggregated = true` cho tất cả rating rows. Staff có thể bấm check-out lại nhiều lần mà không tính trùng.

**Signature summary**:

```csharp
Task<BookingRatingAggregationResultDto> AggregateBookingOutcomesAsync(Guid bookingId);
```

**Response DTO (return value, hiện không exposed qua HTTP — internal booking workflow):**
```json
{
  "bookingId": "uuid",
  "aggregatedAt": "2026-08-01T20:00:00Z",
  "ratingsProcessed": 4,
  "karmaDeltaByUser": {
    "userId-A": 10.0,
    "userId-B": -5.0
  },
  "noShowConfirmedMembers": ["userId-C"],
  "forfeitedDepositIds": ["deposit-D"],
  "totalKarmaDelta": -5.0
}
```

**Audit log (`KarmaLog` table):**

| Field | Value |
|-------|-------|
| `UserId` | UserId bị ảnh hưởng Karma |
| `Source` | `PlayerCrossRating` (cross-rating) / `SystemAutomatic` (no-show + forfeit) |
| `ViolationCategory` | `CrossRating` / `NoShow` |
| `KarmaPointsChange` | Delta áp dụng (int) |
| `KarmaBefore` / `KarmaAfter` | Snapshot trước/sau |
| `Reason` | Human-readable audit message |
| `RelatedLobbyId` | Correlation → `BookingId` |
| `IsAdminAdjustment` | `false` |

**Edge cases:**
- Booking ở `PendingDeposit` / `Cancelled` → 409 Conflict (không aggregate).
- Không có rating rows chưa aggregate + không có no-show vote → return empty result, không có side effect.
- Aggregate lỗi → log error, KHÔNG fail check-out (staff có thể replay).

---

## SignalR realtime events — Tasks #7, #9, #13

Hub: `/hubs/lobby` (đã có cho Lobby). Các event mới:

### Groups

| Group | Format | Subscribe | Mục đích |
|-------|--------|-----------|----------|
| Booking | `booking-{bookingId}` | `JoinBookingGroup(bookingId)` | Nhận event `BookingCheckedIn`, `BookingCheckedOut`, `BookingCancelled`, `BookingNoShowMarked` |
| Cafe | `cafe-{cafeId}` | `JoinCafeGroup(cafeId)` | Nhận event `CafePricingChanged` |
| Lobby | `{lobbyId}` | `JoinLobby(lobbyId)` (đã có) | `LobbyAutoCancelled`, `LobbyTimeout` |

### Events

| Event | Trigger | Payload |
|-------|---------|---------|
| `BookingCheckedIn` | Sau `POST /api/cafes/{cafeId}/pos/check-in` (BR §21A.7) | `{ bookingId, checkedInAt, checkedInBy }` |
| `BookingCheckedOut` | Sau khi ActiveSession `PAID` (POS `CompleteAndCaptureAsync`) | `{ bookingId, checkedOutAt, totalAmount }` |
| `BookingCancelled` | Sau `DELETE /bookings/{id}` hoặc manager cancel | `{ bookingId, cancelledBy, reason, refundStatus }` |
| `BookingNoShowMarked` | Sau khi Staff check-out + aggregate votes | `{ bookingId, noShowMemberIds, karmaDeltas }` |
| `LobbyAutoCancelled` | `LobbyTimeoutJob` (BR-08) | `{ lobbyId, cafeId, cafeName, scheduledTime, reason }` |
| `LobbyTimeout` | (giữ nguyên) | `{ lobbyId, message }` |
| `CafePricingChanged` | `PUT /cafes/{id}/pricing-config` | `{ cafeId, cafeName, oldFirstHourPrice, newFirstHourPrice, effectiveDate, affectedBookingsCount }` |

### Mobile client join/leave

```javascript
// Join khi mở BookingDetailPage
await connection.invoke("JoinBookingGroup", bookingId);

// Leave khi đóng page
await connection.invoke("LeaveBookingGroup", bookingId);
```

**Tại sao thay polling:** Polling 5s có độ trễ; SignalR realtime giải quyết "single check-in" flow.