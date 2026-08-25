# Time-slot Reservation with Fixed End Time - FE-Facing Spec

| **Version:** 3.7 | Doc audit pass 10: BR-NEW-15 RemoveTimeSlotEnumRefactor — bỏ TimeSlot enum, dùng preferredStartTime/preferredEndTime trực tiếp. |
> **Created:** 2026-08-11 (v1.0) — revised 2026-08-12 (v2.0 FE-facing, v2.1 P0 DB-stored time, v2.2 Doc audit pass 2, v2.3 Doc audit pass 3, v2.4 Doc audit pass 4 entity/code alignment, v2.5 Reservation SoT clarification + §9.7 ownership table, v2.6 Phase 2/3 implemented, v2.7 Doc audit pass 5, v2.8 Doc audit pass 6 + BR-REFUND-04/05/06 fix + WalkInWindowCleanupJob, v2.9 Phase 0-3 closed, v3.0 Phase 4 completed: EC-10 warning, EC-11 dispute audit, RFC 8594 deprecation, missing DB indexes, v3.1 Phase 5 completed: BR-REFUND-08 late cancel after check-in endpoint + LateCancelRefundCalculator + 10 unit tests, v3.2 Phase 6 completed: EC-11 Manager override played time endpoint + ActiveSessionBillingCalculator + 12 unit tests, v3.3 Phase 7 completed: BR-NEW-10 cooling-off background job + admin endpoints + 27 unit tests, v3.4 Doc audit pass 7: sync §3.7 BR-KARMA-06/07/08/09/10, §7.1 +3 cooling-off EC (EC-12/13/14), §8.3 cooling-off mitigation, §10.5 +2 Phase 7 admin endpoints, §10.6 note cooling-off độc lập BookingController, §13.2 list 49 unit tests, §13.3 +4 cooling-off metrics, §14 glossary +7 terms), v3.6 Doc audit pass 9: Phase 8 — Risk Management + Admin Audit Log (PlayerAlert/PlayerRiskScore/RiskScoreHistory entities + 3 jobs + 7 admin endpoints + A-01/A-02 audit fix + PlayerActionHistory schema int-conversion fix), v3.7 2026-08-18: **BREAKING CHANGE** — BR-NEW-15 RemoveTimeSlotEnumRefactor: bỏ TimeSlot enum (Morning/Afternoon/Evening/LateNight), dùng preferredStartTime/preferredEndTime trực tiếp. CafeScheduleOverride dùng ApplyDate thay vì TimeSlot.) |
> **Status:** Active
> **Audience:** Frontend team + Mobile app integration
> **Related rules:** `lobby-booking-deposit-bvc.mdc` (canonical BR source — *tên rule file mang tính lịch sử, đề cập "booking" từ flow cũ, hiện dùng cho Reservation flow*), `sepay-payment-flow.mdc`

---

## BREAKING CHANGE — BR-NEW-15 (2026-08-18)

> ⚠️ **BREAKING CHANGE: Enum `TimeSlot` (Morning/Afternoon/Evening/LateNight) đã BỊ LOẠI BỎ.**

Hệ thống giờ bây giờ dùng **`preferredStartTime`** và **`preferredEndTime`** trực tiếp (TimeOnly) thay vì `TimeSlot` enum cố định. FE cũng như các service/backend cần cập nhật theo.

### Đã thay đổi:

| Trước | Sau |
|--------|------|
| `TimeSlot` enum (Morning/Afternoon/Evening/LateNight) | `preferredStartTime` + `preferredEndTime` (TimeOnly) |
| `CafeScheduleOverride.TimeSlot` | `CafeScheduleOverride.ApplyDate` (DateOnly) |
| `SeatInventory.TimeSlot` | `SeatInventory.ScheduledStartTime` + `ScheduledEndTime` (TimeOnly) |
| `GameInventory.TimeSlot` | `GameInventory.ScheduledStartTime` + `ScheduledEndTime` (TimeOnly) |

### Impact trên FE:

- API `/api/v1/reservations/quote` và `/api/v1/reservations/confirm` không còn nhận `timeSlot` field
- FE gửi trực tiếp `preferredStartTime` và `preferredEndTime` (HH:mm format)
- `scheduledStartTime` và `scheduledEndTime` được tính tự động từ input

---

## 0. QUAN TRỌNG CHO FE — ĐỌC TRƯỚC

### 0.1. Tại sao doc này sửa?

Doc phiên bản 1.0 dùng tên **`booking`** cho mọi flow, gây nhầm lẫn giữa:

- **`Booking` (entity cũ)** — vẫn live ở `BookingController.cs` (`/api/bookings/*`), phục vụ walk-in POS + SePay per-member deposit (BR-22 cũ). Đây là **legacy flow**, đang dần migrate sang Reservation.
- **`Reservation` (entity mới — HƯỚNG CHÍNH)** — `ReservationController.cs` (`/api/v1/reservations/*`), phục vụ online player app → Lobby → atomic BVC deposit (BR-NEW-01 → BR-REQUIRED §17.4). Đây là **flow chính của MVP+1**.

FE không cần biết chi tiết `Booking` cũ. Mọi flow online (player app tạo lobby, đặt cọc, cancel, extend) đều đi qua **`Reservation` + `Lobby`**.

### 0.2. Rule phân biệt nhanh

| FE muốn làm gì | Gọi API | Root entity |
|---|---|---|
| Player tạo phòng chờ online | `POST /api/v1/reservations/confirm` | `Reservation` (kèm `Lobby`) |
| Xem thông tin đặt chỗ | `GET /api/v1/reservations/{id}` | `Reservation` |
| Player scan QR POS check-in | `POST /api/check-in/scan-qr` | `ActiveSession` (link từ Reservation) |
| POS scan QR check-in cho khách | `POST /api/cafes/{cafeId}/pos/check-in` | `ActiveSession` (link từ Reservation hoặc Booking) |
| Host hủy đặt chỗ | `POST /api/v1/reservations/{id}/cancel` | `Reservation` |
| Player extend thêm giờ | `POST /api/v1/reservations/{id}/extend` | `Reservation` |
| POS tạo walk-in (khách vãng lai) | `POST /api/v1/reservations/walkin` | `WalkInBooking` |
| POS xem walk-in window đang trống | `GET /api/v1/reservations/walkin/windows` | `WalkInWindow` |

> Tất cả API walk-in có prefix `/api/v1/reservations/walkin/*` để gom nhóm với flow Reservation chính.

---

## 1. Tổng quan nghiệp vụ

### 1.1. Mô tả

**Time-slot Reservation với end time cố định** là mô hình đặt chỗ mà mỗi reservation phải có **thời gian bắt đầu** và **thời gian kết thúc** được xác định bởi user qua **`preferredStartTime`** và **`preferredEndTime`** (TimeOnly). Khi player về sớm (early checkout), hệ thống release ghế + tạo `WalkInWindow` cho phép nhóm khác (walk-in) đặt vào slot trống đó.

> ⚠️ **BREAKING CHANGE 2026-08-18:** Enum `TimeSlot` (Morning/Afternoon/Evening/LateNight) đã bị loại bỏ. Hệ thống giờ bây giờ dùng trực tiếp `preferredStartTime` + `preferredEndTime`. Xem [Breaking Change section](#breaking-change----br-new-15-2026-08-18) ở trên.

### 1.2. So sánh với mô hình cũ (mở)

| Khía cạnh | Mô hình cũ (Booking không end time) | Mô hình mới (Reservation có end time) |
|---|---|---|
| Đặt chỗ | Chỉ có start time | `playDate` + `timeSlot` (enum cố định) + lưu DB `ScheduledStartTime`/`ScheduledEndTime` |
| End time | User tự nhập `scheduleEndTime` | Auto-derived từ TimeSlot, **lưu DB** (`Reservation.ScheduledEndTime`) |
| Ghế release | Khi session kết thúc hoặc no-show | Khi: (1) On-time end, (2) Early checkout, (3) No-show |
| Walk-in | Không thể (ghế luôn bị hold) | Có thể — tạo walk-in vào `WalkInWindow` trống |
| Deposit | SePay per-member (BR-22 cũ) | BVC wallet (host trả toàn bộ, BR-DEPOSIT-01) |
| Root entity | `Booking` (legacy) | `Reservation` + `Lobby` (atomic) |
| Revenue | Không tối ưu | Tối ưu hơn |
| Complexity | Thấp | Cao |

### 1.3. Terminology mapping (bắt buộc đọc)

> **Ghi chú FE:** Cột "Tên trong doc 1.0 (cũ)" chỉ là **tham khảo lịch sử** — không phải API/field mà FE cần gọi. Cột "Tên thực tế trong code (mới)" là **canonical** — FE chỉ dùng cột này.

| Tên trong doc 1.0 (tham khảo lịch sử) | Tên thực tế trong code (canonical) | Entity / Field |
|---|---|---|
| `Booking` | `Reservation` | `BoardVerse.Core/Entities/Reservation.cs` |
| `Booking ID` | `Reservation.Id` (Guid) + `ReservationCode` (8-char alphanumeric uppercase) | `Reservation.Id`, `Reservation.ReservationCode` |
| `bookingId` trong URL | `reservationId` | Truyền vào `ReservationController` |
| `Start time` (user nhập) | `ScheduledStartTime` (DateTime, lưu DB) | `Reservation.ScheduledStartTime` (= `playDate + TimeSlot.startTime`) |
| `End time` (user nhập) | `ScheduledEndTime` (DateTime, lưu DB — qua đêm với `LateNight` slot) | `Reservation.ScheduledEndTime` (= `playDate + TimeSlot.endTime`) |
| `scheduledTime` (raw DateTime) | `Reservation.ScheduledStartTime` (BR-RESV-02 đã rename từ `ScheduledTime`) | `Reservation.ScheduledStartTime` |
| `Booking lobby` | `Lobby` luôn bound 1-1 với `Reservation` (Phase 1+). Legacy lobby có thể tồn tại với `ReservationId = null`. | `Lobby.ReservationId` (nullable cho legacy, required cho lobby mới) |
| `Booking lobby ID` | `Lobby.Id` (Guid) | `BoardVerse.Core/Entities/Lobby.cs` |
| Deposit (SePay) | **BVC hold** trong `Wallet.HeldBalance` | `BoardVerse.Core/Entities/Wallet.cs`, ledger `DEPOSIT_HOLD` |
| `Booking.status` enum | `ReservationStatus` enum + `LobbyStatus` enum + `ActiveSessionStatus` enum | `BoardVerse.Core/Enum/` |
| `Booking.GetByCodeAsync` (POS) | `ReservationService.GetByCodeAsync(code)` lookup bằng `ReservationCode` | `ReservationService` |
| `WalkInBooking` (entity riêng) | `WalkInBooking` (vẫn giữ) — link optional tới `WalkInWindow` | `BoardVerse.Core/Entities/WalkInBooking.cs`, `WalkInWindow.cs` |
| `WalkInWindow` | `WalkInWindow` (giữ nguyên) — tạo khi early checkout hoặc no-show | `BoardVerse.Core/Entities/WalkInWindow.cs` |
| `RefundTransaction` (VND amount) | Ledger entry `DEPOSIT_RELEASE` / `DEPOSIT_FORFEIT` (BVC amount) | `BoardVerse.Core/Entities/Transaction.cs` |
| `KarmaRecord` (append-only) | `KarmaShortPlayRecord` — được tạo tự động trong `ReservationService.CompleteAndCaptureAsync` | `BoardVerse.Core/Entities/KarmaShortPlayRecord.cs` |

### 1.4. Mục tiêu

1. **Tối đa hóa revenue** — Không lãng phí slot trống.
2. **Công bằng cho player** — Ai đặt trước được ưu tiên.
3. **Đơn giản hóa vận hành** — Staff có công cụ rõ ràng trên POS.
4. **Trải nghiệm tốt** — Player biết trước thời gian, không bị "đuổi" bất ngờ.

---

## 2. Các thành phần chính

### 2.1. Slot Structure — `TimeSlot` enum (BR-NEW-15 §7.1)

`TimeSlot` là enum **cố định 4 giá trị**, không cho phép cafe thêm khung giờ mới. Cafe chỉ override được tên hiển thị + start/end time qua `CafeScheduleOverride` table.

**4 khung giờ mặc định** (xem `BoardVerse.Core/Constants/CafeSchedule.cs`):

| `TimeSlot` | `StartTime` | `EndTime` | Mô tả |
|---|---|---|---|
| `Morning` | 06:00 | 12:00 | Phiên sáng |
| `Afternoon` | 12:00 | 17:00 | Phiên chiều |
| `Evening` | 17:00 | 23:00 | Phiên tối |
| `LateNight` | 23:00 | 06:00 (qua đêm, endTime = ngày hôm sau) | Phiên khuya qua đêm |

**State machine `Reservation` (link `Lobby` + `ActiveSession`):**

```
                 ┌─────────────────────┐
                 │  Reservation.Status  │
                 └─────────────────────┘
                           │
   ┌───────────────────┬───┴───┬─────────────────────────────┐
   ▼                   ▼       ▼                             ▼
Holding            Confirmed  CheckedIn                Completed
(lobby tuyển)        (đủ       (POS scan QR             (on-time end /
                      minPlayers)  → ActiveSession)        BR-REFUND-03)
   │                   │              │
   ├─ TimeoutFailed ───┤              ├── EarlyCheckout (WalkInWindow tạo, WindowEnd = ScheduledEndTime)
   ├─ HostCancelled ───┤              ├── NoShow (qua ScheduledStartTime + 30min grace)
   └─ RejectedByCafe ──┘              └── AutoReleased (grace period)
```

**State mapping với entities:**

| Doc state | `Reservation.Status` | `Lobby.Status` | `ActiveSession.Status` |
|---|---|---|---|
| `BOOKED` (chưa check-in) | `Holding` → `Confirmed` | `Open` → `Viable` (đủ minPlayers) | — |
| `CHECKED_IN` (đã đến) | `Confirmed` (đến `ScheduledStartTime`) | `Full`/`Viable` | `Active` |
| `IN_PROGRESS` (đang chơi) | (no change) | `InProgress` | `Active` |
| `COMPLETED` (đúng giờ) | `Completed` | `Closed` | `Paid` |
| `COMPLETED_AUTO_RELEASED` (quên end, grace 30p) | `Completed` + `EndReason=AutoReleased` | `Closed` (synced từ `AutoReleaseExpiredSessionsJob`) | `Closed` |
| `EARLY_CHECKOUT` (về sớm) | (no change) | (no change) | `Closed` (early) |
| `NO_SHOW` (không check-in) | `NoShow` | `TimeoutFailed` | — |
| `CANCELLED` (host hủy trước giờ) | `Cancelled` (kèm `CancelledByPlayer`/`ByCafe`) | `HostCancelled`/`RejectedByCafe`/`ExpiredByCafe` | — |

### 2.2. Walk-in Window

Khi một session kết thúc sớm (early checkout) hoặc không có ai check-in (no-show), hệ thống tự động tạo một **`WalkInWindow`** cho phép walk-in đặt vào slot trống đó.

```
Timeline:

Nhóm A đặt 13:00 - 18:00 (Reservation timeSlot = Afternoon)
  │
  │ Check-in 13:00
  ▼
┌───────────────────────────────────────────────────────────────┐
│                                                               │
│ 13:00          15:00                    18:00                 │
│    │              │                        │                   │
│    ▼              ▼                        ▼                   │
│  Start ───────────┤ Early Checkout         │ End               │
│                    │                        │                   │
│                    └────────────────────────┘                  │
│                           │                                    │
│                           ▼                                    │
│              Walk-in Window: 15:00 - 18:00                   │
│              (3 tiếng trống, có thể bán cho walk-in)         │
│                                                               │
└───────────────────────────────────────────────────────────────┘
```

**Entity:** `BoardVerse.Core/Entities/WalkInWindow.cs`
**Window status:** `Available` → `Partial` (1+ walk-in đã vào) → `Full` → `Expired` (khi đến `ScheduledEndTime`).
**WindowEnd source:** `WalkInWindow.WindowEnd = Reservation.ScheduledEndTime` (BR-RESV-02 — lưu DB, không cần derive runtime từ TimeSlot).
**Walk-in booking:** `BoardVerse.Core/Entities/WalkInBooking.cs` — link `walkInWindowId`, có `guestName`, `guestPhone`, `seats`, `paymentStatus`.

### 2.3. BVC — Đơn vị tiền (BR §II)

| Field | Value |
|---|---|
| Tên | **BVC** (BoardVerse Coin) |
| Tỷ lệ | **1 BVC = 1.000 VND** (cố định toàn hệ thống) |
| Refund | Hoàn cọc trả về ví BVC của host (không refund VND) |
| Min deposit | Theo `BR-NEW-01`: tính theo khoảng cách `playDate` (50k/100k/150k/200k BVC) |

---

## 3. Business Rules (canonical từ `lobby-booking-deposit-bvc.mdc`)

> Các BR dưới đây là **đã chốt với giảng viên (02/08/2026)**. Mọi thay đổi phải cập nhật đồng thời `lobby-booking-deposit-bvc.mdc` (rule file) + file markdown gốc. Implementation thực tế dùng entity/field trong cột "Map sang code".

### 3.1. BR-RESV — Reservation Rules (thay BR-BOOK cũ)

| ID | Rule | Mô tả | Map sang code |
|---|---|---|---|
| `BR-RESV-01` | `playDate + TimeSlot.startTime ≥ Now + 30 min` | Tối thiểu 30 phút buffer cho player chuẩn bị | `ReservationService.QuoteAsync` validate |
| `BR-RESV-02` | `ScheduledStartTime`/`ScheduledEndTime` = `playDate + TimeSlot.startTime/endTime` (auto-resolve, **lưu DB**) | Enum cố định 4 khung giờ | `ReservationService.BuildScheduledStartEnd(playDate, startTime, endTime)` |
| `BR-RESV-03` | Reservation phải unique theo `(cafeId, playDate, timeSlot, hostId)` | Mỗi host 1 reservation active/khung giờ | `ReservationService.ConfirmAsync` atomic check |
| `BR-RESV-04` | Deposit = `max(minDeposit(khoảng cách playDate), ratePerPerson × maxPlayers × riskMultiplier)` | BR-NEW-01 + §IV | `DepositCalculator.CalculateAsync` |
| `BR-RESV-05` | Host active 1 lobby + member active 1 lobby = tối đa 2 lobby active | BR-USER-LIMIT-01 | `LobbyService.JoinLobbyAsync` validate cross-role |
| `BR-RESV-06` | `ConfirmAsync` atomic transaction | Validate quote + seat + game copy + user eligibility trong 1 transaction | `ReservationService.ConfirmAsync` |
| `BR-RESV-07` | RecuitmentDeadline = `ScheduledStartTime - leadTimeMinutes` (mặc định 20 phút) | BR-LOBBY-01 | `Reservation.RecruitmentDeadline` |

> **Note BR-RESV-02:** `ScheduledStartTime` và `ScheduledEndTime` được resolve 1 lần khi `ReservationService.CreateQuoteAsync` chạy (xem helper `BuildScheduledStartEnd` xử lý slot `LateNight` qua đêm), sau đó **lưu DB**. Mọi query/calc (background jobs, playedRatio, refund policy, WalkInWindow) đọc từ DB — không derive runtime từ TimeSlot. DBTIMESTAMP `timestamp with time zone`.

> **LƯU Ý:** Doc 1.0 có `BR-BOOK-05` (max 2 active bookings/user) — đã **BỎ** vì áp BR-USER-LIMIT-01 trong `lobby-booking-deposit-bvc.mdc §IX.1`. Doc 1.0 có `BR-BOOK-02` (end time ≤ start + 6h) — đã **BỎ** vì end time derive tự động từ `TimeSlot` (max ~7 tiếng cho `LateNight`).

### 3.2. BR-CHECKIN — Check-in Rules

| ID | Rule | Mô tả | Map sang code |
|---|---|---|---|
| `BR-CHECKIN-01` | Check-in cho phép trong `[ScheduledStartTime - 15 min, ScheduledEndTime + 30 min]` | Grace -15 phút trước `ScheduledStartTime`, +30 phút sau `ScheduledEndTime` | `CafePosController.CheckIn` validate thời gian (qua `PlayerCheckInService.ValidateCheckInTimeWindowAsync`) |
| `BR-CHECKIN-02` | Sau 30 phút sau `ScheduledStartTime` không check-in → tự động `NoShow` | `ReservationNoShowDetectionJob` chạy mỗi 5 phút, query `Reservation.ScheduledStartTime < now - 30min AND Status = Confirmed` (dùng `IX_Reservations_ScheduledStartTime_Status`) | `BackgroundServices/ReservationNoShowDetectionJob.cs` |
| `BR-CHECKIN-03` | `ActiveSession.StartedAt` = thời điểm scan QR thực tế | Ghi lại để tính `playedRatio` | `ActiveSession.StartedAt` set bởi POS |

### 3.3. BR-END — End Session Rules

| ID | Rule | Mô tả | Map sang code |
|---|---|---|---|
| `BR-END-01` | `ActiveSession.EndedAt` = thời điểm POS bấm End thực tế | Có thể sớm / đúng / trễ hơn `ScheduledEndTime` | `CafePosController.EndSession` set field |
| `BR-END-02` | `playedRatio = (EndedAt - StartedAt) / (ScheduledEndTime - ScheduledStartTime)` | Decimal `[0.0, ~1.0+]` | `ReservationService.CompleteAndCaptureAsync` |
| `BR-END-03` | `playedRatio ≥ 0.9` → Coi như **on-time** → forfeit 100% deposit | BR-REFUND-06 → 0% BVC refund | `ReservationService` |
| `BR-END-04` | `playedRatio > 1.0` (trễ hơn scheduled) | Charge thêm theo hourly rate + check slot liền kề | `CafePosController.EndSession` charge extra |
| `BR-END-05` | Grace period 30 phút sau `ScheduledEndTime` | Không tính extra trong 30 phút đầu | `AutoReleaseExpiredSessionsJob` auto-release session → reservation → **sync lobby → Closed** (idempotent atomic flip) |

### 3.4. BR-REFUND — Refund Rules

> Tất cả rules dưới đây áp dụng cho `Reservation.CompleteAndCaptureAsync` / `ReservationService.CancelAsync`. Currency = **BVC**, không phải VND.

**Nguyên tắc cốt lõi:** Sử dụng `playedRatio` để xác định refund cho early checkout, KHÔNG dùng milestone thời gian (24h/6h) cho phần này.

`playedRatio = (EndedAt - StartedAt) / (ScheduledEndTime - ScheduledStartTime)`

| ID | Điều kiện | Hoàn BVC | BVC ghi nhận |
|---|---|---|---|
| `BR-REFUND-01` | Timeout (lobby fail) | **100%** về `Wallet.availableBalance` | Ledger `DEPOSIT_RELEASE` |
| `BR-REFUND-02` | Host cancel by player | Xem bảng bên dưới | Tùy thời điểm hủy |
| `BR-REFUND-03` | No-show (grace 30 phút) | **0%** | `DEPOSIT_FORFEIT` về `Wallet.forfeitTotal` |
| `BR-REFUND-04` | Early checkout, `playedRatio < 0.5` | **0%** | `DEPOSIT_FORFEIT` |
| `BR-REFUND-05` | Early checkout, `playedRatio ≥ 0.5` | **30%** về `Wallet.availableBalance` | `DEPOSIT_RELEASE` (30%) + `DEPOSIT_FORFEIT` (70%) |
| `BR-REFUND-06` | Early checkout, `playedRatio ≥ 0.9` (treated as on-time) | **0%** | `DEPOSIT_FORFEIT` |
| `BR-REFUND-07` | Staff override đặc biệt | Theo `Manager`/`Admin` decision → ghi log + audit `PlayerActionHistory` | Đặc biệt — cần supervisor approve |

**BR-REFUND-02 — Cancel by Player:**

| Thời điểm hủy | Hoàn BVC |
|----------------|----------|
| Grace 15 phút đầu + chưa có member | 100% |
| ≥ 24h trước `ScheduledStartTime` | 100% |
| < 24h trước `ScheduledStartTime` | 0% |

> **Lưu ý:** BR-REFUND-04/05/06 áp dụng cho **early checkout** (đã check-in rồi về sớm), không áp dụng cho cancel trước khi check-in.

**Ledger mapping:**

```
DEPOSIT_HOLD        → heldBalance += amount, availableBalance -= amount
DEPOSIT_RELEASE     → heldBalance -= amount, availableBalance += amount   (BR-REFUND-01/02/05)
DEPOSIT_FORFEIT     → heldBalance -= amount, forfeit += amount           (BR-REFUND-02/03/04/06)
DEPOSIT_CAPTURE     → heldBalance -= amount, settlement += amount        (khi ActiveSession PAID → BR-REVENUE-01)
```

### 3.5. BR-EXT — Extension Rules (chỉ áp dụng cho Reservation)

> Endpoint: `POST /api/v1/reservations/{id}/extend` (extension mới, cần implement `ReservationExtensionService`). Doc mô tả flow; chưa có controller live. Phase tiếp theo: implement + tests.

| ID | Rule | Mô tả | Map sang code |
|---|---|---|---|
| `BR-EXT-01` | Trước extend, check slot kế còn trống | `(cafeId, playDate + 1, nextTimeSlot)` availability | `SeatInventory.GetAvailableAsync` |
| `BR-EXT-02` | Không conflict với Reservation khác | Query `ReservationRepository` overlap | `IReservationRepository` query |
| `BR-EXT-03` | Max extension = 2 tiếng tổng | 13:00 + 2h = **15:00** (không phải 16:00 — lỗi typo cũ) | `ReservationExtensionService` |
| `BR-EXT-04` | Extension phải trả thêm BVC | Charge qua `Wallet.HeldBalance` hold mới + capture khi complete | `WalletService.HoldAsync` |
| `BR-EXT-05` | Partial extension OK | Vd: extend 30 phút (không bắt buộc full slot) | `ReservationExtensionService` cho phép `extensionMinutes` |

### 3.6. BR-WALKIN — Walk-in Rules (qua Reservation walk-in endpoint)

> Walk-in KHÔNG qua `Booking` cũ nữa — dùng `POST /api/v1/reservations/walkin` (POS-only). Refund 100% nếu nhường chỗ cho booking mới (EC-04).

| ID | Rule | Mô tả | Map sang code |
|---|---|---|---|
| `BR-WALKIN-01` | Chỉ tạo walk-in vào `WalkInWindow` còn `Available` / `Partial` | `WalkInWindow.Status ∈ {Available, Partial}` | `WalkInService.CreateWalkInAsync` |
| `BR-WALKIN-02` | Walk-in Window ≥ 30 phút mới cho phép tạo walk-in | Check `(windowEnd - windowStart) ≥ 30 min` | Service validate |
| `BR-WALKIN-03` | **POS-only** — staff role Manager/CafeStaff mới tạo được | `[Authorize(Roles = "Manager,CafeStaff")]` | `WalkInController` |
| `BR-WALKIN-04` | Walk-in KHÔNG CỌC | Payment 100% tiền giờ tại POS qua SePay session | `WalkInBooking.PaymentStatus` chỉ UNPAID → PAID |
| `BR-WALKIN-05` | First-come-first-served | `WalkInWindow.HeldSeats` update atomic | `WalkInWindowRepository` OCC version |
| `BR-WALKIN-06` | POS cancel walk-in OK | Manager/CafeStaff hủy trước khi check-in | `WalkInController.Cancel` |

### 3.7. BR-KARMA — Karma System Rules (BR-RISK chính)

| ID | Rule | Mô tả | Map sang code |
|---|---|---|---|
| `BR-KARMA-01` | Track `shortPlay` cho Reservation có `playedRatio < 0.5` | Tạo `KarmaShortPlayRecord` (link `ReservationId`) | `ReservationService.CompleteAndCaptureAsync` |
| `BR-KARMA-02` | Sau 3 short-plays trong 30 ngày → Warning notification | Trigger `riskLevel` lên `Warning` | `KarmaAnalyticsController` |
| `BR-KARMA-03` | Sau 5 short-plays → Restriction: chỉ cho đặt slot ≤ 3 tiếng | `riskLevel = High`, cấm `reservation.timeSlot ∈ {Afternoon}` (5 tiếng) | `Wallet.RiskLevel` check |
| `BR-KARMA-04` | Reset sau 30 ngày không vi phạm | Background job `signal_detect_multi_account` (running rảnh mỗi 6h) | `PlayerRiskScoreEntity` |
| `BR-KARMA-05` | Player appeal qua support ticket | `POST /api/v1/karma/appeal` | `KarmaAnalyticsController.Appeal` |
| `BR-KARMA-06` | **BR-NEW-10** — 3 lobby `TimeoutFailed`/`HostCancelled` trong 7 ngày → Cooling-off 30 ngày | `Wallet.IsCoolingOff = true`, `RiskMultiplier ×2`; user chỉ được tạo lobby `playDate = today` | `CoolingOffService.DetectAndActivateAsync` (Phase 7) + `CoolingOffJob` background |
| `BR-KARMA-07` | **BR-NEW-10** — Tổng forfeit/no-show > 500 BVC (= 500.000 VND) trong 30 ngày → Cooling-off 30 ngày | Cùng penalty với BR-KARMA-06 | `CoolingOffService.DetectAndActivateAsync` (Phase 7) — check `SumForfeitAsync` với threshold `500L` (đã sửa 2026-08-12 từ 500.000 BVC) |
| `BR-KARMA-08` | **BR-NEW-10** — Tiếp tục fail trong cooling-off → Escalate 30 ngày + `RiskMultiplier ×3` | `Wallet.RiskMultiplier = max(current, 3.0)`, `CoolingOffExpiresAt = now + 30d` | `CoolingOffService.EscalateAsync` (Phase 7) |
| `BR-KARMA-09` | **BR-RISK-09** — User chỉ thấy `riskLevel`, KHÔNG thấy `riskScore` chi tiết + signals | `GET /api/v1/karma/me` trả `{ riskLevel, shortPlayCount, restrictions }` (không có `riskScore`) | Karma controller filter fields |
| `BR-KARMA-10` | **BR-RISK-04** — `AccountStatus` 5 trạng thái: `active` < `warning` < `restricted` < `suspended` < `banned` | Validation tại API layer | `Wallet.AccountStatus` enum check |

---

## 4. Luồng nghiệp vụ chi tiết (FE-facing)

### 4.1. Luồng tạo Reservation + Lobby atomic (Flow chính)

```
┌─────────────────────────────────────────────────────────────┐
│         RESERVATION + LOBBY CREATION FLOW                  │
└─────────────────────────────────────────────────────────────┘

Player (App)                                System (Backend)
  │                                              │
  │  1. Config lobby config (game, cafe,       │
  │     playDate, timeSlot, maxPlayers,        │
  │     preferredStartTime optional)           │
  │  ─────────────────────────────────────────▶ │
  │                                              │
  │                                              │ 2. Validate quote:
  │                                              │    - User eligible (BR-USER-LIMIT-01/04)
  │                                              │    - No overlap (BR-USER-LIMIT-02)
  │                                              │    - Buffer ≥ 120 min (BR-LOBBY-01)
  │                                              │    - playDate ≤ 7 ngày
  │                                              │    - DepositCalculator → finalDeposit
  │                                              │
  │  3. ReservationQuote response              ◀─┤
  │     (expiresAt = now + 5 min)               │
  │                                              │
  │  4. Nếu currentBalance < finalDeposit:      │
  │     → Mở TopUpPage (BVC wallet)             │
  │     → Sau khi top-up, quote còn hạn         │
  │  ─────────────────────────────────────────▶ │
  │                                              │
  │  5. Nhấn "Xác nhận"                         │
  │     POST /api/v1/reservations/confirm       │
  │     body { cafeId, gameId, playDate,         │
  │            timeSlot, preferredStartTime?,    │
  │            minPlayers, maxPlayers,           │
  │            isPrivate, expectedFinalDeposit,  │
  │            idempotencyKey }                  │
  │  ─────────────────────────────────────────▶ │
  │                                              │
  │                                              │ 6. ATOMIC TRANSACTION:
  │                                              │    a. Validate lại quote (chưa hết hạn)
  │                                              │    b. Validate user eligibility
  │                                              │    c. Lock seat (SELECT FOR UPDATE)
  │                                              │    d. Lock game copy
  │                                              │    e. UPDATE Wallet: avail -= X, held += X
  │                                              │    f. INSERT Ledger DEPOSIT_HOLD
  │                                              │    g. INSERT Reservation (status=Holding)
  │                                              │    h. INSERT Lobby (status=pendingActivation
  │                                              │         hoặc pendingCafeApproval if playDate > 2 days)
  │                                              │    i. UPDATE Reservation.LobbyId = lobby.Id
  │                                              │    j. INSERT Outbox event LobbyActivated
  │                                              │    k. COMMIT
  │                                              │
  │                                              │ 7. Worker publish outbox → SignalR LobbyActivated
  │                                              │
│  8. Reservation + Lobby response           ◀─┤
  │     { reservationId, reservationCode,      │
  │       lobbyId, lobbyStatus,                │
  │       scheduledStartTime, scheduledEndTime,│
  │       timeSlot, deadline, depositAmount }  │
  │                                              │
  ```

### 4.2. Luồng Check-in

```
┌─────────────────────────────────────────────────────────────┐
│                       CHECK-IN FLOW                          │
└─────────────────────────────────────────────────────────────┘

Player                                           POS Staff
  │                                                  │
  │  1. Đến quán, mở app, mở Reservation detail     │
  │     GET /api/v1/reservations/{id}                │
  │  ◀──────────────────────────────────────────────── │
  │                                                  │
  │  2. Hiển thị ReservationCode (8 char)            │
  │     + QR cho staff scan                          │
  │                                                  │
  │                                                  │ 3. Staff bấm "Check-in", nhập code
  │                                                  │    hoặc quét QR
  │                                                  │    POST /api/cafes/{cafeId}/pos/check-in
  │                                                  │    body { code: "ABCD1234" }
  │                                                  │  ─────────────────────────────────────▶
  │                                                  │              │
  │                                                  │              │ 4. Validate:
  │                                                  │              │    - Code exists (Reservation or Booking)
  │                                                  │              │    - Status = Holding/Confirmed (not Cancelled)
  │                                                  │              │    - now ∈ [ScheduledStartTime - 15min, ScheduledEndTime + 30min]
  │                                                  │              │    - Cafe matches
  │                                                  │              │
  │                                                  │              │ 5. UPDATE:
  │                                                  │              │    - Reservation.Status = Confirmed → wait (sẽ set khi start session)
  │                                                  │              │    - Lobby.Status = InProgress
  │                                                  │              │    - ActiveSession.Status = Active
  │                                                  │              │    - ActiveSession.StartedAt = now
  │                                                  │              │    - Bind SessionGame → barcode
  │                                                  │              │
  │  6. SignalR "ActiveSessionStarted"            ◀──┤
  │  ─────────────────────────────────────────     │
  │                                                  │
```

> **Lưu ý BR-CHECKIN-01:** POS chỉ cho check-in trong `[-15 min, +30 min]` quanh `ScheduledStartTime`/`ScheduledEndTime` (grace 15 phút trước `ScheduledStartTime` + 30 phút sau `ScheduledEndTime`). Quá 30 phút sau `ScheduledStartTime` → auto `NoShow` bởi `NoShowDetectionJob`.

### 4.3. Luồng End Session bình thường (On-time)

```
POS Staff
  │
  │  Đúng giờ (18:00) — nhóm mang game ra
  │
  │  POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/end
  │  body { actualEndTime: "2026-08-12T18:00:00Z",
  │         endReason: "ON_TIME" }
  │
  │                 ┌──────────────────────────────────────┐
  │                 │ Backend: ReservationService         │
  │                 │         .CompleteAndCaptureAsync     │
  │                 │                                      │
  │                 │ 1. Tính playedRatio                  │
  │                 │    playedDuration = EndTime -        │
  │                 │      Reservation.ScheduledStartTime  │
  │                 │    scheduledDuration =               │
  │                 │      Reservation.ScheduledEndTime -  │
  │                 │      Reservation.ScheduledStartTime  │
  │                 │    playedRatio =                     │
  │                 │      playedDuration / scheduledDur.  │
  │                 │    (BR-RESV-02: 2 field lưu DB      │
  │                 │     trong `Reservations` table)      │
  │                 │                                      │
  │                 │ 2. BR-REFUND-06 (on-time ≥0.9)      │
  │                 │    → Refund 0% BVC                  │
  │                 │                                      │
  │                 │ 3. UPDATE                           │
  │                 │    - ActiveSession.Status = Paid    │
  │                 │    - ActiveSession.EndedAt = now    │
  │                 │    - Reservation.Status = Completed │
  │                 │                                      │
  │                 │ 4. Ledger:                          │
  │                 │    DEPOSIT_FORFEIT (100% BVC)       │
  │                 │    DEPOSIT_CAPTURE (sang settlement)│
  │                 │                                      │
  │                 │ 5. Short-play check:                │
  │                 │    Nếu đủ điều kiện →              │
  │                 │      INSERT KarmaShortPlayRecord    │
  │                 │      (logic BR-RISK-01)              │
  │                 │                                      │
  │                 │ 6. SignalR events to lobby members  │
  │                 └──────────────────────────────────────┘
  │
  │  Response: bill + Karma rating prompt
```

### 4.4. Luồng End Session sớm (Early Checkout)

```
POS Staff
  │
  │  15:00 — Player yêu cầu về sớm (đặt 13:00-18:00, slot Afternoon)
  │
  │  POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/end
  │  body { actualEndTime: "15:00", endReason: "EARLY_LEAVE" }
  │
  │  Backend xử lý:
  │    1. playedDuration = EndTime - Reservation.ScheduledStartTime
  │       scheduledDuration = Reservation.ScheduledEndTime
  │                          - Reservation.ScheduledStartTime
  │       playedRatio = playedDuration / scheduledDuration
  │                  = (15:00 - 13:00) / 5h = 40%
  │    2. BR-REFUND-04 (ratio < 50%) → Refund 0% BVC
  │    3. UPDATE ActiveSession.Status = Closed (early)
  │    4. Walk-in Window tạo: now → Reservation.ScheduledEndTime
  │       (vd 15:00-18:00, 3h trống, 4 ghế)
  │       BR-RESV-02: WalkInWindow.WindowEnd lưu Reservation.ScheduledEndTime,
  │       không cần derive runtime.
  │    5. Ledger: DEPOSIT_FORFEIT
  │    6. KarmaShortPlayRecord nếu scheduledDuration ≥ 4h (BR-KARMA-01)
  │
  │  Response:
  │    { bill: <hour rate × 2h>,
  │      refund: { eligible: false, percentage: 0 },
  │      walkInWindow: {
  │        id: "<guid>",
  │        windowStart: "15:00",
  │        windowEnd: "18:00",
  │        availableSeats: 4,
  │        status: "Available"
  │      }
  │    }
```

> App hiển thị message cho player: "Bạn đã chơi 2h/5h (40%). Deposit không được refund do chơi dưới 50% thời gian. Vui lòng thanh toán 60.000đ."

### 4.5. Luồng Extend (BR-EXT — extension flow mới)

> ✅ **Phase 3 (W5-6) đã implement:** `ReservationExtensionService` + endpoints `GET/POST /api/v1/reservations/{id}/extend/availability`.

```
Player (App)                                 System
  │                                              │
  │  1. Mở Reservation detail, nhấn "Extend"    │
  │     POST /api/v1/reservations/{id}/extend    │
  │     body { extensionMinutes: 60 }            │
  │  ─────────────────────────────────────────▶ │
  │                                              │
  │                                              │ 2. BR-EXT-01:
  │                                              │    Query (cafeId, playDate, TimeSlot+1)
  │                                              │    → Available seats?
  │                                              │
  │                                              │ 3. BR-EXT-02:
  │                                              │    Query Reservation overlap
  │                                              │
  │                                              │ 4. Nếu có slot trống (BR-EXT-01 OK):
  │                                              │    Tính extraCharge = rate × extensionMinutes
  │                                              │    Wallet.Hold (heldBalance += charge)
  │                                              │    UPDATE Reservation.ScheduledEndTime
  │                                              │    UPDATE Reservation.ExtensionCount +1
  │                                              │
  │  5. Response:                                ◀┤
  │    { available: true,                       │
  │      extraCharge: 12000,  // 12 BVC        │
  │      newScheduledEndTime: "2026-08-12T19:00:00Z" }
  │
  │  Nếu KHÔNG có slot trống:
  │  Response 409:
  │    { available: false, reason: "SLOT_CONFLICT",
  │      suggestions: ["Cannot extend at this time"] }
```

### 4.6. Luồng Walk-in (qua Reservation walk-in endpoint)

> Mọi walk-in đều qua `WalkInController` mới, không dùng `Booking` cũ.

```
POS Staff
  │
  │  GET /api/v1/reservations/walkin/windows
  │      ?cafeId=<>&date=2026-08-12
  │
  │  ◀── response { windows: [
  │       { id: "<guid>", windowStart: "15:00",
  │         windowEnd: "18:00", availableSeats: 4,
  │         sourceReservation: "Nhóm A (về sớm)" },
  │       ...
  │     ] }
  │
  │  POST /api/v1/reservations/walkin
  │     body {
  │       walkInWindowId: "<guid>",
  │       guestName: "Nguyễn Văn B",
  │       guestPhone: "0901234567",
  │       seats: 3,
  │       paymentMethod: "SEPAY"
  │     }
  │
  │                 ┌──────────────────────────────────────┐
  │                 │ Backend validate:                   │
  │                 │  - BR-WALKIN-02: window ≥ 30 min?  │
  │                 │  - BR-WALKIN-05: còn ghế?          │
  │                 │                                      │
  │                 │ OCC: UPDATE WalkInWindow.HeldSeats  │
  │                 │         WHERE availableSeats >= X    │
  │                 │         AND version = expected      │
  │                 │                                      │
  │                 │ → Tạo WalkInBooking + ActiveSession │
  │                 └──────────────────────────────────────┘
  │
  │  Response 201:
  │    { walkInBookingId: "<guid>",
  │      status: "Active",
  │      totalAmount: 60000,
  │      seatsRemaining: 1 }
  │
  │  Walk-in ngồi chơi, kết thúc → POS bấm End như thường
```

### 4.7. Luồng No-Show (background)

```
Time = Reservation.ScheduledStartTime + 30 min
       │
       ▼
ReservationNoShowDetectionJob (background, chạy mỗi 5 phút)
       │
       │  Query: Reservation WHERE
       │         Status = Confirmed
       │         AND ScheduledStartTime + 30min <= now
       │  (BR-RESV-02: dùng index IX_Reservations_ScheduledStartTime_Status,
       │   không cần derive runtime từ PlayDate + TimeSlot)
       │
       │  Với mỗi reservation:
       │    1. UPDATE Reservation.Status = NoShow
       │    2. UPDATE Lobby.Status = TimeoutFailed
       │    3. Release seat + game copy
       │    4. CREATE WalkInWindow (BR-WALKIN-01, WindowEnd = ScheduledEndTime)
       │    5. Ledger: DEPOSIT_FORFEIT (BR-REFUND-03)
       │    6. SignalR "ReservationNoShow" cho host
       │    7. Send push notification
       │
       └─── Host nhận notification
            "Reservation ABCD1234 bị hủy do không check-in.
             Bạn đã mất X BVC. Ghế đã được release."
```

### 4.8. Luồng Cancel (Host chủ động)

```
Player (App)
  │
  │  POST /api/v1/reservations/{id}/cancel
  │  body { reason: "Có việc đột xuất" }
  │
  │                 ┌──────────────────────────────────────┐
  │                 │ Backend (ReservationService.Cancel): │
  │                 │                                      │
  │                 │ 1. Tính refund theo BR-REFUND-01/02: │
  │                 │    if (ScheduledStartTime - now >= 24h): │
  │                 │        refundPercent = 100%         │
  │                 │    elif (>= 6h): refund = 50%       │
  │                 │    else: refund = 0%                 │
  │                 │                                      │
  │                 │ 2. UPDATE Reservation.Status =       │
  │                 │    Cancelled (CancelledByPlayer)    │
  │                 │                                      │
  │                 │ 3. UPDATE Lobby.Status =             │
  │                 │    HostCancelled                     │
  │                 │                                      │
  │                 │ 4. Release seat + game copy          │
  │                 │                                      │
  │                 │ 5. Ledger:                           │
  │                 │    DEPOSIT_RELEASE (phần refund)     │
  │                 │    DEPOSIT_FORFEIT (phần forfeit)   │
  │                 │                                      │
  │                 │ 6. SignalR "LobbyCancelled"          │
  │                 └──────────────────────────────────────┘
  │
  │  Response 200:
  │    { reservationId, status: "Cancelled",
  │      refund: { eligible: <bool>, amount: <BVC>,
  │                percentage: <0/30/50/100>,
  │                reason: "CANCEL_BEFORE_24H" } }
```

---

## 5. Ưu điểm

### 5.1. Ưu điểm cho Quán (Cafe)

1. **Tối đa hóa revenue** — Ghế được release ngay khi player về.
2. **Predictable planning** — Biết trước slot nào có khách.
3. **Giảm wasted capacity** — Không còn "ghế treo".
4. **Better walk-in opportunity** — Có thể sell slot trống cho walk-in.
5. **Inventory management** — Biết trước cần game nào, bao nhiêu bàn.
6. **Staffing optimization** — Biết giờ nào đông/vắng.

### 5.2. Ưu điểm cho Player

1. **Bảo đảm có chỗ** — Đặt trước → chắc chắn có ghế.
2. **Fair hơn** — Ai đặt trước được ưu tiên.
3. **Refund công bằng** — Đã chơi ≥ 50% → được refund 30%.
4. **Rõ ràng thời gian** — Biết trước check-in, check-out.
5. **Extension possible** — Có thể extend nếu slot kế trống (BR-EXT).
6. **Karma warning** — Biết trước nếu bị karma flag.

### 5.3. Ưu điểm cho Hệ thống

1. **Clean data model** — Mỗi reservation có `playDate + timeSlot` rõ ràng.
2. **Easy reporting** — Biết chính xác utilization rate.
3. **Automated workflows** — Auto no-show (`NoShowDetectionJob`), auto release (`AutoReleaseExpiredSessionsJob`), lobby recruitment timeout (`LobbyTimeoutJob`), auto refund (`ReservationDeadlineJob`).
4. **Scalable** — Dễ mở rộng thêm features (extension, walk-in window).

---

## 6. Nhược điểm

### 6.1. Nhược điểm cho Quán

1. **Phức tạp hơn** — Phải quản lý nhiều luồng.
2. **Staff training** — Nhân viên phải học cách xử lý nhiều case.
3. **Edge cases** — Nhiều trường hợp đặc biệt cần xử lý.
4. **Conflict management** — Phải xử lý dispute khi extension bị từ chối.

### 6.2. Nhược điểm cho Player

1. **Phải commit thời gian** — TimeSlot là enum cố định, không "chơi đến khi nào vui thì thôi".
2. **Không spontaneous** — Phải đặt trước, không thể đến ngẫu nhiên (ngoại trừ walk-in).
3. **Pressure khi hết giờ** — Bị "nhắc nhở" khi sắp hết TimeSlot.
4. **Deposit complexity** — Phải hiểu BR-REFUND rules.

### 6.3. Nhược điểm cho Hệ thống

1. **Technical complexity** — 4 entity (`Reservation` + `Lobby` + `WalkInWindow` + `WalkInBooking`).
2. **Migration risk** — Phải migrate entity cũ `Booking` → entity mới `Reservation` (Phase 1 — đang thực hiện).
3. **Race conditions** — 2 POS cùng tạo walk-in → OCC cho `WalkInWindow`.

---

## 7. Các trường hợp ngoại lệ và xử lý

### 7.1. Tổng quan Edge Cases

| # | Edge Case | Xử lý | Ai xử lý |
|---|-----------|--------|----------|
| 1 | Extension conflict | Từ chối + gợi ý partial extension | `ReservationExtensionService` |
| 2 | Refund dispute | Override by Manager/Admin | `ReservationService` + audit `PlayerActionHistory` |
| 3 | Karma abuse | Warning → Restriction | `KarmaAnalyticsController` + `risk_score_recompute` job |
| 4 | Walk-in vs Reservation conflict | Reservation luôn ưu tiên (BR-REFUND-01 cho walk-in) | `WalkInService` |
| 5 | Extension thay đổi `ScheduledEndTime` interplay | Reject extend nếu overlap WalkInWindow | `ReservationExtensionService` + `WalkInWindowRepository` |
| 6 | Race condition (2 POS tạo walk-in) | OCC trên `WalkInWindow.Version` | `WalkInWindowRepository` |
| 7 | Cancel after check-in | `playedRatio < 0.5` → forfeit 100% (BR-REFUND-04) | `ReservationService` |
| 8 | Extension qua midnight (non-LateNight) | Chỉ cho extend trong cùng `playDate` | `ReservationExtensionService` reject |
| 9 | Staff forgot to end | Auto-release sau grace 30p (`AutoReleaseExpiredSessionsJob`) | Background job |
| 10 | Game longer than TimeSlot | Suggest extension / early checkout | POS hiển thị warning |
| 11 | Player disputes played time | Staff judgment + audit log | POS staff |
| 12 | **Cooling-off** (BR-NEW-10) — user đang trong cooling-off tạo lobby `playDate > 1 ngày` | Từ chối tạo lobby, throw `InCoolingOffCannotCreateFutureLobby` | `EligibilityValidator` + `ReservationService.ConfirmAsync` (Phase 7) |
| 13 | **Cooling-off expiry race** — `ExpireOverdueAsync` chạy cùng lúc với rescue deposit | Background job dùng serializable transaction; risk nhỏ vì job chỉ quota 30 phút/lần | `CoolingOffService.ExpireOverdueAsync` (Phase 7) |
| 14 | **Cooling-off escalate overlap** — user fail trong cooling-off nhưng `EscalateAsync` được gọi đồng thời với admin extend | `EscalateAsync` ghi `Wallet.RiskMultiplier = max(current, 3.0)`; admin extend chỉ đụng `CoolingOffExpiresAt` — 2 path không conflict | `CoolingOffService` (Phase 7) — `max()` semantic |

### 7.2. Chi tiết từng Edge Case (giữ logic từ doc 1.0, map sang entity mới)

---

#### EC-01: Extension Conflict (BR-EXT-01/02)

**Mô tả:** Player muốn extend nhưng `TimeSlot` kế đã có reservation.

**Timeline:**
```
Slot Afternoon (13:00-18:00) - Nhóm A
Slot Evening (18:00-24:00) - Nhóm B (đã đặt)
                   ↑
                   A muốn extend đến đây
```

**Xử lý:**
1. `ReservationExtensionService.CheckAvailabilityAsync` → query next TimeSlot còn seat trống.
2. Nếu full → trả `available: false, reason: "SLOT_CONFLICT"`.
3. Service gợi ý: "Bạn không thể extend đến TimeSlot Evening. Vui lòng kết thúc đúng giờ 18:00."

> **Lưu ý:** Doc 1.0 có "partial extension" và "early extension" options — trong MVP+1 chỉ hỗ trợ **từ chối** (strict). Phase sau sẽ bổ sung partial extension.

---

#### EC-02: Refund Dispute (BR-REFUND-07 staff override)

**Mô tả:** Player không đồng ý với refund amount theo BR-REFUND-04/05/06.

**Xử lý:**
1. Manager/Admin truy cập `POST /api/v1/admin/reservations/{id}/override-refund`.
2. Required: `overrideAmountBvc`, `reason`, `evidence`.
3. Ghi `PlayerActionHistory` (actionType = `RefundOverride`, actionBy = admin userId).
4. `Wallet`: `DEPOSIT_RELEASE` với amount override.
5. SignalR notification cho player.

> Staff chỉ override trong trường hợp đặc biệt (technical issue, emergency). Supervisor approval nếu > 50 BVC.

---

#### EC-03: Karma Abuse (BR-KARMA + BR-RISK-01)

**Mô tả:** Player liên tục đặt TimeSlot dài (Afternoon 5h) nhưng chơi ít.

**Xử lý:**

| Level | `KarmaShortPlayRecord` count trong 30 ngày | Hành vi |
|---|---|---|
| Normal | 0-2 | Track im lặng |
| Warning | 3-4 | Thông báo lúc tạo reservation quote |
| Restricted | 5+ | Không cho chọn `Afternoon` / `Evening` (slot ≥ 5h) |

**Tracking trigger:** `ReservationService.CompleteAndCaptureAsync` chèn `KarmaShortPlayRecord` khi `scheduledDuration ≥ 4h` AND `playedRatio < 0.5` AND `actualEndTime < scheduledEndTime`.

---

#### EC-04: Walk-in vs Reservation Conflict

**Mô tả:** Walk-in đang ngồi, Reservation mới đến check-in.

**Priority rule:** **RESERVATION LUÔN ƯU TIÊN HƠN WALK-IN.**

**Xử lý:**
1. POS detect: Walk-in đang trong `WalkInWindow` + Reservation mới đến `ScheduledStartTime`.
2. Walk-in giữ ghế đến khi Reservation mới đến check-in.
3. Nếu Reservation đến:
 - Có ghế khác → staff di chuyển walk-in.
 - Không có ghế → staff thông báo walk-in phải ra trong 15 phút + refund 100% tiền giờ cho walk-in (staff pay cash).

---

#### EC-05: Extension thay đổi `ScheduledEndTime` (BR-EXT interplay)

**Mô tả:** Khi player extend session, `Reservation.ScheduledEndTime` được UPDATE mở rộng ra sau. Điều này ảnh hưởng:

1. **`WalkInWindow.WindowEnd`** đã tạo từ early checkout của Reservation khác — nếu extension kéo dài quá `WalkInWindow.WindowEnd` thì overlap với walk-in đang ngồi.
2. **`NoShowDetectionJob`** filter `ScheduledStartTime + 30min < now` — không ảnh hưởng (extend chỉ đổi `ScheduledEndTime`).
3. **`playedRatio`** tính lại với `ScheduledEndTime` mới.

**Xử lý:**
1. `ReservationExtensionService.ExtendAsync` validate (BR-EXT-01/02): query overlap `(cafeId, newEndTime, nextTimeSlot)`.
2. Nếu có `WalkInWindow` overlap → reject extension, trả `409 Conflict` + reason `WALKIN_WINDOW_CONFLICT`.
3. UPDATE atomic: `Reservation.ScheduledEndTime`, `ExtensionCount++`, ledger `DEPOSIT_HOLD` cho extra charge.
4. SignalR broadcast `ReservationExtended` cho cả nhóm.

> **Lưu ý:** `WalkInWindowCleanupJob` (`§4.4`) filter `WindowEnd <= now` — không query `Reservation.ScheduledEndTime` nên KHÔNG bị miss khi extension thay đổi. Job dựa trên `WalkInWindow.WindowEnd` (lưu lúc tạo), tách biệt khỏi Reservation.

---

#### EC-06: Race Condition (OCC trên WalkInWindow)

**Mô tả:** 2 POS cùng bấm "Add Walk-in" → oversell.

**Xử lý:** **Optimistic Concurrency Control** trên `WalkInWindow.Version`:

```sql
-- 1. POS gửi request với expectedVersion
-- 2. UPDATE WalkInWindow
UPDATE "WalkInWindows"
SET "HeldSeats" = "HeldSeats" + @requested,
    "Version" = "Version" + 1,
    "UpdatedAt" = NOW()
WHERE "Id" = @windowId
  AND "AvailableSeats" >= @requested
  AND "Version" = @expectedVersion;

-- Rows affected = 0 → conflict → trả 409 cho POS
```

**Implementation:** `WalkInWindowRepository.TryHoldSeatsAsync` chạy trong transaction.

---

#### EC-07: Cancel After Check-in

**Mô tả:** Player đã check-in rồi, muốn cancel.

**Xử lý:**
- Tính `playedRatio = (now - ActiveSession.StartedAt) / scheduledDuration`.
- Áp BR-REFUND-04/05/06.
- Staff explanation: "Bạn đã chơi Xh/Yh (Z%) → Refund 0%/30%/0% deposit."

---

#### EC-08: Extension qua Midnight

**Mô tả:** Reservation LateNight (23:00-06:00) muốn extend sang ngày mai (sau 06:00).

**Xử lý:** `ReservationExtensionService` từ chối (cross-day extension không hỗ trợ MVP+1). Response: "Không thể extend qua ngày. Vui lòng tạo Reservation mới cho ngày mai."

---

#### EC-09: Staff Forgot to End Session

**Mô tả:** Player đã về nhưng POS quên bấm End.

**Xử lý:**
- `AutoReleaseExpiredSessionsJob` quét mỗi 5 phút.
- Nếu `ActiveSession.Status = Active` AND `now > Reservation.ScheduledEndTime + 30 min`:
 - Auto set `ActiveSession.Status = Closed` (auto-released) — atomic flip qua `TryUpdateStatusAsync` (status chỉ update khi đang `Active`, idempotent)
 - Set `ActiveSession.ActualEndAt = ScheduledEndTime` (grace period)
 - Set `Reservation.Status = Completed` + `EndReason = AutoReleased` + `PlayedRatio = 1.0m`
 - **Sync `Lobby.Status = Closed`** + `ClosedAt = now` + deactivate members (`IsActive=false`, `Status=LobbyTerminated`) — fix 2026-08-24 để tránh inconsistent state (lobby hiển thị `InProgress` khi reservation đã `Completed`)
 - Tạo `WalkInWindow` cho phần thời gian còn lại
 - Log audit "STAFF_FORGOT_END"
 - Notification staff

---

#### EC-10: Game Longer Than TimeSlot

**Mô tả:** Game đang chơi dở (Catan 60-120 phút) nhưng gần hết TimeSlot.

**Xử lý:**
1. POS show warning khi `minutesUntilScheduledEnd < gameEstimatedRemainingMinutes`.
2. Options:
 - **Quick finish** (nếu game cho phép skip) — không charge extra.
 - **Extend** (nếu slot kế trống + BR-EXT-03 OK) — charge extra.
 - **Continue + grace** — cho thêm 30 phút không charge.

**Implementation (Phase 4 ✅):**
- Backend: `ReservationTimeOverrunHelper.Compute(scheduledEndTimeUtc, estimatedRemainingMinutes)`
  → trả `(TimeOverrunWarning, TimeSlotRemainingMinutes)`.
- `ActiveSessionDto` + `ActiveSessionResponseDto` có field `TimeOverrunWarning` + `TimeSlotRemainingMinutes`.
- POS UI render banner "Game còn dở — TimeSlot sắp hết (X phút). Hãy Extend hoặc End sớm" khi flag = true.

---

#### EC-11: Player Disputes Played Time

**Mô tả:** Player nói đã đến sớm hơn / về muộn hơn POS ghi.

**Xử lý:**
1. POS logs luôn lưu `StartedAt` (scan QR timestamp) + `EndedAt` (POS button timestamp).
2. POS logs là evidence definitive.
3. Staff giải thích + giải quyết theo POS logs.
4. Nếu player escalate → Manager review + có thể override (BR-REFUND-07).

**Implementation (Phase 4 ✅):**
- Endpoint: `POST /api/cafes/{cafeId}/pos/sessions/dispute-played-time` (Role: Manager, CafeStaff).
- Body: `DisputePlayedTimeRequestDto { sessionId, disputeType, playerClaim, proposedResolution? }`.
- Response: `DisputePlayedTimeResponseDto` chứa `auditId`, `sessionStartedAt`, `sessionEndedAt`, `sessionTotalMinutes` (POS evidence).
- Audit log: ghi vào `PlayerActionHistory` với `ActionType=PlayedTimeDisputed (=40)`, `UserId=session.HostId`, `ActionBy=staffId`, `Metadata=JSON` chứa timestamps POS + claim + resolution.

**Manager Override (Phase 5 ✅ — BR-REFUND-07):**

Quy trình end-to-end:
1. Staff mở dispute (Phase 4) → lưu audit `PlayedTimeDisputed (=40)`.
2. Manager review POS evidence (`sessionStartedAt` scan QR + `sessionEndedAt` POS button).
3. Manager override bằng cách set `NewTotalMinutesPlayed` mới qua endpoint mới.
4. Service recalc `Subtotal` (theo BillingModel: FlatEntry → BasePrice, TimeBased → BasePrice + ⌈(new - 60) / block⌉ × blockRate) + `TotalAmount = Subtotal + Penalty`.
5. Audit log: `ActionType=PlayedTimeOverridden (=41)`, `ActionBy=managerId`, `Metadata` chứa `{previousTotalMinutes, newTotalMinutes, previousSubtotal, newSubtotal, subtotalDelta, linkedDisputeAuditId, overrideReason}`.

**Endpoint:** `POST /api/cafes/{cafeId}/pos/sessions/override-played-time` (Role: **Manager only**).

**Body:** `OverridePlayedTimeRequestDto { SessionId, NewTotalMinutesPlayed (0..1440), OverrideReason (≥ 20 chars) }`.

**Response:** `OverridePlayedTimeResponseDto { OverrideAuditId, SessionId, DisputeAuditId, PreviousTotalMinutes, NewTotalMinutes, PreviousSubtotal, NewSubtotal, SubtotalDelta, NewTotalAmount, PolicyApplied: "BR-REFUND-07 ManagerOverride", Status: "Overridden", OverriddenAt }`.

**Điều kiện:**
- Manager chỉ (Staff → 403 `OnlyManagerCanOverride`).
- Session chưa Paid/Closed (409 `CannotOverridePaidSession`).
- Phải có ít nhất 1 dispute audit trước (409 `NoDisputeBeforeOverride`).
- `NewTotalMinutesPlayed ∈ [0, 1440]` (24 giờ — default policy max).

**Service:** `CafePosService.OverridePlayedTimeAsync` (BoardVerse.Services/Services/CafePosService.cs).
- Tìm `PlayerActionHistory` gần nhất với `ActionType=PlayedTimeDisputed` cho session (linked audit).
- Recalc Subtotal theo tỉ lệ: `member.NewMinutes = member.PreviousMinutes × NewTotal / PreviousTotal` (scale proportionally).
- Edge case: PreviousTotal = 0 → chia đều NewTotal cho paying members.
- Save audit + update session trong 1 transaction.

**Helper:** `ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, elapsedMinutes)` (BoardVerse.Core/Helpers/) — pure math, 12 unit tests. Refactored từ `ActiveSessionService.CalculateRealtimeBilling` để share giữa `ActiveSessionService` + `CafePosService`.

**Tests:** 12 unit tests trong `BoardVerse.Tests/Helpers/ActiveSessionBillingCalculatorTests.cs`:
- TimeBased ≤ 60 min → BasePrice (giờ đầu).
- TimeBased > 60 min → BasePrice + ⌈(elapsed - 60) / block⌉ × blockRate.
- FlatEntry → BasePrice cho mọi duration.
- Defensive: TieredBlockRate null/zero → fallback BasePrice.
- TieredBlockMinutes = 0 → fallback 30 minutes.
- Edge cases: elapsed = 0 / negative → 0.
- Long session (480 min = 8h) cho Manager override scenario.

---

## 8. Rủi ro

### 8.1. Rủi ro kỹ thuật

| # | Rủi ro | Xác suất | Tác động | Mitigation |
|---|--------|----------|----------|------------|
| 1 | Race condition (oversell walk-in) | Trung bình | Cao | OCC trên `WalkInWindow.Version` |
| 2 | Migration failure Booking → Reservation | Thấp | Rất cao | ETL job + backup (chưa làm) |
| 3 | Data inconsistency giữa Reservation + Lobby + ActiveSession | Thấp | Cao | Atomic transaction ở `ConfirmAsync` |
| 4 | Performance degradation | Thấp | Trung bình | Index `IX_Reservations_CafeId_PlayDate_TimeSlot_Status` + `IX_Reservations_ScheduledStartTime_Status` + `IX_Reservations_ScheduledEndTime_Status` |
| 5 | Extension charge wallet fail | Thấp | Trung bình | `WalletService.HoldAsync` retry |

### 8.2. Rủi ro vận hành

| # | Rủi ro | Xác suất | Tác động | Mitigation |
|---|--------|----------|----------|------------|
| 1 | Staff không training đủ | Cao | Cao | Training program + docs |
| 2 | Staff forget to end | Trung bình | Trung bình | `AutoReleaseExpiredSessionsJob` auto-release |
| 3 | Staff override abuse | Thấp | Cao | Audit log + approval |
| 4 | Customer complaint spike | Trung bình | Trung bình | Clear policy + UI explanation |
| 5 | Conflict between customers | Trung bình | Cao | BR-REFUND + walk-in priority rule |

### 8.3. Rủi ro kinh doanh

| # | Rủi ro | Xác suất | Tác động | Mitigation |
|---|--------|----------|----------|------------|
| 1 | Player chuyển sang competitor | Trung bình | Cao | Good UX + fair rules (BR-REFUND công bằng) |
| 2 | Revenue không tăng như kỳ vợng | Trung bình | Trung bình | Realistic targets + monitoring |
| 3 | Abuse / Exploitation | Thấp | Trung bình | Karma system + risk score + **BR-NEW-10 cooling-off job** (Phase 7) auto-activate + `CoolingOffService.EscalateAsync` cho repeat offenders |
| 4 | Negative reviews | Trung bình | Cao | Good service + quick response |

---

## 9. Data Models (canonical từ code)

> Toàn bộ entity thực tế ở `BoardVerse.Core/Entities/`. Doc 1.0 có entity giả định (pseudo-Dart) — đã BỎ.

### 9.1. `Reservation` (ROOT entity — SOURCE OF TRUTH)

> ⚠️ **Tất cả time / scheduling / cọc fields bắt buộc ở Reservation.** Lobby chỉ mirror 1 số field cho index/query nhanh — xem §9.7 để biết ownership table chi tiết và lý do tại sao `Lobby` KHÔNG nên có `ScheduledEndTime`.

```csharp
public class Reservation
{
    public Guid Id { get; set; }                       // = field cũ "bookingId" trong doc Booking v1.0
    public Guid HostId { get; set; }                   // BR-DEPOSIT-01: host trả cọc — SoT cho Lobby.HostUserId
    public Guid CafeId { get; set; }                   // SoT cho Lobby.CafeId
    public Guid GameId { get; set; }                   // SoT cho Lobby.GameTemplateId
    public DateOnly PlayDate { get; set; }             // = BR-NEW-04 — SoT cho Lobby.PlayDate
    public TimeSlot TimeSlot { get; set; }             // = BR-NEW-15 enum — SoT cho Lobby.TimeSlot
    public TimeOnly? PreferredStartTime { get; set; }  // optional, BR-NEW-15b — SoT cho Lobby.PreferredStartTime

    // BR-RESV-02: scheduledStartTime + scheduledEndTime lưu DB lúc ConfirmAsync.
    // = playDate + TimeSlot.startTime / endTime (qua đêm với slot LateNight).
    // WalkInWindowCleanupJob (§4.4), playedRatio (§4.3), extension flow (Phase 3)
    // query trực tiếp từ DB, không cần derive runtime.
    // CHỈ CÓ Ở RESERVATION — Lobby KHÔNG có ScheduledEndTime.
    public DateTime ScheduledStartTime { get; set; }
    public DateTime ScheduledEndTime { get; set; }     // ⭐ Source of truth cho WalkInWindow.WindowEnd, playedRatio, no-show grace

    public DateTime RecruitmentDeadline { get; set; }  // = ScheduledStartTime - leadTimeMinutes — SoT cho Lobby.RecruitmentDeadline
    public int MinPlayers { get; set; }                // SoT cho Lobby.MinPlayers
    public int MaxPlayers { get; set; }                // SoT cho Lobby.MaxMembers

    public long DepositAmount { get; set; }            // BVC — SoT cho Lobby.MinDeposit
    public long MinDepositApplied { get; set; }        // BVC, BR-NEW-01 — chỉ Reservation
    public decimal RiskMultiplier { get; set; }        // chỉ Reservation
    public DepositSnapshot DepositConfigSnapshot { get; set; } // BR-21F.9 audit — SoT cho Lobby.DepositSnapshot
    public int CurrentPlayers { get; set; } = 1;       // Mirror từ Lobby (true source). Scheduler đọc nhanh.
    public ReservationStatus Status { get; set; }      // enum: Draft(0,transient)/Holding(1)/Confirmed(2)/Expired(3)/CheckedIn(4)/Completed(5)/CancelledByPlayer(6)/CancelledByCafe(7)/NoShow(8)
    public string ReservationCode { get; set; }        // 8-char alphanumeric, POS scan — chỉ Reservation (Lobby có ShareCode riêng cho invite)
    public Guid? LobbyId { get; set; }                 // FK Lobby, set sau ConfirmAsync
    public Guid? SeatInventoryId { get; set; }         // chỉ Reservation
    public Guid? GameInventoryId { get; set; }         // chỉ Reservation
    public int ExtensionCount { get; set; } = 0;      // Phase 3: số lần gia hạn (BR-EXT-01: max 3)
    public DateTime? ExtendedEndTime { get; set; }     // Phase 3: lưu giờ kết thúc gốc trước khi extend (nếu có)
    public string IdempotencyKey { get; set; }         // chỉ Reservation (BR-XVII.1)
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

**File:** `BoardVerse.Core/Entities/Reservation.cs`

**Index (P0):**
- `IX_Reservations_ScheduledEndTime_Status` (`ScheduledEndTime`, `Status`) — dùng cho `WalkInWindowCleanupJob` (`§4.4`) + auto-release job.
- `IX_Reservations_ScheduledStartTime_Status` (`ScheduledStartTime`, `Status`) — dùng cho `NoShowDetectionJob` (`§4.7`).

**Migration note (P0):** EF migration tự sinh `AddColumn ScheduledStartTime/ScheduledEndTime + AddIndex` (xem `BoardVerse.Data/Migrations/`). Nếu có data cũ thiếu 2 field → script backfill: `ScheduledStartTime = PlayDate + TimeSlot.startTime; ScheduledEndTime = PlayDate + TimeSlot.endTime (+ 1 day nếu LateNight qua đêm)`.

### 9.2. `Lobby` (MIRROR entity — chỉ giữ field time cho index/query nhanh)

> ⚠️ **Lobby KHÔNG phải source of truth cho time/scheduling.** Tất cả time fields trên Lobby (`PlayDate?`, `ScheduledStartTime?`, `RecruitmentDeadline?`) là **mirror** copy từ `Reservation` lúc `ConfirmAsync`, dùng cho:
> 1. Index queries nhanh (`IX_Lobbies_PlayDate`, `IX_Lobbies_ScheduledStartTime`, `IX_Lobbies_RecruitmentDeadline`) mà không cần JOIN Reservation.
> 2. Backward compat với legacy lobby tạo trước khi có Reservation.
>
> Lobby **KHÔNG có `ScheduledEndTime`** — vì walk-in flow, no-show job, playedRatio đều query từ `Reservation.ScheduledEndTime`.

```csharp
public class Lobby
{
    public Guid Id { get; set; }
    public Guid HostUserId { get; set; }                       // mirror Reservation.HostId
    public Guid GameTemplateId { get; set; }                   // mirror Reservation.GameId
    public Guid? CafeId { get; set; }                          // mirror Reservation.CafeId — nullable vì legacy lobby không có Reservation
    public Guid? ReservationId { get; set; }                   // FK Reservation — bắt buộc với lobby mới
    public Guid? BookingId { get; set; }                       // FK cũ (legacy, sẽ bỏ ở Phase 4)

    // MIRROR từ Reservation (nullable cho legacy lobby):
    public DateOnly? PlayDate { get; set; }                    // mirror Reservation.PlayDate
    public TimeSlot? TimeSlot { get; set; }                    // mirror Reservation.TimeSlot — có thể drop Phase 2+ nếu không còn legacy lobby
    public TimeOnly? PreferredStartTime { get; set; }          // mirror Reservation.PreferredStartTime
    public DateTime? ScheduledStartTime { get; set; }          // mirror Reservation.ScheduledStartTime — index IX_Lobbies_ScheduledStartTime
    public DateTime? RecruitmentDeadline { get; set; }         // mirror Reservation.RecruitmentDeadline — index IX_Lobbies_RecruitmentDeadline

    public int MaxMembers { get; set; }                        // mirror Reservation.MaxPlayers
    public int MinPlayers { get; set; } = 2;                   // mirror Reservation.MinPlayers

    // Lobby-SPECIFIC (state machine, UI, lifecycle):
    public long? MinDeposit { get; set; }                      // mirror Reservation.DepositAmount
    public DepositSnapshot? DepositSnapshot { get; set; }      // mirror Reservation.DepositConfigSnapshot
    public int? MinKarmaScore { get; set; }                    // BR-10 filter
    public int? SeatCount { get; set; }                        // BR-07
    public int CancellationLeadTimeMinutes { get; set; } = 30; // BR-08
    public double? Latitude { get; set; }                      // search
    public double? Longitude { get; set; }                     // search
    public bool IsPrivate { get; set; }                        // BR-LOBBY-PRIVACY-01
    public string ShareCode { get; set; }                      // BR-LOBBY-PRIVACY-02 (8 char)
    public string? Description { get; set; }                   // UI
    public string? CoverImageUrl { get; set; }                 // UI
    public Guid? ActiveSessionId { get; set; }                 // FK ActiveSession
    public LobbyStatus Status { get; set; }                    // enum 12 states (state machine riêng)
    public DateTime? CafeApprovalDeadline { get; set; }        // BR-NEW-11
    public Guid? CafeApprovedByUserId { get; set; }            // BR-NEW-11
    public DateTime? CafeApprovedAt { get; set; }              // BR-NEW-11
    public string? CafeRejectionReason { get; set; }           // BR-NEW-11
    public DateTime? RatingOpenedAt { get; set; }              // post-session Karma
    public DateTime? ClosedAt { get; set; }                    // terminal audit
    public string? ClosedReason { get; set; }                  // terminal audit
    public DateTime? FullAt { get; set; }                      // BR-LOBBY-READY-03
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<LobbyMember> Members { get; set; } = [];   // chỉ Lobby
    public virtual ICollection<LobbyInvite> Invites { get; set; } = [];   // chỉ Lobby
    public virtual ICollection<LobbyMessage> Messages { get; set; } = []; // chỉ Lobby
}
```

**File:** `BoardVerse.Core/Entities/Lobby.cs`

**Index (giữ nguyên cho query nhanh):**
- `IX_Lobbies_PlayDate` (single col)
- `IX_Lobbies_RecruitmentDeadline` (single col)
- `IX_Lobbies_ScheduledStartTime` (single col)
- `IX_Lobbies_ReservationId` (FK index)
- `IX_Lobbies_(IsPrivate, Status, ScheduledStartTime)` (composite cho search)

### 9.3. `WalkInWindow`

```csharp
public class WalkInWindow
{
    public Guid Id { get; set; }
    public Guid? SourceReservationId { get; set; }  // FK Reservation tạo window
    public Guid CafeId { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public int TotalSeats { get; set; }
    public int AvailableSeats { get; set; }
    public int HeldSeats { get; set; }
    public int InUseSeats { get; set; }
    public int Version { get; set; }              // OCC
    public WalkInWindowStatus Status { get; set; } // Available/Partial/Full/Expired/Closed
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
```

**File:** `BoardVerse.Core/Entities/WalkInWindow.cs`

### 9.4. `WalkInBooking`

```csharp
public class WalkInBooking
{
    public Guid Id { get; set; }
    public Guid WalkInWindowId { get; set; }
    public Guid CafeId { get; set; }
    public string GuestName { get; set; }
    public string? GuestPhone { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int Seats { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentStatus { get; set; }      // UNPAID/PAID
    public Guid? PosStaffId { get; set; }
    public Guid? ActiveSessionId { get; set; }
    public WalkInBookingStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**File:** `BoardVerse.Core/Entities/WalkInBooking.cs`

### 9.5. BVC Ledger entries (BR §III.2)

```csharp
public class BvcLedgerEntry   // Append-only ledger entry — tên thực tế trong code (KHÔNG dùng TransactionEntity cũ)
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public LedgerEntryType Type { get; set; }      // TOP_UP, DEPOSIT_HOLD, DEPOSIT_RELEASE, DEPOSIT_CAPTURE, DEPOSIT_FORFEIT, ADJUSTMENT
    public long Amount { get; set; }               // BVC, always positive

    /// <summary>Reservation liên kết (BR §III.2). Dùng cho Phase 1+ sau migrate Booking → Reservation.</summary>
    public Guid? RelatedReservationId { get; set; }
    /// <summary>Legacy: Booking liên kết. Deprecated — sẽ bỏ sau khi migrate xong.</summary>
    [Obsolete("Use RelatedReservationId — Booking entity đang migrate sang Reservation (Phase 1).")]
    public Guid? RelatedBookingId { get; set; }

    public Guid? RelatedLobbyId { get; set; }
    public string? RelatedPaymentRef { get; set; }
    public string IdempotencyKey { get; set; }
    public long BalanceSnapshot { get; set; }      // availableBalance after txn
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**File:** `BoardVerse.Core/Entities/BvcLedgerEntry.cs`

**Note:** Entity thực tế là `BvcLedgerEntry` (KHÔNG phải `Transaction` — `Transaction` entity riêng dùng cho payment gateway VND). Doc này mô tả ledger cho BVC; `Transaction` entity xem `docs/api/sepay-payment-flow.md`.

**TD-02 follow-up:** Thêm cột `RelatedReservationId?` vào `BvcLedgerEntry` qua EF migration `AddReservationIdToBvcLedger`. Backfill: cho records cũ có `RelatedBookingId`, set `RelatedReservationId = (SELECT Id FROM Reservations WHERE OldBookingId = BvcLedgerEntry.RelatedBookingId)`. Xem `.cursor/rules/booking-vs-reservation.mdc` để biết thêm.
```

**File:** `BoardVerse.Core/Entities/Transaction.cs`

### 9.6. `KarmaShortPlayRecord`

```csharp
public class KarmaShortPlayRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }               // host hoặc player vi phạm
    public Guid ReservationId { get; set; }
    public int ScheduledMinutes { get; set; }       // từ Reservation.ScheduledEndTime - ScheduledStartTime
    public int PlayedMinutes { get; set; }           // từ ActiveSessionMember.TotalMinutesPlayed
    public decimal PlayedRatio { get; set; }        // playedMinutes / scheduledMinutes
    public int KarmaDelta { get; set; }             // điểm bị trừ (default -5 khi ratio < 0.5)
    public decimal KarmaPointsAdded { get; set; }   // điểm cộng vào ví
    public int TotalKarmaScore { get; set; }        // tổng karma sau khi record
    public KarmaRecordStatus Status { get; set; }   // ACTIVE/EXPIRED/CLEARED
    public DateTime CreatedAt { get; set; }
}
```

**File:** `BoardVerse.Core/Entities/KarmaShortPlayRecord.cs`

### 9.7. Data Ownership & Sync Rules (Reservation ↔ Lobby)

> Mục đích: tránh duplicate field sync drift giữa `Reservation` (root) và `Lobby` (mirror). Phase 1 giữ mirror cho backward compat; Phase 2+ dọn dẹp.

#### 9.7.1. Ownership table

| Field | Reservation (SoT) | Lobby (mirror) | Ghi chú |
|---|---|---|---|
| `Id` | ✅ | ✅ | PK của mỗi entity |
| `HostId` / `HostUserId` | ✅ required | ✅ required (mirror) | Phase 2+: drop Lobby.HostUserId, JOIN Reservation |
| `CafeId` | ✅ required | ✅ nullable (mirror) | Legacy lobby không có Reservation → nullable |
| `GameId` / `GameTemplateId` | ✅ required | ✅ required (mirror) | |
| `PlayDate` | ✅ required | ✅ nullable (mirror) | Index `IX_Lobbies_PlayDate` cần giữ |
| `TimeSlot` | ✅ required | ✅ nullable (mirror) | Phase 2+: drop nếu không còn legacy |
| `PreferredStartTime` | ✅ optional | ✅ optional (mirror) | Phase 2+: drop |
| **`ScheduledStartTime`** | ✅ **required (SoT)** | ✅ nullable (mirror) | Index `IX_Lobbies_ScheduledStartTime` cần giữ |
| **`ScheduledEndTime`** | ✅ **required (SoT ONLY)** | ❌ **không có** | WalkInWindow.WindowEnd, playedRatio, no-show grace đều query từ Reservation |
| `RecruitmentDeadline` | ✅ required | ✅ nullable (mirror) | Index `IX_Lobbies_RecruitmentDeadline` cần giữ |
| `MinPlayers` | ✅ required | ✅ required (mirror, default 2) | |
| `MaxPlayers` / `MaxMembers` | ✅ required | ✅ required (mirror) | |
| `DepositAmount` / `MinDeposit` | ✅ required | ✅ nullable (mirror) | |
| `DepositConfigSnapshot` / `DepositSnapshot` | ✅ required | ✅ nullable (mirror) | |
| `RiskMultiplier` | ✅ required | ❌ không có | Chỉ Reservation |
| `MinDepositApplied` | ✅ required | ❌ không có | Chỉ Reservation |
| `ReservationCode` | ✅ required | ❌ không có | Chỉ Reservation (POS scan) |
| `IdempotencyKey` | ✅ required | ❌ không có | Chỉ Reservation |
| `SeatInventoryId` / `GameInventoryId` | ✅ nullable | ❌ không có | Chỉ Reservation |
| `CurrentPlayers` | ✅ required (mirror) | ✅ required (true source) | Reservation copy từ Lobby, scheduler đọc nhanh |
| `Status` | ✅ enum riêng (9 values) | ✅ enum riêng (12 values) | State machine riêng |
| `ShareCode` | ❌ không có | ✅ required | Chỉ Lobby (invite) |
| `IsPrivate` | ❌ không có | ✅ required | Chỉ Lobby |
| `CafeApproval*` (Deadline/ByUser/At/RejectionReason) | ❌ không có | ✅ nullable | Chỉ Lobby (BR-NEW-11) |
| `Members` (collection) | ❌ không có | ✅ collection | Chỉ Lobby |
| `ActiveSessionId` | ❌ không có | ✅ nullable | Chỉ Lobby (FK) |
| `Latitude/Longitude` | ❌ không có | ✅ nullable | Chỉ Lobby (search) |
| `SeatCount` | ❌ không có | ✅ nullable | Chỉ Lobby (BR-07) |
| `CancellationLeadTimeMinutes` | ❌ không có | ✅ required | Chỉ Lobby (BR-08) |
| `MinKarmaScore` | ❌ không có | ✅ nullable | Chỉ Lobby (BR-10) |
| `Description/CoverImageUrl` | ❌ không có | ✅ nullable | Chỉ Lobby (UI) |
| `RatingOpenedAt/ClosedAt/ClosedReason/FullAt` | ❌ không có | ✅ nullable | Chỉ Lobby (audit lifecycle) |
| `BookingId` (legacy FK) | ❌ không có | ✅ nullable | Chỉ Lobby (legacy, sẽ bỏ Phase 4) |

#### 9.7.2. Sync rules (khi ConfirmAsync)

`ReservationService.ConfirmAsync` chịu trách nhiệm copy các mirror fields từ Reservation → Lobby trong cùng transaction:

```csharp
// ConfirmAsync line 582-604 (BoardVerse.Services/Services/ReservationService.cs)
var lobby = new Lobby
{
    HostUserId = hostId,                          // ← mirror Reservation.HostId
    GameTemplateId = request.GameId,              // ← mirror Reservation.GameId
    CafeId = request.CafeId,                      // ← mirror Reservation.CafeId
    ReservationId = reservation.Id,               // ← FK
    PlayDate = request.PlayDate,                  // ← mirror
    TimeSlot = request.TimeSlot,                  // ← mirror
    PreferredStartTime = request.PreferredStartTime, // ← mirror
    ScheduledStartTime = scheduledStartTime,      // ← mirror
    RecruitmentDeadline = recruitmentDeadline,    // ← mirror
    MaxMembers = quote.MaxPlayersApplied,         // ← mirror
    MinPlayers = request.MinPlayers,              // ← mirror
    MinDeposit = quote.FinalDeposit,              // ← mirror
    DepositSnapshot = depositSnapshot,            // ← mirror
    // KHÔNG set ScheduledEndTime — Lobby không có field này.
};
```

#### 9.7.3. Query rules (FE/backend phải tuân thủ)

| Use case | Query từ | Lý do |
|---|---|---|
| Check-in window | `Reservation` | Cần ScheduledStartTime + ScheduledEndTime |
| WalkInWindow tạo khi early checkout | `Reservation` | Cần ScheduledEndTime |
| No-show grace 30 phút | `Reservation` | Cần ScheduledStartTime + status |
| `playedRatio` tính toán | `Reservation` | Cần cả Start + End |
| Lobby recruitment deadline check | Cả 2 | Lobby index nhanh cho background job |
| Lobby search/filter theo time | `Lobby` | Index `IX_Lobbies_PlayDate/ScheduledStartTime` |
| Background jobs (`LobbyNotificationJob`, `LobbyAtRiskWarningJob`) | `Lobby` | Index đã có sẵn, không JOIN |
| Background jobs (`ReservationNoShowDetectionJob`, `WalkInWindowCleanupJob`) | `Reservation` | Cần EndTime, không có ở Lobby |

#### 9.7.4. Future cleanup (Phase 2+)

Khi migrate xong legacy lobby (không còn Reservation=null), có thể drop các field pure duplicate ở Lobby:
- `CafeId?` (chỉ còn khi Reservation chưa tồn tại)
- `TimeSlot?`, `PreferredStartTime?` (derive từ Reservation khi cần)
- `MinDeposit?`, `DepositSnapshot?` (Reservation đã có)

Giữ mirror cho các field có index quan trọng:
- `PlayDate?` (index `IX_Lobbies_PlayDate`)
- `ScheduledStartTime?` (index `IX_Lobbies_ScheduledStartTime`)
- `RecruitmentDeadline?` (index `IX_Lobbies_RecruitmentDeadline`)
- `MaxMembers`, `MinPlayers` (search filter)

**KHÔNG BAO GIỜ** thêm `ScheduledEndTime` vào Lobby — vi phạm ownership rule.

---

## 10. API Endpoints (FE-facing)

> **Quy tắc URL:** Tất cả flow Reservation (online player + walk-in) dùng prefix `/api/v1/reservations/...`. POS dùng `/api/cafes/{cafeId}/pos/...`. **KHÔNG dùng `/api/bookings/...`** cho flow mới.

### 10.1. Reservation APIs (Flow chính — Player app + POS check-in)

```
POST /api/v1/reservations/quote
  Body: { cafeId, gameId, playDate, timeSlot, preferredStartTime?, minPlayers, maxPlayers }
  Auth: Player
  Response 200: ReservationQuoteDto
    { expiresAt, depositRatePerPerson, baseDeposit, riskMultiplier,
      finalDeposit (BVC), minDeposit (BVC), currentBalance (BVC),
      missingAmount, bufferMinutes,
      scheduledStartTime (UTC), scheduledEndTime (UTC, qua đêm với LateNight),
      recruitmentDeadline }

POST /api/v1/reservations/confirm
  Body: {
    cafeId, gameId, playDate, timeSlot, preferredStartTime?,
    minPlayers, maxPlayers, isPrivate,
    expectedFinalDeposit (BVC), idempotencyKey
  }
  Auth: Player
  Response 201:
    { reservationId, reservationCode (8 char),
      lobbyId, lobbyStatus,
      playDate, timeSlot,
      scheduledStartTime (UTC), scheduledEndTime (UTC),
      recruitmentDeadline,
      depositAmount (BVC), status,
      requiresCafeApproval?, cafeApprovalDeadline? }

> Lưu ý: Body dùng **expected fields** (CafeId/GameId/...) thay vì `quoteSnapshot` để server validate đầy đủ constraints. `expectedFinalDeposit` (từ quote) khớp với `finalDeposit` server-side (BR §XVII.2).

GET /api/v1/reservations/{reservationId}
  Auth: Player (owner), Manager, Admin
  Response 200: ReservationResponseDto
    (gồm scheduledStartTime + scheduledEndTime — FE dùng hiển thị đầu-cuối phiên)

GET /api/v1/reservations
  Query: status?, page?, size?
  Auth: Player
  Response 200: list ReservationResponseDto

POST /api/v1/reservations/{reservationId}/cancel
  Body: { reason }
  Auth: Player (host only)
  Response 200:
    { reservationId, status: "Cancelled",
      refund: { eligible, amount (BVC), percentage, reason } }

POST /api/v1/reservations/{reservationId}/extend          # NEW — Phase tiếp theo
  Body: { extensionMinutes }
  Auth: Player (host only)
  Response 200:
    { available: true, extraCharge (BVC), newScheduledEndTime }
  OR 409:
    { available: false, reason: "SLOT_CONFLICT", suggestions: [...] }

GET /api/v1/reservations/{reservationId}/lobby
  → Thay bằng GET /api/v1/lobbies/{lobbyId} (xem docs/api/lobby.md)
```

> **POS check-in** dùng endpoint riêng ở POS namespace:
> `POST /api/cafes/{cafeId}/pos/check-in` (body: `{ code: "<ReservationCode or BookingCode>" }`) — Service tự resolve loại code.

### 10.2. Walk-in APIs (POS flow)

```
GET /api/v1/reservations/walkin/windows
  Query: cafeId, date
  Auth: Manager, CafeStaff
  Response 200:
    { windows: [
        { windowId, windowStart, windowEnd, availableSeats,
          sourceReservation: "<host name>" }
      ] }

POST /api/v1/reservations/walkin
  Body: { walkInWindowId, guestName, guestPhone?, seats, paymentMethod }
  Auth: Manager, CafeStaff
  Response 201:
    { walkInBookingId, status: "Active",
      totalAmount (VND), seatsRemaining, activeSessionId }

POST /api/v1/reservations/walkin/{walkInBookingId}/cancel
  Body: { reason }
  Auth: Manager, CafeStaff
  Response 200: { walkInBookingId, status: "Cancelled" }
```

### 10.3. POS Session APIs (giữ nguyên route cũ)

```
POST /api/cafes/{cafeId}/pos/check-in
  Body: { code }
  Auth: Manager, CafeStaff
  Response 201: { sessionId, activeSessionId, startedAt, lobbyId, reservationId }

POST /api/cafes/{cafeId}/pos/sessions
  Body: { cafeTableId, barcode, reservationId? }
  Auth: Manager, CafeStaff
  Response 201: { sessionId, startedAt }

POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/end
  Body: { actualEndTime, endReason }
  Auth: Manager, CafeStaff
  Response 200:
    { sessionId, status: "Closed",
      bill: { subtotal, penalty, total (VND) },
      refund: { eligible, amount (BVC), percentage, reason },
      walkInWindow?: { id, windowStart, windowEnd, availableSeats }
    }

POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/component-check
  Body: { ... }  # xem docs/api/cafe-pos.md
  Auth: Manager, CafeStaff
```

### 10.4. Lobby APIs (Player app — flow tuyển người)

> Xem chi tiết tại [docs/api/lobby.md](./api/lobby.md). Tóm tắt:

```
POST /api/v1/lobbies/{lobbyId}/join          # Player vào lobby
POST /api/v1/lobbies/{lobbyId}/leave         # Player rời lobby
POST /api/v1/lobbies/search                  # Tìm lobby theo game
GET  /api/v1/lobbies/discoverable            # Browse lobby public
POST /api/v1/lobbies/{lobbyId}/close         # Host đóng lobby
POST /api/v1/lobbies/{lobbyId}/ready         # Member bấm Ready
GET  /api/v1/lobbies/{lobbyId}               # Lobby detail
```

> ⚠️ `POST /api/v1/lobbies` (create lobby) **đã deprecated** — trả 410. Dùng `POST /api/v1/reservations/confirm`.

### 10.5. Karma APIs (BR-RISK)

```
GET /api/v1/karma/me
  Auth: Player
  Response 200: { riskLevel, riskScore, shortPlayCount, restrictions }
  ⚠️ BR-RISK-09: chỉ trả `riskLevel` (low/medium/high/critical), KHÔNG trả `riskScore` chi tiết + signals

POST /api/v1/karma/appeal
  Body: { reason, supportingDocuments? }
  Auth: Player (đã bị suspended/restricted) — BR-RISK-10 chỉ `suspended` được appeal, `banned` không
  Response 201: { appealId, status: "PENDING" }

GET /api/v1/karma/analytics                  # Admin only
  Auth: Admin
  Query: cafeId?, dateFrom?, dateTo?
  Response 200: [ { userId, karmaScore, shortPlayCount, lastShortPlayDate, riskLevel } ]
```

**Admin Cooling-off + Risk Endpoints (Phase 7 — BR-NEW-10 + BR-RISK-09):**

```
POST /api/v1/admin/cooling-off/{userId}/extend
  Auth: Admin (Risk role trở lên — BR-RISK-07)
  Body: { additionalDays: 1..90, reason: string (≥ 20 chars) }
  Response 200: { userId, previousExpiresAt, newExpiresAt, newRiskMultiplier, auditId }
  Side effects: ghi `PlayerActionHistory` với metadata JSON (audit trail vĩnh viễn)
  Validation: `additionalDays` 1..90, `reason` length 20..1000, wallet tồn tại, user đang cooling-off
  Implementation: `CoolingOffService.ExtendAsync` (BoardVerse.Services/Services/CoolingOffService.cs)

GET /api/v1/admin/players/{userId}/risk
  Auth: Admin (Risk role trở lên)
  Response 200: PlayerRiskDetailDto {
    userId, userName, email,
    riskScore, riskLevel, riskMultiplier, accountStatus,
    isCoolingOff, coolingOffExpiresAt,
    signals: [{ signalId, occurredAt, sourceActionType }],
    actionHistoryCount, lastUpdated
  }
  ⚠️ BR-RISK-09: endpoint này KHÔNG để player gọi — endpoint riêng cho admin
  Implementation: `PlayerRiskQueryService.GetPlayerRiskDetailAsync` (parse `PlayerActionHistory.Metadata` JSON)
  Use case: dashboard `risk_dashboard` (§ XVIII.11 file lobby-booking-deposit-bvc.mdc) click vào user → mở detail
```

**Admin Risk Management Endpoints (Phase 8 — BR-RISK-01/02/05/11):**

```
GET /api/v1/admin/users/action-history
  Auth: Admin
  Query: userId?, actionType?, fromUtc?, toUtc?, pageNumber=1, pageSize=20
  Response 200: PaginatedResponse<PlayerActionHistoryDto> {
    items: [{ id, userId, username, actionType, actionBy, actionByUsername,
              reason, metadata (JSON), createdAt, expiresAt }],
    pageNumber, pageSize, totalCount
  }
  ⚠️ BR-RISK-05: audit log vĩnh viễn, mọi admin action phải có entry ở đây
  Implementation: `AdminModerationService.GetPlayerActionHistoryAsync` query `PlayerActionHistories` table

GET /api/v1/admin/alerts
  Auth: Admin
  Query: status?, severity?, alertType?, pageNumber=1, pageSize=20
  Response 200: PaginatedResponse<PlayerAlertDto> {
    items: [{ id, userId, alertType, severity, signals (JSON),
              riskScoreSnapshot, createdAt, acknowledgedBy, acknowledgedAt,
              status, resolutionNote }]
  }
  Use case: dashboard list alerts cho admin review

GET /api/v1/admin/alerts/metrics
  Auth: Admin
  Response 200: PlayerAlertMetricsDto {
    openCritical, openWarning, openInfo,
    acknowledgedAwaitingResolve, resolvedLast24h
  }
  Use case: dashboard top-bar counts (mục 18.12 lobby-booking-deposit-bvc.mdc)

POST /api/v1/admin/alerts/{alertId}/acknowledge
  Auth: Admin
  Body: (empty)
  Response 200: { id, status: "Acknowledged", acknowledgedAt }
  Side effects: set Status=Acknowledged, AcknowledgedBy=adminId, AcknowledgedAt=now
  Errors: 404 AlertNotFound, 409 AlertAlreadyProcessed (nếu đã Resolved/Dismissed)

POST /api/v1/admin/alerts/{alertId}/resolve
  Auth: Admin
  Body: { note: string (1..2000 chars) }
  Response 200: { id, status: "Resolved", resolutionNote, resolvedAt }
  Side effects: ghi `PlayerActionHistory` audit (ActionType=AdminFlagged, metadata=alertId+note), set Status=Resolved
  Errors: 400 note rỗng, 404 AlertNotFound, 409 AlertAlreadyResolved

POST /api/v1/admin/alerts/{alertId}/dismiss
  Auth: Admin
  Body: { note: string (1..2000 chars) }
  Response 200: { id, status: "Dismissed", resolutionNote, dismissedAt }
  Side effects: ghi `PlayerActionHistory` audit với prefix `[False positive]`, set Status=Dismissed
  Use case: alert là false positive do signals chưa đủ evidence

GET /api/v1/admin/players/{userId}/risk-history
  Auth: Admin
  Query: fromUtc? (default = now-30d), toUtc? (default = now)
  Response 200: RiskScoreHistoryDto[] {
    id, userId, riskScore, riskLevel, signals (JSON snapshot), snapshotDate, createdAt
  }
  Use case: chart trend 365 ngày cho admin dashboard (BR-RISK-11)
  ⚠️ Retention: 365 ngày, sau đó aggregate thành monthly summary
```

### 10.6. DEPRECATED — `BookingController` (chỉ Phase migration)

> Các endpoint dưới vẫn LIVE cho walk-in cũ + BR-22 SePay flow, nhưng không dùng cho flow mới:

```
# KHÔNG dùng cho flow Reservation mới:
POST /api/bookings                           # → dùng POST /api/v1/reservations/confirm
POST /api/bookings/{id}/check-in             # → dùng POST /api/cafes/{cafeId}/pos/check-in
POST /api/bookings/{id}/extend               # → dùng POST /api/v1/reservations/{id}/extend
POST /api/bookings/{id}/cancel               # → dùng POST /api/v1/reservations/{id}/cancel
POST /api/bookings/{id}/end                  # → dùng POST /api/cafes/{cafeId}/pos/sessions/{id}/end

# Vẫn live cho walk-in SePay cũ + tương thích ngược:
GET /api/bookings/{id}                       # legacy booking detail
GET /api/bookings/{id}/session-status        # realtime session (Task #8)
```

> **Migration plan:** Phase 1 (W1-W2) đang migrate dần `Booking` → `Reservation` (xem `.cursor/rules/booking-vs-reservation.mdc`). Hiện tại cả 2 đều live. FE app mới **CHỈ DÙNG** `/api/v1/reservations/...`. `BookingController` cũ sẽ được gỡ sau khi FE confirm không còn dùng (ước tính Q1/2027).
>
> **Phase 7 note:** Cooling-off background job (`CoolingOffJob`) + admin endpoints (`/api/v1/admin/cooling-off/*`, `/api/v1/admin/players/{userId}/risk`) **KHÔNG phụ thuộc** `BookingController` — hoạt động độc lập với `Wallet` + `PlayerActionHistory` + `Lobby`. Ngay cả khi `BookingController` bị gỡ trong tương lai, cooling-off vẫn chạy bình thường.

---

## 11. Flow Reference — Tổng hợp cho FE

```
┌─────────────────────────────────────────────────────────────────┐
│ FLOW A: Player app (online) — khung đặt chỗ chính           │
└─────────────────────────────────────────────────────────────────┘
   Client                                Server
    │                                      │
    │ POST /api/v1/reservations/quote      │   ← xem quote, cọc, deadline
    │  (cafeId, gameId, playDate, timeSlot,│
    │   minPlayers, maxPlayers)            │
    │ ─────────────────────────────────▶   │
    │ ◀────────────────────────────────  │ ReservationQuoteDto
    │                                      │
    │ (nếu BVC thiếu: mở TopUp BVC)        │
    │                                      │
    │ POST /api/v1/reservations/confirm    │   ← atomic confirm
    │  (cafeId, gameId, playDate, timeSlot,│
    │   preferredStartTime?, minPlayers,   │
    │   maxPlayers, isPrivate,             │
    │   expectedFinalDeposit, idempotencyKey) │
    │ ─────────────────────────────────▶   │
    │ ◀── Reservation + Lobby response ──┤
    │                                      │
    │ GET /api/v1/lobbies/{lobbyId}        │   ← xem lobby detail, members
    │ POST /api/v1/lobbies/{lobbyId}/join  │   ← member tham gia
    │ POST /api/v1/lobbies/{lobbyId}/leave │   ← member rời
    │                                      │
    │ Khi đạt minPlayers → Server signalR  │
    │   "LobbyViable" + "ReservationConfirmed"
    │                                      │
    │ POST /api/v1/reservations/{id}/cancel │   ← host hủy trước giờ
    │ POST /api/v1/reservations/{id}/extend│   ← (Phase mới) extend
    │ POST /api/check-in/scan-qr           │   ← player scan QR POS
    │   body { token }                     │
    │                                      │

┌─────────────────────────────────────────────────────────────────┐
│ FLOW B: POS app (staff) — check-in, end session             │
└─────────────────────────────────────────────────────────────────┘
   Client                                Server
    │                                      │
    │ POST /api/cafes/{cafeId}/pos/check-in│   ← staff quét QR / nhập code
    │  body { code: "ABCD1234" }          │     → resolve ReservationCode/BookingCode
    │ ─────────────────────────────────▶   │     → check-in vào ActiveSession
    │ ◀────────────────────────────────  │
    │                                      │
    │ POST /api/cafes/{cafeId}/pos/sessions│   ← start session + bind barcode
    │  body { cafeTableId, barcode }      │
    │ ─────────────────────────────────▶   │
    │                                      │
    │ POST /api/cafes/{cafeId}/pos/sessions│   ← end session (on-time / early)
    │       /{sessionId}/end              │
    │  body { actualEndTime, endReason }  │
    │ ─────────────────────────────────▶   │
    │ ◀── bill + walkInWindow ─────────── ┤
    │                                      │
    │ POST /api/cafes/{cafeId}/pos/sessions│   ← kiểm kê linh kiện (BR-12)
    │       /component-check              │
    │                                      │
    │ POST /api/cafes/{cafeId}/pos/sessions│   ← gộp session (Exception 4)
    │       /{sessionId}/transfer-members│
    │                                      │
    │ (Gọi POS payment flow ở docs/api/cafe-pos.md)

┌─────────────────────────────────────────────────────────────────┐
│ FLOW C: Walk-in (POS only) — khách vãng lai không đặt online│
└─────────────────────────────────────────────────────────────────┘
   Client                                Server
    │                                      │
    │ GET /api/v1/reservations/walkin/windows│  ← xem walk-in window trống
    │  ?cafeId=&date=                     │
    │ ─────────────────────────────────▶   │
    │ ◀── list WalkInWindowDto ──────────┤
    │                                      │
    │ POST /api/v1/reservations/walkin     │   ← tạo WalkInBooking
    │  body { walkInWindowId, guestName,  │     → OCC trên WalkInWindow
    │          guestPhone?, seats,        │     → tạo WalkInBooking + ActiveSession
    │          paymentMethod }            │
    │ ─────────────────────────────────▶   │
    │ ◀── walkInBookingId + bill ────────┤
    │                                      │
    │ (POS xử lý payment qua SePay/VietQR) │
    │ (After payment → walk-in ngồi chơi) │
    │                                      │
    │ POST /api/v1/reservations/walkin/{id}/cancel  │ ← cancel nếu chưa check-in
```

---

## 12. UX/UI Design (giữ nguyên từ doc 1.0, có chỉnh labels)

### 12.1. App — Reservation Flow

```
┌─────────────────────────────────────────┐
│  Đặt chỗ BoardVerse                   │
│                                         │
│  Cafe: [BoardVerse Cafe    ▼]            │
│  Game: [Catan               ▼]          │
│                                         │
│  Ngày chơi: 12/08/2026 (CN)              │
│                                         │
│  Khung giờ:                              │
│   ● Morning (08:00-13:00)                │
│   ● Afternoon (13:00-18:00) ← chọn       │
│   ○ Evening (17:00-23:00)                │
│   ○ LateNight (23:00-06:00, qua đêm)    │
│                                         │
│  ⏰ Giờ bắt đầu mong muốn:               │
│  [14:00 ▼] (optional, trong khung)      │
│                                         │
│  Số người: Min [2] — Max [5]              │
│                                         │
│  ┌─────────────────────────────────┐    │
│  │     XEM TRÍCH GIÁ CỌC           │    │
│  └─────────────────────────────────┘    │
└─────────────────────────────────────────┘
```

**Quote screen (sau khi nhấn "Xem trích giá"):**

```
┌─────────────────────────────────────────┐
│  📊 TRÍCH GIÁ ĐẶT CỌC                  │
│                                         │
│  Cafe: BoardVerse Cafe                   │
│  Game: Catan                            │
│  Ngày: 12/08/2026 (CN)                  │
│  Khung giờ: Afternoon 13:00-18:00      │
│  Số người dự kiến: 2-5                  │
│  ⏰ Giờ bắt đầu mong muốn: 14:00        │
│                                         │
│  ─── Breakdown cọc (BVC) ───           │
│  Cọc theo người: 5 người × 25 BVC = 125│
│  Min deposit (5 ngày): 200 BVC          │
│  Hệ số rủi ro: 1.0x                    │
│  → CỌC CUỐI: 200 BVC                   │
│                                         │
│  Ví BVC: 50 BVC                         │
│  ⚠️ CẦN NẠP THÊM: 150 BVC (= 150.000đ) │
│                                         │
│  Thời hạn trích giá: 4:32                │
│                                         │
│  ┌─────────────────┐                     │
│  │   NẠP BVC      │                     │
│  └─────────────────┘                     │
│  ┌─────────────────────────────────┐    │
│  │     XÁC NHẬN ĐẶT CỌC          │    │
│  └─────────────────────────────────┘    │
└─────────────────────────────────────────┘
```

### 12.2. App — Reservation Detail (my booking)

```
┌─────────────────────────────────────────┐
│  📋 Đặt chỗ của tôi                     │
│  ReservationCode: ABCD1234              │
│                                         │
│  ─── Sắp tới ───                       │
│  ┌─────────────────────────────────┐    │
│  │ 🏠 BoardVerse Cafe              │    │
│  │ 📅 12/08/2026 (CN)              │    │
│  │ ⏰ Afternoon 13:00-18:00        │    │
│  │ 👥 2-5 người                     │    │
│  │ 💰 Cọc: 200 BVC (= 200.000đ)   │    │
│  │                                   │    │
│  │ [QR Check-in] [Hủy đặt]        │    │
│  └─────────────────────────────────┘    │
│                                         │
│  ─── Đã hoàn thành ───                  │
│  ┌─────────────────────────────────┐    │
│  │ 🏠 BoardVerse Cafe              │    │
│  │ 📅 10/08/2026 (T2)              │    │
│  │ ⏰ Afternoon 13:00-18:00        │    │
│  │ ✅ Đã chơi 5h (100%)            │    │
│  │ 💰 BVC: Forfeit 100%            │    │
│  └─────────────────────────────────┘    │
│                                         │
│  ⚠️ Karma Warning                       │
│  Bạn đã 3 lần đặt Afternoon nhưng       │
│  chơi dưới 50%. Lần tới vui lòng       │
│  đặt Morning/Evening để tránh hạn chế. │
└─────────────────────────────────────────┘
```

### 12.3. POS — Active Sessions

```
┌─────────────────────────────────────────┐
│  📋 Sessions đang hoạt động            │
│                                         │
│  ┌─────────────────────────────────┐    │
│  │ 🔵 Nhóm A (RES-AB1234)         │    │
│  │    Catan Room                   │    │
│  │    ⏰ 13:00 ────●──── 18:00    │    │
│  │    👥 4/4 ghế | Còn: 2h 30m    │    │
│  │    🟡 Sắp hết TimeSlot (30p)   │    │
│  │                                   │    │
│  │ [Extend] [End Session]          │    │
│  └─────────────────────────────────┘    │
│                                         │
│  ┌─────────────────────────────────┐    │
│  │ 🟢 Walk-in: Nguyễn Văn B        │    │
│  │    ⏰ 15:30 ────●──── 17:30    │    │
│  │    👥 3 ghế | Còn: 1h          │    │
│  │                                   │    │
│  │ [End Session]                    │    │
│  └─────────────────────────────────┘    │
│                                         │
│  ─── Sắp bắt đầu ───                    │
│  ┌─────────────────────────────────┐    │
│  │ ⏳ Nhóm B (RES-CD5678)          │    │
│  │    ⏰ Afternoon 14:00-18:00    │    │
│  │    👥 2/5 ghế                   │    │
│  │    Chưa check-in               │    │
│  └─────────────────────────────────┘    │
│                                         │
│  ─── Walk-in Windows ───                 │
│  ┌─────────────────────────────────┐    │
│  │ 🚶 15:00-18:00: 4 ghế trống    │    │
│  │    (Nhóm A về sớm — RES-AB1234)│    │
│  │    [Add Walk-in]                │    │
│  └─────────────────────────────────┘    │
└─────────────────────────────────────────┘
```

### 12.4. POS — End Session Dialog

```
┌─────────────────────────────────────────┐
│  🔚 Kết thúc Session                    │
│                                         │
│  ─── Nhóm A (RES-AB1234) ───            │
│  Catan Room                              │
│                                         │
│  ⏱ Thời gian:                          │
│  • Bắt đầu: 13:05                      │
│  • Kết thúc: 15:30                      │
│  • Đã chơi: 2h 25p                      │
│  • TimeSlot đặt: Afternoon 13:00-18:00 (5h) │
│  • Played ratio: 48%                   │
│                                         │
│  💰 Thanh toán:                         │
│  • Tiền giờ: 2h 25p × 10k = 24,500đ    │
│  • BVC đã cọc: 200 BVC (không trừ)    │
│                                         │
│  📊 Refund BVC:                         │
│  ┌─────────────────────────────────┐    │
│  │ ⚠️ Chơi dưới 50% TimeSlot      │    │
│  │ BVC không được refund           │    │
│  │ (Forfeit toàn bộ 200 BVC)      │    │
│  └─────────────────────────────────┘    │
│                                         │
│  🚶 Walk-in Window:                    │
│  ┌─────────────────────────────────┐    │
│  │ Tự động tạo window:            │    │
│  │ 15:30 - 18:00 (2h 30p)         │    │
│  │ 4 ghế available                 │    │
│  │                                   │    │
│  │ ✅ Walk-in Window đã tạo        │    │
│  └─────────────────────────────────┘    │
│                                         │
│  [ Hủy ]           [ Xác nhận End ]   │
└─────────────────────────────────────────┘
```

### 12.5. POS — Add Walk-in

```
┌─────────────────────────────────────────┐
│  🚶 Thêm Walk-in (WalkInWindow)        │
│                                         │
│  Chọn Window:                           │
│  ┌─────────────────────────────────┐    │
│  │ ● Window 15:30 - 18:00 (2h 30p) │    │
│  │   4 ghế trống                   │    │
│  │   (Nhóm A về sớm — RES-AB1234) │    │
│  └─────────────────────────────────┘    │
│                                         │
│  👤 Tên khách: *                        │
│  [ Nguyễn Văn B                       ]    │
│                                         │
│  📱 Số điện thoại:                       │
│  [ 0901234567                          ]    │
│                                         │
│  👥 Số người: *                         │
│  [ 3 ] người (còn 1 ghế)               │
│                                         │
│  💰 Tạm tính:                          │
│  2 tiếng × 10k × 3 người = 60,000đ    │
│  (Thanh toán cuối session qua POS)      │
│                                         │
│  Phương thức: ● Sepay  ○ Tiền mặt       │
│                                         │
│  [ Hủy ]      [ Xác nhận tạo Walk-in ]   │
└─────────────────────────────────────────┘
```

---

## 13. Implementation Notes

### 13.1. Phased Rollout

| Phase | Tuần | Scope | Status |
|---|---|---|---|
| 0 | W0 | **DB-stored time fields:** thêm `ScheduledStartTime`/`ScheduledEndTime` vào `Reservation` (lưu DB), thêm 2 index, fix `BuildScheduledStartEnd` helper (xử lý `LateNight` qua đêm). | ✅ Done (2026-08-12) |
| 1 | W1-2 | **Reservation core + Migrate `Booking` → `Reservation`:**<br>• Verify atomic confirm + Lobby flow end-to-end<br>• TD-01: Fix `BookingService.cs` `Lobby.BookingId` bug<br>• TD-02: Add `ReservationId?` FK to `KarmaShortPlayRecord`/`BookingRating`/`BookingNoShowVote`, migrate aggregation<br>• Deprecate `TimeSlotBookingController` (tag `[Obsolete]`, redirect → `/api/v1/reservations`)<br>• Audit `BookingController`, mark endpoints legacy | ✅ Done (2026-08-12) |
| 2 | W3-4 | Walk-in Window + WalkInBooking + POS flow:<br>• `WalkInWindow` entity + `WalkInBooking` entity<br>• `WalkInWindowStatus` + `WalkInBookingStatus` enums<br>• `WalkInController` + `WalkInService` + `IWalkInService`<br>• `GET /api/v1/reservations/walkin/windows` + `POST /api/v1/reservations/walkin/windows/{id}/close`<br>• `POST /api/v1/reservations/walkin` (IdempotencyKey support)<br>• `POST /api/v1/reservations/walkin/{id}/cancel` (trả ghế về WalkInWindow)<br>• OCC via PostgreSQL `xmin` cho `WalkInWindow.TryHoldSeatsAsync`<br>• `WalkInWindowCleanupJob` (background service) | ✅ Done (2026-08-12) |
| 3 | W5-6 | Extension flow + Karma tracking + Override + Auto-release + No-show detection + Early checkout WalkInWindow:<br>• `ExtendReservationRequestDto` + `ExtendAvailabilityDto` + `ExtendReservationResponseDto`<br>• `ReservationExtensionService.CheckAvailabilityAsync` + `ExtendAsync`<br>• `GET /api/v1/reservations/{id}/extend/availability` + `POST /api/v1/reservations/{id}/extend`<br>• `Reservation.ExtensionCount` + `ExtendedEndTime`<br>• `KarmaShortPlayRecord.KarmaDelta` + `KarmaPointsAdded` + `TotalKarmaScore` + `Status`<br>• `ReservationService.TriggerShortPlayTrackingAsync` (gọi trong `CompleteAndCaptureAsync`)<br>• `ReservationNoShowDetectionJob` + `AutoReleaseExpiredSessionsJob` (background services)<br>• `KarmaRecordStatus` enum + `GroupSessionStatus.Closed`<br>• Early checkout WalkInWindow (§4.4): `ActiveSessionService.TryCreateWalkInWindowAsync` trong `PaySessionAsync`<br>• §4.7 NoShow tạo WalkInWindow (ReservationNoShowDetectionJob)<br>• EC-09 Auto-release tạo WalkInWindow (AutoReleaseExpiredSessionsJob) | ✅ Done (2026-08-12) |
| 4 | W7-8 | Performance tuning + Edge case handling + UI refinement + gỡ `BookingController` (khi FE xác nhận không còn dùng) | ✅ Done (2026-08-12) |
| 5 | W9 | **BR-REFUND-08 — Late cancel after check-in:**<br>• Endpoint `POST /api/v1/reservations/{id}/cancel-after-checkin` (Role: Player host).<br>• `CancelAfterCheckinRequestDto` + `CancelAfterCheckinResponseDto` (BoardVerse.Core/DTOs/Reservation).<br>• `LateCancelRefundCalculator` helper (BoardVerse.Core/Helpers/) — pure math, 10 unit tests.<br>• `ReservationService.CancelAfterCheckinAsync` — Serializable transaction, refund 30% nếu playedRatio ≥ 0.5, forfeit 100% nếu &lt; 0.5.<br>• ApiErrorMessages: `OnlyHostCanLateCancelAfterCheckin`, `MustBeCheckedInToLateCancel`, `NotEnoughPlayedForLateCancelRefund`.<br>• Idempotent retry cho Postgres serialization failure (40001) × 3.<br>• BR-REFUND-06 + BR-REFUND-07 vẫn dùng logic 3-tier (0.5/0.9) trong `CompleteAndCaptureAsync` + `TryCreateWalkInWindowAsync`. | ✅ Done (2026-08-12) |
| 6 | W10 | **EC-11 — Manager override played time (BR-REFUND-07 completion):**<br>• Endpoint `POST /api/cafes/{cafeId}/pos/sessions/override-played-time` (Role: Manager only).<br>• `OverridePlayedTimeRequestDto` + `OverridePlayedTimeResponseDto` (BoardVerse.Core/DTOs/Pos/DisputePlayedTimeDto.cs).<br>• `ActiveSessionBillingCalculator` helper (BoardVerse.Core/Helpers/) — pure math, 12 unit tests. Refactored từ `ActiveSessionService.CalculateRealtimeBilling` để share giữa `ActiveSessionService` + `CafePosService`.<br>• `CafePosService.OverridePlayedTimeAsync` — Manager review dispute, set `NewTotalMinutesPlayed`, recalc `Subtotal` + `TotalAmount` theo tỉ lệ scale member minutes.<br>• Audit: `ActionType=PlayedTimeOverridden (=41)`, linked to dispute audit qua `linkedDisputeAuditId`.<br>• Validation: Manager only, session chưa Paid, phải có dispute audit trước, `NewTotalMinutesPlayed ∈ [0, 1440]`.<br>• ApiErrorMessages: `OnlyManagerCanOverride`, `NoDisputeBeforeOverride`, `CannotOverridePaidSession`, `OverrideMinutesExceedsPolicy`. | ✅ Done (2026-08-12) |
| 7 | W11 | **BR-NEW-10 — Cooling-off background job + admin endpoints:**<br>• `ICoolingOffService` + `CoolingOffService` (BoardVerse.Services/Services/CoolingOffService.cs): `DetectAndActivateAsync` (quét signals 3 TimeoutFailed/HostCancelled trong 7d HOẶC forfeit > 500 BVC (= 500.000 VND) trong 30d → activate 30d + RiskMultiplier ×2), `ExpireOverdueAsync` (auto-deactivate khi `CoolingOffExpiresAt < now`), `EscalateAsync` (×3 multiplier + extend 30d), `ExtendAsync` (admin manual extend 1..90d, ghi audit).<br>• Background job: `CoolingOffJob` (BoardVerse.API/BackgroundServices/CoolingOffJob.cs) chạy mỗi 30 phút, batch 100 wallets/tick.<br>• Repository: `ILobbyRepository.CountFailuresByTypeForHostAsync` (per-host failure count), `IWalletRepository.GetActiveCoolingOffWalletsPagedAsync` + `GetActiveWalletsPagedAsync`.<br>• **Admin endpoints** trong `AdminModerationController`:<br>&nbsp;&nbsp;- `POST /api/v1/admin/cooling-off/{userId}/extend` — admin extend thêm N ngày (1..90), ghi audit JSON.<br>&nbsp;&nbsp;- `GET /api/v1/admin/players/{userId}/risk` — admin xem `PlayerRiskDetailDto` (RiskScore, RiskMultiplier, RiskLevel, AccountStatus, IsCoolingOff, Signals từ `PlayerActionHistory.Metadata`, ActionHistoryCount).<br>• DTOs: `ExtendCoolingOffRequestDto/ResponseDto`, `PlayerRiskDetailDto` (BoardVerse.Core/DTOs/Admin/CoolingOffDto.cs).<br>• Service: `IPlayerRiskQueryService` + `PlayerRiskQueryService` (InMemory DbContext trong DI).<br>• 22 unit tests cho `CoolingOffService` (detect/activate/expire/escalate/extend validation) + 5 unit tests cho `PlayerRiskQueryService` (mapping + JSON parse signals).<br>• Errors: `InCoolingOffCannotCreateFutureLobby`.<br>• DI: `AddScoped<ICoolingOffService>` + `AddScoped<IPlayerRiskQueryService>` + `AddHostedService<CoolingOffJob>` trong `Program.cs`. | ✅ Done (2026-08-12) |
| 8 | W12 | **BR-RISK-* — Risk Management & Admin Audit Log (full system):**<br>• **Entities mới** (3): `PlayerAlert` (BoardVerse.Core/Entities/PlayerAlert.cs) + `PlayerRiskScore` (PK=UserId, single snapshot) + `RiskScoreHistory` (BR-RISK-11, 365 ngày audit).<br>• **Enums mới** (3): `PlayerAlertType` (`AutoThresholdCrossed`/`MultiAccountDetected`/`ManualReport`/`AdminFlagged`) + `PlayerAlertSeverity` (`Info`/`Warning`/`Critical`) + `PlayerAlertStatus` (`Open`/`Acknowledged`/`Resolved`/`Dismissed`).<br>• **Background jobs** (3): `RiskScoreRecomputeJob` (mỗi giờ, batch 100 wallets, compute SIG-01/02/03/08 signals) + `SuspensionExpiryCheckJob` (mỗi giờ, revert `AccountStatus=Suspended` → `Active` khi `LockoutEndDate < now`, ghi audit) + `AlertExpiryCleanupJob` (mỗi ngày, dismiss alerts Open quá 30 ngày không ai review).<br>• **Services**: `PlayerRiskScoreService.RecomputeForUserAsync` (compute riskScore 0-100 từ signals, upsert `PlayerRiskScore`, append `RiskScoreHistory`, auto-create Critical alert) + `PlayerAlertService.EnsureAlertForSignalsAsync` (cooldown 7 ngày, không spam alert trùng).<br>• **A-01/A-02 fix**: `AdminModerationService.PunishUserAsync` + `AdjustKarmaAsync` inject `BoardVerseDbContext`, ghi `PlayerActionHistory` audit JSON (BR-RISK-05). Suspend set `ExpiresAt = now + duration`.<br>• **Schema bug fix**: `PlayerActionHistoryConfiguration.HasConversion<int>()` thay vì `HasConversion<string>()` để khớp cột DB `ActionType` (đã được migration từ trước dưới dạng integer).<br>• **Admin endpoints mới** (7) trong `AdminModerationController`:<br>&nbsp;&nbsp;- `GET /api/v1/admin/users/action-history?userId=&actionType=&fromUtc=&toUtc=` — paginated audit log (BR-RISK-05).<br>&nbsp;&nbsp;- `GET /api/v1/admin/alerts?status=&severity=&alertType=` — list `PlayerAlert` (BR-RISK-02).<br>&nbsp;&nbsp;- `GET /api/v1/admin/alerts/metrics` — counts cho dashboard (OpenCritical/OpenWarning/OpenInfo).<br>&nbsp;&nbsp;- `POST /api/v1/admin/alerts/{alertId}/acknowledge` — đánh dấu admin đã xem.<br>&nbsp;&nbsp;- `POST /api/v1/admin/alerts/{alertId}/resolve` — đóng alert + ghi audit note.<br>&nbsp;&nbsp;- `POST /api/v1/admin/alerts/{alertId}/dismiss` — dismiss false positive + ghi audit note.<br>&nbsp;&nbsp;- `GET /api/v1/admin/players/{userId}/risk-history?fromUtc=&toUtc=` — lịch sử riskScore 365 ngày cho chart (BR-RISK-11).<br>• **DTOs mới** (3): `PlayerActionHistoryDto` + `PlayerAlertDto` + `AlertResolveRequestDto`.<br>• **Repository extensions**: `IPlayerAlertRepository` + `IPlayerRiskScoreRepository` (GetPaged, Upsert, AppendHistory, ShouldCreateAutoAlertAsync với cooldown check).<br>• **Integration tests** updated để xử lý schema fix (16/16 AdminModeration tests pass).<br>• **⏭ SKIPPED**: R-02 `PlayerAccountLink` + `signal_detect_multi_account` job — multi-account detection không thuộc MVP theo yêu cầu user (2026-08-12). | ✅ Done (2026-08-12) |

#### Phase 4 deliverables (2026-08-12)

**EC-10 — Time-overrun warning** (§7.1):
- Helper `ReservationTimeOverrunHelper.Compute(scheduledEndTimeUtc, estimatedRemainingMinutes)` (BoardVerse.Core/Helpers/).
- DTO: `ActiveSessionDto.TimeOverrunWarning` + `TimeSlotRemainingMinutes`, `ActiveSessionResponseDto` cùng field.
- Repository: `CafePosRepository.GetActiveSessionsAsync` + `GetUnpaidSessionsAsync` load `Lobby → Reservation` để có `ScheduledEndTime`.
- `ActiveSessionRepository.GetByIdAsync` include `Lobby.Reservation`.
- POS UI sẽ show banner khi `estimatedRemainingMinutes > timeSlotRemainingMinutes` (FE sẽ tích hợp từ UI sprint tới).

**EC-11 — Player dispute played time** (§7.2):
- Enum extend: `AdminActionType.PlayedTimeDisputed = 40`, `PlayedTimeOverridden = 41`.
- DTO: `DisputePlayedTimeRequestDto` + `DisputePlayedTimeResponseDto` (BoardVerse.Core/DTOs/Pos/DisputePlayedTimeDto.cs).
- Endpoint: `POST /api/cafes/{cafeId}/pos/sessions/dispute-played-time` (Manager, CafeStaff).
- Service: `CafePosService.DisputePlayedTimeAsync` — ghi `PlayerActionHistory` với metadata JSON (StartedAt, EndedAt, totalMinutes, claim, proposedResolution).
- **Manager override (Phase 6 ✅ — 2026-08-12):** xem §7.2 bên trên.

**RFC 8594 Deprecation headers** (§13.1 Phase 1):
- Filter `DeprecationHeadersAttribute` (BoardVerse.API/Filters/) set `Deprecation: true` + `Sunset` + `Link` cho mọi response của class/controller decorate.
- Apply cho `BookingController` với sunset `2026-12-31` + link `/docs/api/booking#deprecation`.
- Sau sunset date, controller sẽ đổi sang trả 410 Gone. Đến đó sẽ xóa file hoàn toàn nếu FE xác nhận không còn dùng.

**Performance tuning**:
- Phát hiện 2 index Reservation đã được config trong `ReservationConfiguration` (Phase 0) nhưng **chưa được apply** trong DB do project dùng `EnsureCreated` + manual migration scripts (không có folder `Migrations/`).
- Đã apply trên testing branch: `IX_Reservations_ScheduledStartTime_Status`, `IX_Reservations_ScheduledEndTime_Status` (Postgres `CREATE INDEX CONCURRENTLY`).
- Script để apply production: `docs/migrations/phase4-reservation-scheduled-time-indexes.sql`.
- Index quan trọng cho background services `WalkInWindowCleanupJob` + `ReservationNoShowDetectionJob` chạy mỗi phút query theo (ScheduledEndTime, Status).
- Verify định kỳ: chạy `EXPLAIN ANALYZE` trên query của 2 job trên production mỗi tháng để đảm bảo planner dùng index.

**Tests** (BoardVerse.Tests/):
- `ReservationTimeOverrunHelperTests` — 7 test cases (null scheduledEnd, overrun, no overrun, already-ended, edge exact equal, default now, ceil rounding).
- `DeprecationHeadersAttributeTests` — 3 test cases (RFC 8594 headers, defaults, AttributeUsage spec).

### 13.2. Testing Strategy (theo `api-doc-test-standards.mdc`)

**Unit Tests** (`BoardVerse.Tests/Services/`):
- `ReservationServiceTests.CalculateAsync_*` — deposit formula
- `ReservationServiceTests.CancelAsync_*` — BR-REFUND-01/02
- `ReservationServiceTests.CompleteAndCaptureAsync_*` — BR-END + BR-REFUND-04/05/06 + Karma tracking
- `ReservationServiceTests.CancelAfterCheckinAsync_*` — BR-REFUND-08 (late cancel after check-in, 30% refund if `playedRatio >= 0.5`)
- `LateCancelRefundCalculatorTests.Compute_*` — pure helper for ratio/refund calculation (Phase 5)
- `ActiveSessionBillingCalculatorTests.CalculateRealtimeBilling_*` — pure helper for billing model (Phase 6, 12 tests)
- `CafePosServiceTests.OverridePlayedTimeAsync_*` — Phase 6 manager override + audit
- `CafePosServiceTests.DisputePlayedTimeAsync_*` — Phase 4 EC-11 staff dispute audit
- `ReservationTimeOverrunHelperTests.Compute_*` — Phase 4 EC-10 time-overrun warning
- `DeprecationHeadersAttributeTests.OnResultExecuting_*` — Phase 4 RFC 8594 headers
- `WalkInWindowServiceTests.TryHoldSeatsAsync_*` — OCC race condition
- **`CoolingOffServiceTests.DetectSignalsAsync_*` + `DetectAndActivateAsync_*`** — Phase 7 BR-NEW-10 (15 tests)
- **`CoolingOffExtendTests.ExtendAsync_*`** — Phase 7 admin extend validation (7 tests)
- **`PlayerRiskQueryServiceTests.GetPlayerRiskDetailAsync_*`** — Phase 7 admin risk view + JSON parsing (5 tests)
- **`AdminModerationServiceTests.PunishUserAsync_*` + `AdjustKarmaAsync_*`** — Phase 8 A-01/A-02 audit log fix (test 16/16 AdminModeration integration pass sau khi fix `PlayerActionHistory.HasConversion<int>()`)
- **`PlayerRiskScoreService.ComputeRiskScore_*` + `ResolveRiskLevel_*`** — Phase 8 BR-RISK-01 pure math (signal weights + level thresholds)

**Integration Tests** (`BoardVerse.Tests/Integration/`):
- Atomic reservation + lobby creation
- Walk-in creation with optimistic concurrency
- Cancel flow + refund calculation
- No-show background job
- **Cooling-off background job** (Phase 7) — verify `DetectAndActivateAsync` + `ExpireOverdueAsync` runs on schedule, idempotent across multiple ticks

**Total Phase 4-8 unit tests added:** 4 (EC-10) + 1 (Deprecation) + 10 (LateCancel) + 12 (ActiveSessionBilling) + 22 (CoolingOff + PlayerRisk) + **3 (PlayerRiskScoreService — Phase 8)** = **52 tests**. All pre-existing integration failures (31) are unrelated to Phase 4-8 changes; **16/16 AdminModeration integration tests pass** sau Phase 8 schema fix.

### 13.3. Monitoring & Alerting

| Metric | Target |
|---|---|
| Reservation conversion rate (quote → confirm) | > 60% |
| No-show rate | < 15% |
| Early checkout rate (ratio < 0.5) | < 10% |
| Walk-in fill rate (windows used > 50%) | > 70% |
| Extension success rate | > 80% |
| Oversell detected | **0** (Critical alert) |
| **Cooling-off users active** (Phase 7) — count `Wallet.IsCoolingOff = true` mỗi giờ | Monitor spike — alert nếu > 5% user base |
| **Cooling-off escalation rate** (Phase 7) — `EscalateAsync` calls/day | < 10/day (false positive threshold) |
| **Cooling-off admin extend rate** (Phase 7) — `ExtendAsync` calls/day | < 5/day (human review threshold) |
| **Risk dashboard poll latency** (Phase 7) — admin `/players/{userId}/risk` p95 | < 500ms |
| **Open Critical alerts count** (Phase 8 — BR-RISK-02) — `PlayerAlert.Status = Open AND Severity = Critical` | < 50 active (review SLA 24h) |
| **Open Critical alert review latency** (Phase 8) — `AcknowledgedAt - CreatedAt` p95 | < 24h (admin phải review trong SLA) |
| **Alert auto-creation cooldown violations** (Phase 8) — 2 alerts cùng `UserId` trong 7 ngày | 0 (cooldown enforcement) |
| **Suspension auto-release rate** (Phase 8 — BR-RISK-06) — `SuspensionExpiryCheckJob` reverts/day | track — verify match manual admin release count |
| **Risk score recompute batch latency** (Phase 8) — `RiskScoreRecomputeJob` p95 batch | < 60s cho 100 wallets |

### 13.4. Rollback Plan

Nếu có issue với Reservation flow mới:
1. Disable mới features (`extension`, `walk-in`) qua feature flag.
2. Fallback về Reservation core + Lobby flow (no extension, no walk-in) — không rollback về `Booking` cũ.
3. Nếu data bị lỗi nghiêm trọng: ETL script sẽ move records giữa các bảng trong cùng namespace `Reservation` (không quay lại `Booking`).

---

## 14. Glossary

| Term | Definition |
|------|------------|
| **Reservation** | Bản ghi giữ chỗ ngồi + game copy + BVC hold. Root entity của flow mới. |
| **ReservationCode** | Mã 8 ký tự alphanumeric uppercase dùng cho POS scan QR check-in. |
| **TimeSlot** | Enum cố định 4 giá trị: `Morning` / `Afternoon` / `Evening` / `LateNight`. |
| **playDate** | `DateOnly` — ngày dự kiến chơi. |
| **preferredStartTime** | `TimeOnly?` — giờ bắt đầu mong muốn (optional, trong `TimeSlot`). |
| **ScheduledStartTime** | `DateTime = playDate + TimeSlot.startTime`. Lưu DB tại `Reservation.ScheduledStartTime` (BR-RESV-02). |
| **ScheduledEndTime** | `DateTime = playDate + TimeSlot.endTime` (cộng 1 ngày nếu `TimeSlot.LateNight` qua đêm). Lưu DB tại `Reservation.ScheduledEndTime`. |
| **ScheduledDurationMinutes** | `(ScheduledEndTime - ScheduledStartTime).TotalMinutes` — tính runtime, dùng cho `playedRatio` (§4.3) và extension flow (Phase 3). |
| **RecruitmentDeadline** | `ScheduledStartTime - leadTimeMinutes` (mặc định 20 phút). |
| **BVC** | BoardVerse Coin. 1 BVC = 1.000 VND. |
| **Walk-in Window** | `WalkInWindow` entity — khoảng thời gian trống có thể bán cho walk-in. |
| **Walk-in Booking** | `WalkInBooking` entity — đặt chỗ cho khách vãng lai (không qua lobby). |
| **Early Checkout** | Player về sớm — `playedRatio < 1.0`. |
| **Played Ratio** | `playedDuration / ScheduledDurationMinutes` với `playedDuration = EndedAt - StartedAt` (decimal `[0.0, ~1.0+]`). |
| **No-Show** | Player không check-in trong 30 phút sau `ScheduledStartTime`. |
| **Lead Time** | Số phút chuẩn bị trước `ScheduledStartTime` (mặc định 20). |
| **Buffer** | `RecruitmentDeadline - now()` — phút host có để tuyển. |
| **OCC** | Optimistic Concurrency Control — kỹ thuật tránh race condition. |
| **ActiveSession** | `ActiveSession` entity — phiên chơi thực tế sau check-in. |
| **RiskScore** | `Wallet.RiskScore` (0-100) — điểm rủi ro player, hệ thống tự tính từ 10 signals (BR-RISK-01). User KHÔNG thấy — chỉ `riskLevel` (BR-RISK-09). |
| **RiskMultiplier** | `Wallet.RiskMultiplier` (1.0-3.0) — hệ số nhân cọc. `1.0 + (riskScore / 100) × 1.0` (BR-RISK-03), hoặc ×2 khi cooling-off (BR-NEW-10). |
| **RiskLevel** | `Wallet.RiskLevel` enum: `Low` (0-29) / `Medium` (30-49) / `High` (50-74) / `Critical` (75-100). FE chỉ thấy `RiskLevel`, không thấy `RiskScore`. |
| **AccountStatus** | `Wallet.AccountStatus` enum: `Active` < `Warning` < `Restricted` < `Suspended` < `Banned` (BR-RISK-04). `Suspended`/`Banned` chặn tất cả thao tác. |
| **Cooling-off** | Trạng thái `Wallet.IsCoolingOff = true` — user bị hạn chế tạo lobby `playDate > today` (BR-NEW-10). Kích hoạt khi 3 lobby fail trong 7 ngày HOẶC forfeit > 500 BVC (= 500.000 VND) trong 30 ngày. Thời hạn 30 ngày, tự động đánh lại mỗi 30 phút qua `CoolingOffJob`. |
| **Signal (BR-RISK-01)** | Tín hiệu hành vi bất thường của user: `SIG-01` (lobby timeout 7d) / `SIG-02` (host cancel 7d) / `SIG-03` (forfeit 30d) / `SIG-04` (spam cùng playDate) / `SIG-05` (join/leave 24h) / `SIG-06` (bị từ chối) / `SIG-07` (multi-account) / `SIG-08` (create+cancel < 5p) / `SIG-09` (hour-pattern) / `SIG-10` (report). Mỗi signal có trọng số riêng để tính `riskScore`. |
| **PlayerActionHistory** | Audit log vĩnh viễn — `AdminActionType` enum + `Metadata` JSON snapshot. Phase 7 cooling-off admin extend (`ExtendAsync`) ghi vào đây. Player risk dashboard parse `Metadata` JSON để extract signals. |
| **PlayerAlert** | `PlayerAlert` entity (Phase 8 — BR-RISK-02) — auto-trigger khi `riskScore` vượt ngưỡng 30/50/75 HOẶC multi-account detected. Có `Severity` (`Info`/`Warning`/`Critical`) + `Status` lifecycle (`Open`→`Acknowledged`→`Resolved`/`Dismissed`). Admin review trong **24h SLA** cho Critical. Cooldown 7 ngày giữa 2 alerts cùng user để tránh spam. |

---

## 15. Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-08-11 | Design Team | Initial draft (booking-centric) |
| 2.0 | 2026-08-12 | Design Team + FE sync | **Major refactor:** Reservation as primary entity; terminology mapping; BR alignment with `lobby-booking-deposit-bvc.mdc` §XX; FE-flow reference; **API rewrite `/api/bookings` → `/api/v1/reservations/...`** |
| 2.1 | 2026-08-12 | Backend Team | **P0 — DB-stored time fields:** Rename `ScheduledTime` → `ScheduledStartTime`, add `ScheduledEndTime` (lưu DB, không derive runtime). Bổ sung 2 index `IX_Reservations_ScheduledStartTime_Status` + `IX_Reservations_ScheduledEndTime_Status`. Thêm EC-05 (extension interplay với WalkInWindow). Cập nhật glossary với `ScheduledStartTime` / `ScheduledEndTime` / `ScheduledDurationMinutes`. |
| 2.2 | 2026-08-12 | Backend Team | **Doc audit pass 2:** Fix duplicate EC-05 (Race Condition → EC-06, các EC sau +1). Sửa `LobbyTimeoutJob` references: BR-END-05 + §5.3 + EC-09 + §8 risk row 2 → `AutoReleaseExpiredSessionsJob`. §4.7 NoShow flow → `NoShowDetectionJob`. §7.1 table thêm row "5 | Extension interplay" và đánh lại số 6-11. Fix BR reference: BR-RESV-04 (DB-stored time) → BR-RESV-02 ở §0.2, §2.2, §4.1, §4.4, §4.7, §9.1, glossary; giữ `BR-RESV-04` cho Deposit formula (§3.1 row 188). |
| 2.3 | 2026-08-12 | Backend Team | **Doc audit pass 3:** Fix §10.1 `POST /confirm` body — `quoteSnapshot` (sai) → expected fields schema (CafeId/GameId/PlayDate/TimeSlot/MinPlayers/MaxPlayers/IsPrivate/ExpectedFinalDeposit/IdempotencyKey) khớp với `ReservationConfirmRequestDto` và `docs/api/reservation.md`. Update §6.3 + §10.6 "Phase 4 (2026-Q4) migrate" → "Phase 1 đang migrate". §13.1 Phase 1 scope mở rộng: thêm TD-01/TD-02/deprecate `TimeSlotBookingController`/audit `BookingController`. Phase 4 chuyển thành "gỡ `BookingController` sau khi FE xác nhận". Bump header version 2.2 → 2.3. |
| 2.4 | 2026-08-12 | Backend Team | **Doc audit pass 4:** Fix §9.5 entity name `Transaction` → thực tế là `BvcLedgerEntry` (KHÔNG trộn với `Transaction` payment gateway VND). Thêm `RelatedReservationId?` + giữ `RelatedBookingId` `[Obsolete]`. Doc §9.1 enum fix: bổ sung `Draft/Expired/CancelledByPlayer/CancelledByCafe` (đầy đủ 9 giá trị). §9.1 entity bổ sung field `DepositConfigSnapshot` + `CurrentPlayers` (mirror từ lobby). §10.1 quote body: `gameTemplateId` → `gameId` khớp với `ReservationQuoteRequestDto`. §8.1 risk mitigation: bổ sung 3 index name explicit (thay vì "(cafeId, playDate, timeSlot)" chung chung). |
| 2.5 | 2026-08-12 | Backend Team | **Reservation SoT clarification:** §9.1 đổi title `(ROOT entity mới)` → `(ROOT entity — SOURCE OF TRUTH)` + comment `⭐ ScheduledEndTime chỉ ở Reservation`. §9.2 đổi title `Lobby` → `Lobby (MIRROR entity — chỉ giữ field time cho index/query nhanh)`. Thêm mới §9.7 "Data Ownership & Sync Rules" — ownership table 32 fields giữa Reservation ↔ Lobby, sync rules trong `ConfirmAsync`, query rules (FE/backend phải tuân thủ), future cleanup plan (Phase 2+). Update §1.3 terminology row "Booking lobby" note thêm "Legacy lobby có thể tồn tại với `ReservationId = null`". |
| 2.6 | 2026-08-12 | Backend Team | **Phase 2 + Phase 3 implemented:** §9.6 `KarmaShortPlayRecord` schema cập nhật: thêm `KarmaDelta`, đổi enum `KarmaStatus` → `KarmaRecordStatus`, bỏ `ViolationDate`, thêm `CreatedAt`. §13.1 Phase 1/2/3 mark ✅ Done, Phase 4 status: W7-8. §13.1 Phase 2 scope chi tiết: `WalkInWindow`/`WalkInBooking` entities, `WalkInController`/`WalkInService`, OCC via PostgreSQL `xmin`, `WalkInWindowCleanupJob`. §13.1 Phase 3 scope chi tiết: extension DTOs + `ReservationExtensionService`, extension endpoints (`GET/POST /extend/availability`), `Reservation.ExtensionCount` + `ExtendedEndTime`, `TriggerShortPlayTrackingAsync`, `ReservationNoShowDetectionJob` + `AutoReleaseExpiredSessionsJob`. |
| 2.7 | 2026-08-12 | Backend Team | **Doc audit pass 5:** Fix BR-EXT-03 example typo: "Afternoon 13-18 → max extend 16:00" → "13:00 + 2h = **15:00**". Add Phase 2 cancel endpoint: `POST /api/v1/reservations/walkin/{id}/cancel`. Add Phase 3 early checkout WalkInWindow: §4.4, §4.7 no-show WalkInWindow, EC-09 auto-release WalkInWindow vào Phase 3 scope. |
| 2.8 | 2026-08-12 | Backend Team | **Doc audit pass 6:** Fix BR-REFUND-04/05/06 trong `ExecuteCompleteAndCaptureTransactionAsync` — trước đây luôn capture 100%, giờ đúng: playedRatio < 0.5 → forfeit 100%; 0.5 ≤ ratio < 0.9 → capture 70%, refund 30%; ratio ≥ 0.9 → forfeit 100%. Thêm `WalkInWindowCleanupJob.cs` + đăng ký trong `Program.cs` (§4.4). |
| 2.9 | 2026-08-12 | Backend Team | **BR-REFUND-07 implemented:** Thêm `AdminOverrideRefundRequestDto` + `AdminOverrideRefundResultDto` trong `BoardVerse.Core/DTOs/Reservation/`. Thêm method `AdminOverrideRefundAsync` trong `IReservationService` + `ReservationService`. Thêm endpoint `POST /api/v1/admin/reservations/{id}/override-refund` trong `AdminReservationController`. Test fixes: `TryCreateWalkInWindowAsync_ShouldCreateWindow_WhenEarlyCheckout`, `TryCreateWalkInWindowAsync_ShouldNotCreateWindow_WhenOnTimeEnd`, `TryCreateWalkInWindowAsync_ShouldNotCreateWindow_WhenNoLobbyId` — thêm `UserId` vào `ActiveSessionMember`, fix mock `BeginTransactionAsync`. EC-04 (Walk-in vs Reservation conflict) là POS operational workflow, KHÔNG cần backend change. |
| 3.3 | 2026-08-12 | Backend Team | **Phase 7 implemented (BR-NEW-10 cooling-off):** Thêm `ICoolingOffService` + `CoolingOffService` (detect signals 3 TimeoutFailed/HostCancelled trong 7d HOẶC forfeit > 500 BVC (= 500.000 VND) trong 30d → activate 30d + RiskMultiplier ×2; expire overdue; escalate ×3 multiplier). `CoolingOffJob` background service chạy mỗi 30 phút, batch 100. Admin extend endpoint `POST /api/v1/admin/cooling-off/{userId}/extend` + `IPlayerRiskQueryService` với `GET /api/v1/admin/players/{userId}/risk` (admin-only RiskScore view + signals JSON parsing). Repository: `CountFailuresByTypeForHostAsync` (per-host), `GetActiveCoolingOffWalletsPagedAsync` + `GetActiveWalletsPagedAsync`. Errors: `InCoolingOffCannotCreateFutureLobby`. **27 unit tests pass** (22 cho CoolingOffService validation + 5 cho PlayerRiskQueryService signal parsing). |
| 3.4 | 2026-08-12 | Backend Team | **Doc audit pass 7 — sync Phase 7 cooling-off toàn diện:** §3.7 BR-KARMA +5 rules (BR-KARMA-06/07/08 cooling-off + BR-KARMA-09 BR-RISK-09 user chỉ thấy riskLevel + BR-KARMA-10 BR-RISK-04 AccountStatus 5 trạng thái). §7.1 edge cases +3 (EC-12 cooling-off từ chối future lobby + EC-13 cooling-off expiry race + EC-14 cooling-off escalate overlap). §8.3 row 3 risk mitigation reference BR-NEW-10 cooling-off job. §10.5 +2 Phase 7 admin endpoints (`/cooling-off/{userId}/extend` + `/players/{userId}/risk`). §10.6 note cooling-off KHÔNG phụ thuộc `BookingController`. §13.2 list **49 unit tests** total (4 EC-10 + 1 Deprecation + 10 LateCancel + 12 ActiveSessionBilling + 22 CoolingOff/PlayerRisk). §13.3 +4 monitoring metrics (cooling-off active count, escalation rate, extend rate, risk dashboard latency). §14 glossary +7 terms (RiskScore, RiskMultiplier, RiskLevel, AccountStatus, Cooling-off, Signal BR-RISK-01, PlayerActionHistory). |
| 3.5 | 2026-08-12 | Backend Team | **Threshold fix BR-NEW-10 §XI.1 — forfeit threshold 500.000 BVC → 500 BVC (= 500.000 VND):** Rule gốc có typo lớn (500k BVC ≈ 500 triệu VND ≈ không user thường nào chạm). Đã sửa: rule file `lobby-booking-deposit-bvc.mdc` §XI.1, code `CoolingOffService.cs:24` (constant `ForfeitAmountThreshold = 500L`), tests `CoolingOffServiceTests.cs` (3 cases cập nhật `750.75/600/100` BVC). Doc §3.7 BR-KARMA-07 + §13.1 Phase 7 + §14 Glossary đã sync. Lý do: với cọc per-lobby ~50-250 BVC, threshold 500 BVC (~2-10 forfeit liên tiếp) mới catch repeat-offender thực sự. |
| 3.6 | 2026-08-12 | Backend Team | **Phase 8 implemented (BR-RISK-* — Risk Management & Admin Audit Log):** 3 entities mới (`PlayerAlert` + `PlayerRiskScore` + `RiskScoreHistory`), 3 enums (`PlayerAlertType` + `PlayerAlertSeverity` + `PlayerAlertStatus`), 3 background jobs (`RiskScoreRecomputeJob` hourly + `SuspensionExpiryCheckJob` hourly + `AlertExpiryCleanupJob` daily), 7 admin endpoints mới (`/users/action-history` BR-RISK-05 + `/alerts` list/metrics/acknowledge/resolve/dismiss BR-RISK-02 + `/players/{userId}/risk-history` BR-RISK-11), A-01/A-02 fix `AdminModerationService.PunishUserAsync`/`AdjustKarmaAsync` inject `BoardVerseDbContext` ghi `PlayerActionHistory` audit JSON. **Bug fix schema**: `PlayerActionHistoryConfiguration.HasConversion<int>()` (khớp DB cột `integer`) thay vì `HasConversion<string>()` ban đầu. R-02 `PlayerAccountLink` + `signal_detect_multi_account` job **SKIPPED** per user request. §13.1 +1 Phase 8 row, §10.5 +7 admin endpoints, §13.2 +3 unit tests (52 total), §13.3 +5 monitoring metrics (Open Critical alerts count, review latency, cooldown violations, suspension auto-release, risk recompute latency), §14 +1 glossary term `PlayerAlert`. **16/16 AdminModeration integration tests pass** sau schema fix. |

---

## 16. Approval

| Role | Name | Date | Signature |
|------|------|------|-----------|
| Product Owner | | | |
| Tech Lead | | | |
| QA Lead | | | |
| Operations | | | |
| FE Lead | | | |

---

*Document End*
