# Time-slot Reservation with Fixed End Time - Business Specification

> **Version:** 2.0 (Naming Refactor)
> **Created:** 2026-08-11
> **Revised:** 2026-08-13
> **Status:** Draft
> **Author:** Design Team
>
> **Scope clarification (2026-08-13):** File này mô tả **luồng chính của hệ thống** — "đặt cọc → tạo Lobby" theo atomic transaction (BR-REQUIRED §17.4 trong `lobby-booking-deposit-bvc.mdc`).
>
> Root entity = **`Reservation`**. Flow A (primary, BVC wallet) theo rule `booking-vs-reservation.mdc`. Entity `Booking` legacy chỉ tồn tại ở Flow B (BR-22 per-member SePay deposit) — đã được loại bỏ khỏi file này.
>
> **Terminology mapping (Booking → Reservation)**:
>
> | Tên cũ (sai) | Tên đúng | Ghi chú |
> |---|---|---|
> | `class Booking` | `class Reservation` | Entity root |
> | `BookingStatus` enum | `ReservationStatus` enum | State machine |
> | `String lobbyId` (trong Booking) | **Xóa** — Reservation tự sinh Lobby (1:1) | BR-RESERVATION-01 |
> | `POST /api/bookings` | `POST /api/v1/reservations/confirm` | URL atomic |
> | `BR-BOOK-01..06` | `BR-RES-01..06` | Match rule canonical |
> | `bookings` table | `reservations` table | DB schema |
> | `bookId` / `book_789` | `reservationId` / `res_789` | REST convention |
>
> **Flow liên quan**:
>
> | Flow | URL | Entity | Deposit | Status |
> |---|---|---|---|---|
> | **A — Reservation** (canonical, file này) | `/api/v1/reservations/*` | `Reservation` + `Lobby` | BVC wallet (heldBalance) | **Active** |
> | **B — Booking legacy** (BR-22 SePay) | `/api/bookings/*` | `Booking` + `BookingDeposit` | SePay gateway (per-member) | Maintained for backward compat |
>
> Tham chiếu: `docs/api/reservation.md`, `docs/api/booking.md`, rule `booking-vs-reservation.mdc`.

---

## 1. Tổng quan nghiệp vụ

### 1.1 Mô tả

Time-slot reservation với end time cố định là mô hình đặt chỗ mà mỗi reservation phải có **thời gian bắt đầu** và **thời gian kết thúc** được xác định trước. Ghế sẽ được release ngay khi player về sớm (early checkout), cho phép nhóm khác đặt vào slot trống đó.

> Flow chính của hệ thống: **đặt cọc → tạo Reservation + Lobby (atomic)**. Theo BR-REQUIRED §17.4: 1 transaction duy nhất hold BVC + giữ ghế + giữ game copy + tạo reservation + tạo lobby. Sau commit → publish lobby → recruit members.

**Ràng buộc quan trọng về thời gian (BR-RES-07, BR-RES-08, BR-RES-09):**

| Ràng buộc | Mô tả |
|---|---|
| **Bắt buộc có start + end** | Mỗi reservation phải có cả `startTime` và `endTime`. **Không có open-ended reservation** (chỉ có start time). |
| **End time cùng ngày start time** | `endTime.date` phải bằng `startTime.date` (cùng `playDate`). **Không cho phép reservation cross midnight** (vd: 22:00 → 02:00 ngày hôm sau). |
| **End time tự động** | User **không nhập free-form end time**. Backend tự resolve từ `playDate + timeSlot` qua `CafeSchedule.GetEndTime(timeSlot)`. TimeSlot enum cố định 4 giá trị (morning/afternoon/evening/night). |

Xem chi tiết + error messages + ví dụ ở [Section 3.1 — BR-RES-07..09](#31-reservation-rules-br-res).

### 1.2 So sánh với mô hình cũ

| Khía cạnh | Mô hình cũ (Không end time) | Mô hình mới (Có end time) |
|-----------|-----------------------------|---------------------------|
| Đặt chỗ | Chỉ có start time | Start time + End time |
| Ghế release | Khi session kết thúc hoặc no-show | Khi: (1) Kết thúc, (2) Early checkout, (3) No-show |
| Walk-in | Không thể (ghế luôn bị hold) | Có thể (nếu có slot trống) |
| Revenue | Không tối ưu | Tối ưu hơn |
| Complexity | Thấp | Cao |

### 1.3 Mục tiêu

1. **Tối đa hóa revenue** - Không lãng phí slot trống
2. **Công bằng cho player** - Ai đặt trước được ưu tiên trong slot của họ
3. **Đơn giản hóa vận hành** - Staff có công cụ rõ ràng để quản lý
4. **Trải nghiệm tốt** - Player biết trước thời gian, không bị "đuổi" bất ngờ

---

## 2. Các thành phần chính

### 2.1 Slot Structure

```
Giờ hoạt động: 07:00 - 22:00 (hoặc theo cấu hình quán)

Cấu trúc Slot:
┌─────────────────────────────────────────────────────────────────────┐
│  07:00  │  09:00  │  12:00  │  14:00  │  17:00  │  20:00  │  22:00 │
├─────────┼─────────┼─────────┼─────────┼─────────┼─────────┼────────┤
│ Slot 1  │ Slot 2  │ Slot 3  │ Slot 4  │ Slot 5  │ Slot 6  │        │
│ 07-09   │ 09-12   │ 12-14   │ 14-17   │ 17-20   │ 20-22   │        │
└─────────┴─────────┴─────────┴─────────┴─────────┴─────────┴────────┘

Mỗi slot có:
- Start time: Giờ bắt đầu
- End time: Giờ kết thúc
- Duration: Thời lượng (tính bằng giờ)
- Max players: Số ghế tối đa trong slot
- Status: Available / Booked / In-Progress / Completed / Cancelled
```

### 2.2 Reservation States

> **Lưu ý:** Diagram dưới dùng terminology rút gọn để dễ hiểu. State machine chính thức là `ReservationStatus` enum (Section 9.1).

```
┌──────────┐     ┌──────────┐     ┌──────────┐     ┌──────────┐
│ HOLDING  │────▶│ CHECKED  │────▶│ IN       │────▶│ COMPLETED│
│ (đã cọc)│     │ -IN      │     │ PROGRESS │     │          │
└──────────┘     └──────────┘     └──────────┘     └──────────┘
     │                │                │                │
     │                │                │                │
     ▼                ▼                ▼                ▼
┌──────────┐     ┌──────────┐     ┌──────────┐     ┌──────────┐
│CANCELLED │     │  NO-SHOW │     │ EARLY    │     │  REFUND  │
│_BY_PLAYER│     │          │     │ CHECKOUT │     │ PROCESSED│
└──────────┘     └──────────┘     └──────────┘     └──────────┘
```

**Chi tiết từng state (Reservation — Flow A):**

| State | Mô tả | Ghế | Deposit (BVC) |
|-------|-------|-----|---------------|
| HOLDING | Đã trừ BVC, lobby đang tuyển | Held | Trong heldBalance |
| CONFIRMED | Lobby đạt minPlayers trước deadline (BR-LOBBY-02) | Held | Vẫn held |
| CHECKED_IN | Đã quét QR tại quán, ActiveSession tạo | In Use | Vẫn held |
| IN_PROGRESS | Đang chơi | In Use | Vẫn held |
| COMPLETED | Kết thúc đúng giờ, ActiveSession.PAID | Released | DEPOSIT_CAPTURE → doanh thu quán |
| EARLY_CHECKOUT | Về sớm, ActiveSession.PAID sớm | Released | DEPOSIT_RELEASE 30% nếu playedRatio ≥ 50% |
| NO_SHOW | Không check-in sau grace | Released | DEPOSIT_FORFEIT 100% |
| CANCELLED_BY_PLAYER | Host hủy theo BR-REFUND-02 | Released | DEPOSIT_RELEASE theo milestone |
| CANCELLED_BY_CAFE | Cafe hủy đặt chỗ | Released | DEPOSIT_RELEASE 100% (BR-REFUND-04) |
| EXPIRED | Lobby timeoutFailed, hoàn 100% | Released | DEPOSIT_RELEASE 100% (BR-REFUND-01) |

### 2.3 Walk-in Window

Khi một session kết thúc sớm (early checkout), hệ thống sẽ tạo một **Walk-in Window** cho phép walk-in đặt vào slot trống đó.

```
Timeline Example:

Nhóm A đặt 13:00 - 18:00 (5 tiếng)
  │
  │ Check-in 13:00
  ▼
┌───────────────────────────────────────────────────────────────┐
│                                                               │
│ 13:00          15:00                    18:00                │
│    │              │                        │                  │
│    ▼              ▼                        ▼                  │
│  Start ───────────┤ Early Checkout         │ End              │
│                    │                        │                  │
│                    └────────────────────────┘                  │
│                           │                                     │
│                           ▼                                     │
│              Walk-in Window: 15:00 - 18:00                    │
│              (3 tiếng trống, có thể bán cho walk-in)          │
│                                                               │
└───────────────────────────────────────────────────────────────┘
```

---

## 3. Business Rules

### 3.1 Reservation Rules (BR-RES)

| ID | Rule | Mô tả |
|----|------|-------|
| BR-RES-01 | Start time ≥ Now + 30 min | Không cho đặt quá sát giờ (cho phép buffer tối thiểu cho lobby recruitment) |
| BR-RES-02 | End time ≤ Start time + 6 hours | Giới hạn tối đa 6 tiếng/slot |
| BR-RES-03 | No overlap | Slot mới không được overlap với slot đã đặt |
| BR-RES-04 | Min deposit ≥ 30% giá slot | Deposit theo BR-DEPOSIT-02 (ratePerPerson × maxPlayers × riskMultiplier) |
| BR-RES-05 | Max 2 active reservations/user | Theo BR-USER-LIMIT-01 (1 host + 1 member, tổng 2 lobby active) |
| BR-RES-06 | Atomic check before confirm | Luôn kiểm tra BVC balance + seat inventory + game copy availability trong transaction trước khi xác nhận |
| **BR-RES-07** | **Start time + End time bắt buộc** | **Mọi reservation phải có cả `startTime` và `endTime`. Không cho phép tạo reservation chỉ có `startTime` (open-ended).** |
| **BR-RES-08** | **End time cùng ngày với Start time** | **`endTime` phải cùng `playDate` với `startTime`. Không cho phép reservation cross midnight (vd: 22:00 → 02:00 ngày hôm sau).** |
| **BR-RES-09** | **TimeSlot auto-resolve endTime** | **End time KHÔNG do user nhập free-form. Backend tự resolve từ `playDate + timeSlot` qua `CafeSchedule.GetEndTime(timeSlot)` theo BR-NEW-15a. TimeSlot enum cố định 4 giá trị (morning/afternoon/evening/night) — đảm bảo end time cùng ngày và hợp lệ.** |

**Validation chain cho quote (POST /api/v1/reservations/quote):**

```
Input: { playDate, timeSlot, preferredStartTime? }
   │
   ▼
1. Validate playDate hợp lệ + không quá xa (BR-NEW-01)
   │
   ▼
2. Auto-resolve endTime = playDate + CafeSchedule.GetEndTime(timeSlot)
   │
   ▼
3. Validate BR-RES-08: endTime.date == playDate
   │ → Nếu cross-day → throw BR-RES-08 (cùng ngày)
   │
   ▼
4. Validate BR-RES-01: startTime ≥ Now + 30 min
   │
   ▼
5. Validate BR-RES-02: endTime ≤ startTime + 6 hours
   │ → TimeSlot đã cap rồi (max slot = 5 tiếng, night 19:00-24:00)
   │
   ▼
6. Trả quote với startTime + endTime đã resolve
```

**Error messages (tiếng Việt, throw từ service):**

| BR | Code | Message |
|---|---|---|
| BR-RES-07 | `RESERVATION_REQUIRES_START_AND_END` | "Đặt chỗ bắt buộc phải có thời gian bắt đầu và thời gian kết thúc. Vui lòng chọn khung giờ (morning/afternoon/evening/night) để hệ thống tự tính giờ kết thúc." |
| BR-RES-08 | `RESERVATION_END_TIME_DIFFERENT_DAY` | "Thời gian kết thúc phải cùng ngày với thời gian bắt đầu. Đặt chỗ qua đêm không được hỗ trợ — vui lòng chọn 2 reservation riêng (tối nay và ngày mai)." |
| BR-RES-09 | `RESERVATION_INVALID_TIMESLOT` | "Khung giờ không hợp lệ. Chỉ chấp nhận: morning (09:00-13:00), afternoon (13:00-18:00), evening (18:00-23:00), night (19:00-24:00)." |

**Lý do BR-RES-08 (end time cùng ngày):**

| Lý do | Chi tiết |
|---|---|
| **BR-15 (boardverse.mdc)** | Hóa đơn cá nhân tính theo phiên chơi liên tục — cross-day phá vỡ công thức `time × hourlyRate` |
| **Giờ hoạt động quán** | Giờ hoạt động chuẩn 07:00-22:00, riêng night slot 19:00-24:00 — đảm bảo end time ≤ 24:00 cùng ngày |
| **Walk-in Window** | Release ghế tạo Walk-in Window cùng ngày — cross-day phức tạp hóa release logic |
| **ActiveSession billing** | `playedMinutes` đơn giản = `endTime - startTime` — cross-day nhân thêm 1 ngày, dễ bug |
| **BR-DEPOSIT-02** | `finalDeposit` tính theo maxPlayers × timeSlot duration — không có cơ chế cross-day pricing |
| **UX đơn giản** | User chọn 1 calendar date + 1 timeSlot — backend tự resolve end time, không có edge case input |

**Ví dụ hợp lệ vs không hợp lệ:**

```
✅ Hợp lệ (BR-RES-07, BR-RES-08, BR-RES-09):
  { playDate: "2026-08-15", timeSlot: "AFTERNOON" }
  → startTime = 2026-08-15 13:00
  → endTime   = 2026-08-15 18:00 (cùng ngày ✅)
  → duration  = 5 tiếng

✅ Hợp lệ (night slot):
  { playDate: "2026-08-15", timeSlot: "NIGHT" }
  → startTime = 2026-08-15 19:00
  → endTime   = 2026-08-15 24:00 (cùng ngày ✅, 5 tiếng)
  → duration  = 5 tiếng

❌ Không hợp lệ (BR-RES-08 cross-day):
  startTime = 2026-08-15 22:00
  endTime   = 2026-08-16 02:00 (khác ngày ❌)
  → Hệ thống từ chối. User phải đặt 2 reservation riêng.

❌ Không hợp lệ (BR-RES-07 open-ended):
  { playDate: "2026-08-15", timeSlot: "EVENING", endTime: null }
  → Hệ thống từ chối. TimeSlot EVENING đã resolve sẵn endTime = 23:00.

❌ Không hợp lệ (BR-RES-09 invalid timeSlot):
  { playDate: "2026-08-15", timeSlot: "LATE_NIGHT" }
  → Hệ thống từ chối. Chỉ chấp nhận 4 enum BR-NEW-15.
```

### 3.2 Check-in Rules (BR-CHECKIN)

| ID | Rule | Mô tả |
|----|------|-------|
| BR-CHECKIN-01 | Within 15 min of start | Check-in phải trong vòng 15 phút trước/sau start time |
| BR-CHECKIN-02 | Auto no-show after 30 min | Không check-in sau 30 phút → auto NO_SHOW |
| BR-CHECKIN-03 | Record actual start | Lưu StartedAt = thời điểm check-in thực tế |

### 3.3 End Session Rules (BR-END)

| ID | Rule | Mô tả |
|----|------|-------|
| BR-END-01 | Record actual end | Lưu ActualEndAt = thời điểm kết thúc thực tế |
| BR-END-02 | Calculate played ratio | playedRatio = (ActualEndAt - StartedAt) / (ScheduledEndTime - StartTime) |
| BR-END-03 | On-time end | playedRatio ≥ 90% → Không refund (forfeit 100%) |
| BR-END-04 | Late end | playedRatio > 100% → Tính thêm tiền giờ (nếu có slot) |
| BR-END-05 | Grace period | Grace period 30 phút sau end time (không tính extra) |

### 3.4 Refund Rules (BR-REFUND)

| ID | Rule | Mô tả |
|----|------|-------|
| BR-REFUND-01 | Cancel > 24h before | Refund 100% deposit |
| BR-REFUND-02 | Cancel < 24h before | Refund 0% deposit (forfeit) |
| BR-REFUND-03 | No-show | Refund 0% deposit (forfeit) |
| BR-REFUND-04 | Early checkout < 50% played | Refund 0% deposit (forfeit) |
| BR-REFUND-05 | Early checkout ≥ 50% played | Refund 30% deposit |
| BR-REFUND-06 | Early checkout ≥ 90% played | Refund 0% deposit (treated as on-time) |
| BR-REFUND-07 | Staff can override | Staff có quyền override refund trong trường hợp đặc biệt |

### 3.5 Extension Rules (BR-EXT)

| ID | Rule | Mô tả |
|----|------|-------|
| BR-EXT-01 | Check slot availability | Trước khi extend, kiểm tra slot kế có trống không |
| BR-EXT-02 | No conflict with next reservation | Không cho extend nếu có reservation kế conflict |
| BR-EXT-03 | Max extension = 2 hours | Tổng thời gian extend không quá 2 tiếng |
| BR-EXT-04 | Payment required | Extension phải thanh toán thêm |
| BR-EXT-05 | Partial extension allowed | Có thể extend 1 phần slot (không nhất thiết full slot kế) |

### 3.6 Walk-in Rules (BR-WALKIN)

| ID | Rule | Mô tả |
|----|------|-------|
| BR-WALKIN-01 | Only from released slots | Walk-in chỉ được đặt vào Walk-in Window |
| BR-WALKIN-02 | Min 30 min window | Walk-in Window phải ≥ 30 phút mới được tạo |
| BR-WALKIN-03 | POS only | Chỉ POS staff mới được tạo walk-in |
| BR-WALKIN-04 | No deposit | Walk-in không cần deposit |
| BR-WALKIN-05 | First-come-first-served | Ai đến trước được ngồi trước |
| BR-WALKIN-06 | Cancel by POS | POS có thể cancel walk-in nếu cần |

### 3.7 Karma System Rules (BR-KARMA)

| ID | Rule | Mô tả |
|----|------|-------|
| BR-KARMA-01 | Track short play | Track mỗi lần player đặt ≥ 4h nhưng chơi < 50% |
| BR-KARMA-02 | Warning after 3 times | shortPlayCount ≥ 3 → Warning notification |
| BR-KARMA-03 | Restriction after 5 times | shortPlayCount ≥ 5 → Không cho đặt slot ≥ 4h |
| BR-KARMA-04 | Reset monthly | Karma reset sau 30 ngày không vi phạm |
| BR-KARMA-05 | Appeal process | Player có thể appeal nếu bị restriction |

---

## 4. Luồng nghiệp vụ chi tiết

### 4.1 Luồng đặt cọc tạo Lobby (Reservation Atomic Flow)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              RESERVATION + LOBBY ATOMIC FLOW (BR-REQUIRED §17.4)             │
└─────────────────────────────────────────────────────────────────────────────┘

Host (Player)                                     System                          Cafe/Staff
  │                                                 │                              │
  │  1. Chọn ngày, timeSlot (morning/afternoon/     │                              │
  │     evening/night), maxPlayers, minPlayers      │                              │
  │  ─────────────────────────────────────────────▶ │                              │
  │                                                 │                              │
  │  2. Trả quote: depositAmount (BVC),             │                              │
  │     availableBalance, missingAmount             │                              │
  │  ◀────────────────────────────────────────────── │                              │
  │                                                 │                              │
  │  3. Nếu thiếu BVC → mở TopUpPage                │                              │
  │     Sau khi đủ BVC → bấm "Xác nhận đặt cọc"     │                              │
  │  ─────────────────────────────────────────────▶ │                              │
  │                                                 │                              │
  │                                                 │ 4. BEGIN TRANSACTION          │
  │                                                 │    - Validate quote (chưa     │
  │                                                 │      hết hạn 5 phút)          │
  │                                                 │    - Validate user            │
  │                                                 │      (BR-USER-LIMIT-01..05)   │
  │                                                 │    - Lock seat_inventory      │
  │                                                 │      SELECT FOR UPDATE        │
  │                                                 │    - Lock game_inventory      │
  │                                                 │    - Validate cooling-off     │
  │                                                 │      (BR-NEW-10)              │
  │                                                 │                              │
  │                                                 │ 5. Trừ BVC:                  │
  │                                                 │    availableBalance -= X      │
  │                                                 │    heldBalance    += X       │
  │                                                 │    INSERT ledger:             │
  │                                                 │      DEPOSIT_HOLD             │
  │                                                 │                              │
  │                                                 │ 6. INSERT reservation         │
  │                                                 │      status = holding         │
  │                                                 │    INSERT lobby               │
  │                                                 │      status = pendingActivation│
  │                                                 │      hoặc pendingCafeApproval │
  │                                                 │      (BR-NEW-11)              │
  │                                                 │    UPDATE seat_inventory      │
  │                                                 │      HeldSeats += maxPlayers  │
  │                                                 │    UPDATE game_inventory      │
  │                                                 │      HeldCopies += 1          │
  │                                                 │                              │
  │                                                 │ 7. COMMIT TRANSACTION         │
  │                                                 │    - Outbox event:            │
  │                                                 │      LobbyActivated           │
  │                                                 │    - Publish SignalR          │
  │                                                 │    - Push notification        │
  │                                                 │      player phù hợp           │
  │                                                 │                              │
  │  8. Nhận confirmation (reservationCode,         │                              │
  │     QR code, thông tin lobby)                   │                              │
  │  ◀────────────────────────────────────────────── │                              │
  │                                                 │                              │
  │  9. Lobby tuyển members (open → viable → full)  │                              │
  │     hoặc đến recruitmentDeadline                │                              │
```

### 4.2 Luồng check-in (Check-in Flow)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           CHECK-IN FLOW                                      │
└─────────────────────────────────────────────────────────────────────────────┘

Player                                          System                          Cafe/Staff
  │                                                 │                              │
  │  1. Đến quán, mở app                            │                              │
  │  ─────────────────────────────────────────────▶ │                              │
  │                                                 │                              │
  │                                                 │ 2. Validate reservation       │
  │                                                 │    - Đúng ngày/giờ?          │
  │                                                 │    - Đã cancel chưa?         │
  │                                                 │    - Đã check-in chưa?       │
  │                                                 ◀───────────────               │
  │                                                 │                              │
  │  3. Scan QR / Nhấn "Check-in"                   │                              │
  │  ─────────────────────────────────────────────▶ │                              │
  │                                                 │                              │
  │                                                 │ 4. Update status: IN_PROGRESS│
  │                                                 │    - Set StartedAt = now      │
  │                                                 │    - Set ScheduledEndTime     │
  │                                                 │                              │
  │  5. Nhận thông báo: "Check-in thành công"      │                              │
  │     "Thời gian kết thúc: 18:00"                  │                              │
  │  ◀────────────────────────────────────────────── │                              │
  │                                                 │                              │
  │  6. Ngồi xuống, bắt đầu chơi                    │                              │
  │                                                 │                              │
```

### 4.3 Luồng kết thúc bình thường (On-time End Flow)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           ON-TIME END FLOW                                  │
└─────────────────────────────────────────────────────────────────────────────┘

Player                                          System                          Cafe/Staff
  │                                                 │                              │
  │                                                 │                              │
  │  17:55 - Staff nhắc: "Còn 5 phút nữa hết giờ"  │                              │
  │                                                 │                              │
  │                                                 │                              │
  │  18:00 - Hết giờ                                │                              │
  │  ─────────────────────────────────────────────▶ │                              │
  │                                                 │                              │
  │                                                 │ 1. Tính playedRatio           │
  │                                                 │    playedRatio = 100%         │
  │                                                 │                              │
  │                                                 │ 2. Xác định: ON-TIME END     │
  │                                                 │    → Forfeit 100% deposit    │
  │                                                 │                              │
  │                                                 │ 3. Update: COMPLETED         │
  │                                                 │    - Set ActualEndAt          │
  │                                                 │    - Release ghế             │
  │                                                 │                              │
  │                                                 │ 4. Tạo transaction           │
  │                                                 │                              │
  │  5. Staff hướng dẫn thanh toán tiền giờ       │                              │
  │  ◀────────────────────────────────────────────── │                              │
  │                                                 │                              │
  │  6. Thanh toán tiền giờ                         │                              │
  │  ─────────────────────────────────────────────▶ │                              │
  │                                                 │                              │
  │  7. Nhận biên nhận, ra về                       │                              │
  │                                                 │                              │
```

### 4.4 Luồng kết thúc sớm (Early Checkout Flow)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           EARLY CHECKOUT FLOW                               │
└─────────────────────────────────────────────────────────────────────────────┘

Player                                          System                          Cafe/Staff
  │                                                 │                              │
  │  15:00 - Player về sớm                         │                              │
  │  ─────────────────────────────────────────────▶ │                              │
  │                                                 │                              │
  │                                                 │ 1. POS nhấn "End Session"    │
  │                                                 │                              │
  │                                                 │ 2. Tính playedRatio           │
  │                                                 │    StartedAt = 13:00          │
  │                                                 │    ActualEndAt = 15:00        │
  │                                                 │    ScheduledDuration = 5h    │
  │                                                 │    ActualDuration = 2h        │
  │                                                 │    playedRatio = 40%          │
  │                                                 │                              │
  │                                                 │ 3. Xác định: EARLY CHECKOUT  │
  │                                                 │    40% < 50% → Forfeit 100%  │
  │                                                 │    (hoặc refund 30% nếu ≥50%) │
  │                                                 │                              │
  │                                                 │ 4. Update: EARLY_CHECKOUT    │
  │                                                 │    - Set ActualEndAt          │
  │                                                 │    - Release ghế              │
  │                                                 │    - Tạo Walk-in Window       │
  │                                                 │      [15:00 - 18:00]          │
  │                                                 │                              │
  │  5. Hiển thị refund preview                     │                              │
  │     "Bạn đã chơi 2h/5h (40%)"                   │                              │
  │     "Refund: 0đ"                               │                              │
  │     "Vui lòng thanh toán tiền giờ: 2h × 10k"  │                              │
  │  ◀────────────────────────────────────────────── │                              │
  │                                                 │                              │
  │  6. Thanh toán tiền giờ                         │                              │
  │  ─────────────────────────────────────────────▶ │                              │
  │                                                 │                              │
  │  7. Nhận biên nhận + refund (nếu có)            │                              │
  │  ◀────────────────────────────────────────────── │                              │
  │                                                 │                              │
  │  8. Staff hướng dẫn ra về                       │                              │
  │                                                 │                              │
```

### 4.5 Luồng extension (Extension Flow)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           EXTENSION FLOW                                    │
└─────────────────────────────────────────────────────────────────────────────┘

Player                                          System                          Cafe/Staff
  │                                                 │                              │
  │  17:30 - Player muốn chơi thêm                  │                              │
  │                                                 │                              │
  │  17:30 - Staff kiểm tra slot kế                 │                              │
  │                                                 │ 1. Kiểm tra: [18:00-22:00]   │
  │                                                 │    - Có reservation nào không?│
  │                                                 │    - Có trống không?          │
  │                                                 │                              │
  │                                                 │ 2. Xác định: CÓ THỂ EXTEND  │
  │                                                 │    (nếu slot kế trống)       │
  │                                                 │                              │
  │  17:31 - Staff báo: "Có thể thêm 30 phút"     │                              │
  │  ◀────────────────────────────────────────────── │                              │
  │                                                 │                              │
  │  17:32 - Player đồng ý                          │                              │
  │  ─────────────────────────────────────────────▶ │                              │
  │                                                 │                              │
  │                                                 │ 3. Tính extra payment        │
  │                                                 │    30 phút × hourly rate     │
  │                                                 │                              │
  │  4. Player thanh toán extra                      │                              │
  │  ─────────────────────────────────────────────▶ │                              │
  │                                                 │                              │
  │                                                 │ 5. Update ScheduledEndTime  │
  │                                                 │    [18:00] → [18:30]         │
  │                                                 │    - Extend count +1         │
  │                                                 │                              │
  │  6. Xác nhận extension                           │                              │
  │  ◀────────────────────────────────────────────── │                              │
  │                                                 │                              │
```

**Trường hợp KHÔNG THỂ EXTEND:**

```
Player muốn extend, nhưng slot kế đã có người đặt:

┌─────────────────────────────────────────────────────────────────────────────┐
│  13:00 ─────── 18:00 ─────── 22:00                                         │
│       │ Nhóm A    │ Nhóm B  │                                              │
│       │ muốn      │ đặt rồi  │                                              │
│       │ extend    │          │                                              │
│       │ 18:00→20  │          │                                              │
│       │           │          │                                              │
│       └───────────┴──────────┘                                              │
│                                                                              │
│  Staff thông báo: "Xin lỗi, slot 18:00-22:00 đã có người đặt"              │
│  "Bạn có thể chơi thêm 30 phút (đến 18:30) trước khi nhường chỗ"          │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 4.6 Luồng walk-in (Walk-in Flow)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           WALK-IN FLOW                                      │
└─────────────────────────────────────────────────────────────────────────────┘

Walk-in                                         System                          POS Staff
  │                                                 │                              │
  │  Đến quán, hỏi có chỗ không                      │                              │
  │  ─────────────────────────────────────────────▶ │                              │
  │                                                 │                              │
  │                                                 │ 1. POS kiểm tra Walk-in      │
  │                                                 │    Windows hiện có           │
  │                                                 │    - [15:00-18:00]: 3 ghế    │
  │                                                 │    - [12:00-14:00]: 4 ghế    │
  │                                                 │                              │
  │  2. POS hỏi: "Bạn muốn chơi bao lâu?"           │                              │
  │  ◀────────────────────────────────────────────── │                              │
  │                                                 │                              │
  │  3. Walk-in: "Khoảng 2 tiếng"                    │                              │
  │  ─────────────────────────────────────────────▶ │                              │
  │                                                 │                              │
  │                                                 │ 4. Kiểm tra:                 │
  │                                                 │    - Window nào phù hợp?     │
  │                                                 │    - Có đủ ghế không?        │
  │                                                 │                              │
  │  5. POS tạo WalkInBooking               │                              │
  │  ─────────────────────────────────────────────▶ │                              │
  │                                                 │                              │
  │                                                 │ 6. Validate:                │
  │                                                 │    - Window ≥ 30 phút?       │
  │                                                 │    - Còn đủ ghế?             │
  │                                                 │                              │
  │                                                 │ 7. Tạo:                     │
  │                                                 │    - WalkInBooking           │
  │                                                 │    - Update window seats     │
  │                                                 │                              │
  │  8. Thông báo: "Có chỗ 15:00-18:00"            │                              │
  │  ◀────────────────────────────────────────────── │                              │
  │                                                 │                              │
  │  9. Walk-in đồng ý                               │                              │
  │  ─────────────────────────────────────────────▶ │                              │
  │                                                 │                              │
  │                                                 │ 10. Confirm + check-in       │
  │                                                 │                              │
  │  11. Walk-in ngồi xuống, chơi                   │                              │
  │                                                 │                              │
```

### 4.7 Luồng no-show (No-show Flow)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           NO-SHOW FLOW                                     │
└─────────────────────────────────────────────────────────────────────────────┘

                                                  System                          Cafe/Staff
                                                    │                              │
                                                    │  13:30 - Đã 30 phút sau     │
                                                    │     start time (13:00)       │
                                                    │                              │
                                                    │ 1. Kiểm tra: Đã check-in?   │
                                                    │    → CHƯA                    │
                                                    │                              │
                                                    │ 2. Auto update: NO_SHOW     │
                                                    │    - Release ghế             │
                                                    │    - Forfeit 100% deposit    │
                                                    │    - Tạo Walk-in Window      │
                                                    │      [13:00 - 18:00]         │
                                                    │                              │
                                                    │ 3. Gửi notification         │
                                                    │    "Reservation của bạn bị   │
                                                    │     hủy do không check-in"  │
                                                    │                              │
                                                    │                              │
                                                    │                              │
```

### 4.8 Luồng cancel (Cancel Flow)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           CANCEL FLOW                                       │
└─────────────────────────────────────────────────────────────────────────────┘

Player                                          System                          Cafe/Staff
  │                                                 │                              │
  │  1. Mở app, chọn reservation muốn hủy    │                              │
  │  ─────────────────────────────────────────────▶ │                              │
  │                                                 │                              │
  │                                                 │ 2. Kiểm tra:                 │
  │                                                 │    - Đã check-in chưa?       │
  │                                                 │    - Còn bao lâu đến giờ?    │
  │                                                 │                              │
  │  3. Xác nhận hủy                                │                              │
  │  ─────────────────────────────────────────────▶ │                              │
  │                                                 │                              │
  │                                                 │ 3. Tính refund:              │
  │                                                 │    Case A: > 24h trước       │
  │                                                 │           → Refund 100%      │
  │                                                 │    Case B: < 24h trước       │
  │                                                 │           → Refund 0%        │
  │                                                 │                              │
  │                                                 │ 4. Update: CANCELLED         │
  │                                                 │    - Release ghế              │
  │                                                 │    - Process refund           │
  │                                                 │                              │
  │  5. Xem kết quả refund                          │                              │
  │  ◀────────────────────────────────────────────── │                              │
  │                                                 │                              │
```

---

## 5. Ưu điểm

### 5.1 Ưu điểm cho Quán (Cafe)

| # | Ưu điểm | Chi tiết |
|---|----------|----------|
| 1 | **Tối đa hóa revenue** | Ghế được release ngay khi player về → không lãng phí thời gian |
| 2 | **Predictable planning** | Biết trước slot nào có khách → staff appropriately |
| 3 | **Giảm wasted capacity** | Không còn "ghế treo" khi player đã về |
| 4 | **Better walk-in opportunity** | Có thể sell slot trống cho walk-in |
| 5 | **Inventory management** | Biết trước cần game nào, bao nhiêu bàn |
| 6 | **Staffing optimization** | Biết giờ nào đông/vắng → phân bổ nhân viên tốt hơn |
| 7 | **Conflict resolution** | Rõ ràng ai được ưu tiên trong slot nào |

### 5.2 Ưu điểm cho Player

| # | Ưu điểm | Chi tiết |
|---|----------|----------|
| 1 | **Bảo đảm có chỗ** | Đặt trước → chắc chắn có ghế |
| 2 | **Fair hơn** | Ai đặt trước được ưu tiên |
| 3 | **Refund công bằng** | Đã chơi ≥ 50% → được refund 30% |
| 4 | **Rõ ràng thời gian** | Biết trước check-in, check-out |
| 5 | **Extension possible** | Có thể extend nếu slot kế trống |
| 6 | **Karma warning** | Biết trước nếu bị karma flag |

### 5.3 Ưu điểm cho Hệ thống

| # | Ưu điểm | Chi tiết |
|---|----------|----------|
| 1 | **Clean data model** | Mỗi slot có start/end rõ ràng |
| 2 | **Easy reporting** | Biết chính xác utilization rate |
| 3 | **Automated workflows** | Auto no-show, auto release, auto refund |
| 4 | **Scalable** | Dễ mở rộng thêm features |

---

## 6. Nhược điểm

### 6.1 Nhược điểm cho Quán

| # | Nhược điểm | Giải thích |
|---|-------------|------------|
| 1 | **Phức tạp hơn** | Phải quản lý nhiều luồng hơn |
| 2 | **Staff training** | Nhân viên phải học cách xử lý nhiều case |
| 3 | **Edge cases** | Nhiều trường hợp đặc biệt cần xử lý |
| 4 | **Conflict management** | Phải xử lý dispute khi extension bị từ chối |
| 5 | **POS complexity** | POS UI phức tạp hơn với nhiều options |

### 6.2 Nhược điểm cho Player

| # | Nhược điểm | Giải thích |
|---|-------------|------------|
| 1 | **Phải commit thời gian** | Không thể "chơi đến khi nào vui thì thôi" |
| 2 | **Không spontaneous** | Phải đặt trước, không thể đến ngẫu nhiên |
| 3 | **Pressure khi hết giờ** | Bị "nhắc nhở" khi sắp hết giờ |
| 4 | **Không extend được** | Nếu slot kế đã có người |
| 5 | **Deposit complexity** | Phải hiểu refund rules |

### 6.3 Nhược điểm cho Hệ thống

| # | Nhược điểm | Giải thích |
|---|-------------|------------|
| 1 | **Technical complexity** | Nhiều entities, relationships phức tạp |
| 2 | **Migration risk** | Phải migrate data cũ |
| 3 | **Testing effort** | Nhiều edge cases cần test |
| 4 | **Race conditions** | 2 POS cùng tạo walk-in |

---

## 7. Các trường hợp ngoại lệ và xử lý

### 7.1 Tổng quan Edge Cases

| # | Edge Case | Xử lý | Ai xử lý |
|---|-----------|--------|----------|
| 1 | Extension conflict | Từ chối, suggest partial | System/POS |
| 2 | Refund dispute | Override by staff | POS |
| 3 | Karma abuse | Warning → Restriction | System |
| 4 | Walk-in vs reservation conflict | Reservation ưu tiên | System |
| 5 | Race condition (2 POS) | Optimistic concurrency | System |
| 6 | Cancel after check-in | Refund 30% (if ≥50% played) | System/POS |
| 7 | Extension via midnight | Split into 2 sessions | System |
| 8 | Staff forgot to end | Auto-release after grace | System |
| 9 | Game longer than slot | Suggest extend/cut short | POS |
| 10 | Player disputes played time | Staff judgment | POS |

---

### 7.2 Chi tiết từng Edge Case

---

#### EC-01: Extension Conflict

**Mô tả:** Player muốn extend nhưng slot kế đã có người đặt.

**Timeline:**
```
Slot 1: 13:00-18:00 (Nhóm A)
Slot 2: 18:00-22:00 (Nhóm B - đã đặt)
         ↑
         A muốn extend đến đây
```

**Xử lý:**

```
Bước 1: Kiểm tra slot kế
         → Slot 2 đã có Nhóm B

Bước 2: Tính toán overlap
         → Overlap: 18:00-22:00

Bước 3: Đề xuất options:
         
         Option A: Partial extension (nếu B chưa check-in)
           - Nhóm A có thể extend đến khi B check-in
           - Thông báo: "Bạn có thể thêm X phút trước khi 
             nhường chỗ cho nhóm đặt sau"

         Option B: Early extension (nếu B đồng ý)
           - B đồng ý đến muộn hơn
           - Rare case, cần B approve

         Option C: Không extend
           - Nhóm A phải rời đi đúng giờ
           - Staff thông báo trước 30 phút

Bước 4: Áp dụng option được chọn
```

**Staff Actions:**
- Thông báo trước 30 phút nếu không thể extend
- Không đuổi khách đột ngột
- Offer alternative: "Quán có khu vực chờ/café"

---

#### EC-02: Refund Dispute

**Mô tả:** Player không đồng ý với refund amount.

**Timeline:**
```
Player đặt 13:00-18:00 (5 tiếng, deposit 50k)
Check-in 13:00
Về 15:30 (played 2.5h = 50%)
→ Refund: 30% = 15k

Player dispute: "Tôi đã chơi 2.5 tiếng, ít nhất cho refund 50%"
```

**Xử lý:**

```
Bước 1: Hiển thị calculation chi tiết
         - Đặt: 13:00-18:00 (5 tiếng)
         - Check-in: 13:00
         - Actual end: 15:30
         - Played: 2.5 tiếng = 50%
         - Rule: ≥50% played → 30% refund
         - Refund: 15k

Bước 2: Kiểm tra có special case không?
         - Player có karma flag không?
         - Player là VIP không?
         - Có technical issue không?

Bước 3: Staff decision
         
         Option A: Giữ nguyên (no override)
           - Giải thích rule, không override
           - "Xin lỗi, đây là chính sách"

         Option B: Override (special case)
           - Staff có quyền override refund
           - Cần ghi reason
           - Log vào audit trail

Bước 4: Thông báo player
```

**Staff Override Policy:**
- Staff chỉ được override trong trường hợp đặc biệt
- Phải ghi rõ lý do
- Supervisor approval nếu > 50k
- Report hàng ngày về overrides

---

#### EC-03: Karma Abuse

**Mô tả:** Player đặt slot dài nhưng chơi ít, lặp đi lặp lại.

**Timeline:**
```
Lần 1: Đặt 5 tiếng, chơi 1 tiếng → Karma +1
Lần 2: Đặt 4 tiếng, chơi 1 tiếng → Karma +1
Lần 3: Đặt 6 tiếng, chơi 2 tiếng → Karma +1
        ↑
        Warning: "Bạn đã đặt slot dài nhưng chơi ít 3 lần"
Lần 4: Đặt 5 tiếng, chơi 1 tiếng → Karma +1
        ↑
        Restriction: "Bạn không thể đặt slot ≥ 4 tiếng"
```

**Xử lý:**

```
Level 1: Normal (0-2 violations)
  - Không có action
  - Track trong background

Level 2: Warning (3-4 violations)
  - Gửi notification khi đặt
  - "Bạn đã có 3 lần đặt slot dài nhưng chơi ít. 
    Vui lòng đặt thời gian phù hợp với nhu cầu thực tế."

Level 3: Restriction (5+ violations)
  - Không cho đặt slot ≥ 4 tiếng
  - Chỉ cho đặt slot ≤ 3 tiếng
  - Gửi thông báo: "Tài khoản của bạn tạm thời bị giới hạn 
    đặt slot dài. Giới hạn sẽ được gỡ sau 30 ngày."

Level 4: Appeal
  - Player có thể appeal qua support
  - Nếu approved → reset karma
  - Nếu rejected → continue restriction
```

**Karma Calculation:**
```
eligibleForKarma = 
  AND(
    scheduledDuration >= 4 hours,
    playedRatio < 0.5,
    isEarlyCheckout == true
  )

if eligibleForKarma:
  karmaScore += 1
```

> Lưu ý: `playedRatio` được tính dựa trên `Reservation` (entity root), không phải `Booking` legacy. Reservation status khi early checkout → `expired` (grace) hoặc `completed` (sau POS end).

---

#### EC-04: Walk-in vs Reservation Conflict

**Mô tả:** Walk-in đang ngồi, reservation mới đến và muốn slot đó.

**Timeline:**
```
15:00 - Nhóm A (reservation) về sớm, release ghế
15:30 - Walk-in đến, ngồi vào ghế trống
16:00 - Nhóm B đặt mới (13:00-18:00, reservation mới)
16:30 - Nhóm B đến check-in
         ↑
         Muốn slot 13:00-18:00
         Nhưng walk-in đang ngồi
```

**Xử lý:**

```
Priority Rule: RESERVATION LUÔN LUÔN ƯU TIÊN HƠN WALK-IN

Bước 1: Xác định priority
         - Nhóm B: Valid reservation (13:00-18:00)
         - Walk-in: Không có reservation, đang ngồi nhờ

Bước 2: Kiểm tra Walk-in Window
         - Walk-in đang trong Walk-in Window không?
         - Window: [15:00-18:00]
         - Walk-in đến 15:30 → TRONG WINDOW

Bước 3: Quyết định
         
         Case A: Walk-in chưa check-in
           - Không tạo WalkInBooking
           - Không có conflict

         Case B: Walk-in đã check-in (trong window)
           - Walk-in được giữ ghế đến hết window
           - Hoặc đến khi B check-in (tùy policy)
           - Staff thông báo B: "Ghế đang có walk-in, 
             vui lòng chờ X phút"

         Case C: Walk-in đã check-in + B đến check-in
           - Walk-in phải nhường ghế
           - Walk-in được refund full (không phạt)
           - Staff tìm ghế khác cho walk-in
           - Nếu không có ghế → Walk-in phải ra
```

**Staff Decision Flow:**
```
Walk-in đang ngồi + Reservation mới đến
           │
           ▼
    Còn ghế khác không?
    │
    ├── Có → Di chuyển walk-in sang ghế mới
    │        → OK
    │
    └── Không → Walk-in phải ra trong 15 phút
                → Refund full cho walk-in
                → Đây là exceptional case
                → Ghi log cho analysis
```

---

#### EC-05: Race Condition (2 POS cùng tạo walk-in)

**Mô tả:** 2 nhân viên cùng bấm "Add Walk-in" cùng lúc, overselling.

**Timeline:**
```
Walk-in Window: 15:00-18:00 (3 tiếng, 4 ghế trống)
Còn: 4 ghế

POS 1: 15:00 - Bấm "Add Walk-in" (nghĩ còn 4 ghế)
POS 2: 15:00 - Bấm "Add Walk-in" (cùng lúc, nghĩ còn 4 ghế)

→ Cả 2 nghĩ còn 4 ghế
→ POS 1 thêm 3 khách (3 ghế)
→ POS 2 thêm 3 khách (3 ghế)
→ Total: 6 khách cho 4 ghế ← OVERSELL
```

**Xử lý:**

```
Solution: Optimistic Concurrency Control (OCC)

Bước 1: POS gửi request với:
         - windowId
         - requestedSeats
         - expectedAvailableSeats (4)
         - version/timestamp

Bước 2: Server kiểm tra:
         SELECT availableSeats, version 
         FROM walk_in_windows 
         WHERE id = windowId

Bước 3: So sánh:
         if (requestedSeats <= availableSeats AND
             version == expectedVersion):
           - UPDATE availableSeats -= requestedSeats
           - version += 1
           - Success
         else:
           - Failure: "Ghế không còn đủ"
           - POS hiển thị thông báo

Bước 4: POS xử lý failure:
         - Hiển thị: "Còn X ghế, bạn có muốn đặt X ghế không?"
         - Hoặc: "Hết ghế cho window này"
```

**Implementation:**
```sql
UPDATE walk_in_windows 
SET 
  available_seats = available_seats - :requested,
  version = version + 1
WHERE 
  id = :windowId 
  AND available_seats >= :requested
  AND version = :expectedVersion

-- Nếu rows_affected = 0 → Race condition detected
```

---

#### EC-06: Cancel After Check-in

**Mô tả:** Player đã check-in rồi, sau đó muốn cancel.

**Timeline:**
```
13:00 - Player check-in (đặt 13:00-18:00)
14:00 - Player muốn cancel
        ↑
        Đã check-in rồi, có refund không?
```

**Xử lý:**

```
Bước 1: Xác định state
         - Đã check-in: YES
         - Đã chơi: 1 tiếng = 20%
         - playedRatio = 20%

Bước 2: Áp dụng rules
         - playedRatio < 50% → Forfeit 100%
         - BR-REFUND-04 applies

Bước 3: Staff explanation
         "Bạn đã check-in và đã chơi 1 tiếng. 
          Theo chính sách, bạn đã sử dụng 20% thời gian 
          (dưới 50%), nên deposit sẽ không được refund."

Bước 4: Options cho player
         - Tiếp tục chơi đến hết giờ
         - Kết thúc sớm (vẫn forfeit)
         - Staff có thể override trong special cases
```

**Special Cases:**
```
Case A: Technical issue (wifi, game broken)
  → Staff override → Refund 30% or more
  → Ghi reason: "Technical issue"

Case B: Emergency (bệnh, việc gấp)
  → Staff override → Refund 30%
  → Ghi reason: "Emergency"

Case C: Khác (tùy staff judgment)
  → Staff override if reasonable
  → Ghi reason + Supervisor notification
```

---

#### EC-07: Extension qua Midnight

**Mô tả:** Player đặt đến 23:00, muốn extend sang ngày mai.

**Timeline:**
```
Đặt: 20:00 - 23:00
Muốn extend: đến 01:00 (ngày mai)
     ↑
     Cross midnight
```

**Xử lý:**

```
Bước 1: Kiểm tra ngày
         - Current slot: hôm nay 20:00-23:00
         - Extension: ngày mai 23:00-01:00
         → Đây là 2 ngày khác nhau

Bước 2: Kiểm tra ngày mai
         - Ngày mai có mở cửa không?
         - Có slot available không?
         - Price khác không?

Bước 3: Xử lý
         
         Option A: Từ chối (simplest)
           "Xin lỗi, không thể extend qua ngày. 
            Vui lòng đặt lại cho ngày mai."

         Option B: Tạo 2 sessions (complex)
           Session 1: Hôm nay 20:00-23:00 (existing)
           Session 2: Ngày mai 23:00-01:00 (new reservation)
           → Yêu cầu player đặt lại cho ngày mai
           → Có thể giữ deposit/price discount

Bước 4: Thông báo
         "Không thể extend qua ngày. 
          Bạn có muốn đặt lại cho ngày mai không?"
```

---

#### EC-08: Staff Forgot to End Session

**Mô tả:** Player đã về, nhưng staff quên bấm "End Session".

**Timeline:**
```
15:00 - Player về (đặt 13:00-18:00)
15:30 - Staff nhớ ra, bấm End
         ↑
         30 phút bị "block" vô tình
```

**VÀ:**

```
15:00 - Player về
17:00 - Staff mới nhớ ra
         ↑
         2 tiếng bị "block"
```

**Xử lý:**

```
Auto-Release Mechanism:

Bước 1: ScheduledEndTime = 18:00
Bước 2: Grace period = 30 phút
         → Auto-release at 18:30

Bước 3: Nếu after 18:30 mà session vẫn ACTIVE:
         - Kiểm tra: Player đã check-in chưa?
         - Nếu YES + no activity:
           → Auto end session
           → Set ActualEndAt = ScheduledEndTime
           → Process refund (nếu early)
           → Create Walk-in Window

Bước 4: Notify staff
         "Session đã được auto-ended do không có activity. 
          Player đã về sớm. Refund đã được process."

Bước 5: Staff verification
         - Staff kiểm tra lại
         - Confirm hoặc adjust nếu cần
```

**Activity Detection (nếu có):**
```
Có thể dùng:
- POS check-out action
- Player app "I'm leaving" button
- Table sensor (nếu có hardware)

Nếu không có detection:
→ Phụ thuộc staff
→ Backup: Auto-release after grace period
```

---

#### EC-09: Game Longer Than Slot

**Mô tả:** Game đang chơi dở, nhưng hết giờ.

**Timeline:**
```
Đặt: 14:00-17:00
Game started: 14:30 (Catan - 60-120 phút)
17:00: Hết giờ, game mới chơi được 30 phút
         ↑
         Game không thể kết thúc trong 30 phút
```

**Xử lý:**

```
Staff Assessment:

Bước 1: Xác định tình hình
         - Game đang chơi: Catan
         - Estimated remaining: 60-90 phút
         - Slot còn lại: 0 phút
         - Next slot: 17:00-20:00 (có người đặt)

Bước 2: Kiểm tra options
         Option A: Quick finish (nếu game support)
           - Đề xuất: "Mình finish nhanh không?"
           - Nếu cả nhóm đồng ý → OK

         Option B: Extend 30-60 phút (grace)
           - Cho thêm grace period (0-30 phút)
           - Không tính extra (grace)
           - Không block slot kế

         Option C: Finish game + Leave
           - Cho 60-90 phút để finish
           - Tính extra time (nếu > grace)
           - Next group phải đợi

         Option D: Take break / Resume later
           - Lưu game state
           - Ra ngoài chờ
           - Resume khi có bàn trống
           - (Rare case, complex)

Bước 3: Thông báo next group (nếu có)
         "Ghế của bạn đang có group khác chơi game dài. 
          Dự kiến free sau X phút. Bạn có thể:"
          - Đợi ở quầy
          - Đợi ở khu vực khác
          - Reschedule
```

**Recommendation:**
```
Grace period 30 phút là đủ cho hầu hết games
Nếu game > 30 phút → Staff case-by-case
Priority:
  1. Player đã đặt (current) > Player đặt sau (next)
  2. Nhưng cũng cần consider next group
```

---

#### EC-10: Player Disputes Played Time

**Mô tả:** Player nói đã đến sớm hơn, hoặc về muộn hơn thực tế.

**Timeline:**
```
Đặt: 13:00-18:00
Staff ghi: Start 13:05, End 17:55
Player claim: Start 12:55, End 18:10
         ↑
         Disputing played time
```

**Xử lý:**

```
Bước 1: Verify evidence
         - POS logs (nếu có check-in time)
         - QR scan timestamp
         - Staff memory
         - Game selection logs
         - Other witnesses

Bước 2: Staff judgment
         - Nếu có clear evidence → dùng evidence
         - Nếu không clear → staff judgment

Bước 3: Communicate
         Option A: Staff đúng
           "Theo records, bạn check-in lúc 13:05. 
            Mình sẽ dùng con số này."

         Option B: Player đúng
           "OK, mình điều chỉnh theo timeline của bạn."

         Option C: Compromise
           "Mình lấy trung bình nhé?"

Bước 4: Document
         - Ghi reason
         - Log dispute
         - Backup nếu cần escalate
```

**Prevention:**
```
- QR scan timestamp là definitive
- Staff nên scan khi player đến
- Clear communication khi check-in
- App notification khi start/end
```

---

## 8. Rủi ro

### 8.1 Rủi ro kỹ thuật

| # | Rủi ro | Xác suất | Tác động | Mitigation |
|---|--------|----------|----------|------------|
| 1 | Race condition (oversell) | Trung bình | Cao | OCC implementation |
| 2 | Migration failure | Thấp | Rất cao | Backup + rollback plan |
| 3 | Data inconsistency | Thấp | Cao | Transaction + validation |
| 4 | Performance degradation | Thấp | Trung bình | Indexing + caching |
| 5 | Integration failure (payment) | Thấp | Cao | Retry + fallback |

### 8.2 Rủi ro vận hành

| # | Rủi ro | Xác suất | Tác động | Mitigation |
|---|--------|----------|----------|------------|
| 1 | Staff không training đủ | Cao | Cao | Training program + documentation |
| 2 | Staff forget to end | Trung bình | Trung bình | Auto-release mechanism |
| 3 | Staff override abuse | Thấp | Cao | Audit trail + approval |
| 4 | Customer complaint spike | Trung bình | Trung bình | Clear policy + communication |
| 5 | Conflict between customers | Trung bình | Cao | Clear priority rules |

### 8.3 Rủi ro kinh doanh

| # | Rủi ro | Xác suất | Tác động | Mitigation |
|---|--------|----------|----------|------------|
| 1 | Player chuyển sang competitor | Trung bình | Cao | Good UX + fair rules |
| 2 | Revenue không tăng như kỳ vọng | Trung bình | Trung bình | Realistic targets + monitoring |
| 3 | Abuse / Exploitation | Thấp | Trung bình | Karma system + monitoring |
| 4 | Negative reviews | Trung bình | Cao | Good service + quick response |

### 8.4 Chi tiết rủi ro quan trọng

---

#### Risk-1: Race Condition (Oversell)

**Mô tả:** 2 POS cùng tạo walk-in, dẫn đến oversell.

**Xác suất:** Trung bình (đặc biệt khi busy)

**Tác động:** Cao - Khách đến không có chỗ

**Mitigation:**
```sql
-- Sử dụng row-level locking
BEGIN TRANSACTION;

SELECT available_seats, version 
FROM walk_in_windows 
WHERE id = ? FOR UPDATE;

-- Validate + Update
UPDATE walk_in_windows 
SET available_seats = available_seats - ?,
    version = version + 1
WHERE id = ? AND available_seats >= ?;

COMMIT;
```

**Backup Plan:**
- POS show real-time available seats
- Hard limit không cho oversell
- Manual override by supervisor if needed

---

#### Risk-2: Migration Failure

**Mô tả:** Khi upgrade lên model mới, data migration fail.

**Xác suất:** Thấp (nếu có test kỹ)

**Tác động:** Rất cao - Mất data, downtime

**Mitigation:**
```
1. Full backup trước migration
2. Test migration trên staging trước
3. Incremental migration (từng phần)
4. Rollback plan rõ ràng
5. Scheduled maintenance window
6. Go-live support team
```

---

#### Risk-3: Staff Override Abuse

**Mô tả:** Staff lạm dụng override để favor friends/family.

**Xác suất:** Thấp

**Tác động:** Cao - Revenue loss + unfair

**Mitigation:**
```
1. Audit trail cho all overrides
2. Supervisor approval nếu > threshold
3. Regular review overrides
4. Automated anomaly detection
5. Clear policy + consequences
```

---

#### Risk-4: Customer Complaint Spike

**Mô tả:** Khi launch, nhiều player không hiểu rules mới.

**Xác suất:** Cao (change management issue)

**Tác động:** Trung bình - Negative reviews

**Mitigation:**
```
1. Clear in-app communication
2. FAQ / Help section
3. Staff trained to explain
4. Grace period (soft launch)
5. Quick response to complaints
6. Monitor social media
```

---

## 9. Data Models

### 9.1 Reservation Entity

```dart
class Reservation {
  // Identifiers (Root entity của Flow A)
  String id;
  String userId;            // HostId — người trả cọc
  String cafeId;
  // KHÔNG có lobbyId — Reservation tự sinh Lobby (1:1) theo BR-REQUIRED §17.4

  // Slot Information (TimeSlot-based — KHÔNG phải free-form start/end)
  // BR-RES-07: startTime + endTime là bắt buộc, KHÔNG nullable
  // BR-RES-08: endTime.date == playDate (cùng ngày, không cross midnight)
  // BR-RES-09: endTime được auto-resolve từ playDate + timeSlot qua CafeSchedule
  DateOnly playDate;        // BR-NEW-04: chỉ ngày, không giờ
  TimeSlot timeSlot;        // morning | afternoon | evening | night (BR-NEW-15)
  TimeOnly? preferredStartTime;  // Optional, trong [timeSlot.startTime, timeSlot.endTime]
  DateTime startTime;       // = playDate + timeSlot.startTime (+ preferredStartTime nếu có)
  DateTime endTime;         // = playDate + timeSlot.endTime (auto-resolve, BR-NEW-15a)
  DateTime recruitmentDeadline;  // = startTime - leadTimeMinutes
  
  // Lobby config
  int minPlayers;
  int maxPlayers;
  
  // TimeSlot tự resolve end time từ CafeSchedule (BR-NEW-15a)
  // DateTime endTime = playDate + timeSlot.endTime
  
  // Seats
  int seatsHeld;            // = maxPlayers (BR-RESERVATION-01)
  
  // Payment (BVC wallet — Flow A, KHÔNG qua SePay)
  long depositAmount;       // BVC, theo BR-DEPOSIT-02 formula
  long availableBalance;    // snapshot lúc confirm
  long heldBalance;         // snapshot lúc confirm
  String? idempotencyKey;   // BR-REQUIRED §17.1
  
  // Deposit snapshot (BR-LOBBY-12 — audit khi cafe đổi config)
  DepositSnapshot depositConfigSnapshot;
  decimal riskMultiplier;   // 1.0 - 2.0 (BR-RISK-03)
  
  // Lobby link (1:1, sinh ra cùng reservation)
  String lobbyId;           // Lobby được tạo atomic cùng reservation
  
  // Status (Reservation state machine — KHÔNG phải Booking legacy)
  ReservationStatus status;
  
  // Session tracking (khi check-in tạo ActiveSession)
  DateTime? checkedInAt;        // Actual check-in time
  DateTime? actualEndAt;        // Actual leave time
  double? playedRatio;          // Calculated: actual / scheduled
  SessionEndReason? endReason;
  
  // Walk-in Window (nếu tạo từ early checkout)
  String? walkInWindowId;
  
  // Audit
  DateTime createdAt;
  DateTime updatedAt;
  String? cancelledBy;
  String? cancelReason;
}

enum ReservationStatus {
  AWAITING_DEPOSIT,  // Quote đã tạo, chờ confirm (chưa trừ BVC)
  HOLDING,           // Đã trừ BVC, lobby đang tuyển (atomic đã commit)
  CONFIRMED,         // Lobby đạt minPlayers trước deadline (BR-LOBBY-02)
  CHECKED_IN,        // POS scan QR, đã vào quán
  IN_PROGRESS,       // Đang chơi (ActiveSession.ACTIVE)
  COMPLETED,         // Kết thúc đúng giờ (ActiveSession.PAID)
  EARLY_CHECKOUT,    // Về sớm (ActiveSession.PAID sớm)
  EXPIRED,           // Lobby timeoutFailed, hoàn 100% BVC
  CANCELLED_BY_PLAYER,   // Host hủy theo BR-REFUND-02
  CANCELLED_BY_CAFE,     // Cafe hủy, hoàn 100% BVC (BR-REFUND-04)
  NO_SHOW,           // Không check-in sau grace (BR-LOBBY-09)
}

// Error codes riêng cho validation startTime/endTime (BR-RES-07, BR-RES-08, BR-RES-09)
// Throw từ ReservationService.QuoteAsync / ConfirmAsync
enum ReservationValidationError {
  RESERVATION_REQUIRES_START_AND_END,    // BR-RES-07
  RESERVATION_END_TIME_DIFFERENT_DAY,    // BR-RES-08
  RESERVATION_INVALID_TIMESLOT,          // BR-RES-09
  RESERVATION_BUFFER_TOO_SHORT,          // BR-LOBBY-01a (cảnh báo)
  RESERVATION_BUFFER_TOO_SMALL,          // BR-LOBBY-01b (từ chối < 60 phút)
}

enum RefundStatus {
  NONE,
  PENDING,
  PROCESSING,
  COMPLETED,
  REJECTED,
}

enum SessionEndReason {
  ON_TIME,        // Kết thúc đúng giờ
  EARLY_LEAVE,    // Về sớm
  EXTENDED,       // Extend quá giờ (qua ReservationExtensionService)
  NO_SHOW,        // Không check-in
  CANCELLED,      // Hủy
  STAFF_ENDED,    // Staff end session
  AUTO_RELEASED,  // System auto release
}

enum TimeSlot {
  MORNING,    // 09:00 - 13:00 (BR-NEW-15)
  AFTERNOON,  // 13:00 - 18:00
  EVENING,    // 18:00 - 23:00
  NIGHT,      // 19:00 - 24:00
}
```

### 9.2 Walk-in Window Entity

```dart
class WalkInWindow {
  // Identifiers
  String id;
  String? sourceReservationId;  // Reservation nào tạo ra window này
  String cafeId;
  
  // Window Information
  DateTime windowStart;
  DateTime windowEnd;
  int totalSeats;
  int availableSeats;
  
  // Version for optimistic concurrency
  int version;
  
  // Status
  WalkInWindowStatus status;
  
  // Audit
  DateTime createdAt;
  DateTime expiresAt;  // Khi nào window hết hiệu lực
}

enum WalkInWindowStatus {
  ACTIVE,    // Có thể nhận walk-in
  PARTIAL,   // Đã có walk-in, còn chỗ
  FULL,      // Đã full
  EXPIRED,   // Hết hiệu lực
  CLOSED,    // Đã đóng
}
```

### 9.3 Walk-in Booking Entity

```dart
class WalkInBooking {
  // Identifiers
  String id;
  String walkInWindowId;
  String cafeId;
  
  // Guest Information (không cần account)
  String guestName;
  String? guestPhone;
  
  // Time
  DateTime startTime;
  DateTime endTime;
  
  // Seats
  int seats;
  
  // Payment
  double hourlyRate;
  double totalAmount;
  PaymentStatus paymentStatus;
  
  // POS
  String? posStaffId;
  
  // Status
  WalkInBookingStatus status;
  
  // Audit
  DateTime createdAt;
  DateTime? cancelledAt;
  String? cancelReason;
}

enum WalkInBookingStatus {
  PENDING,
  CONFIRMED,
  IN_PROGRESS,
  COMPLETED,
  CANCELLED,
  NO_SHOW,
}
```

### 9.4 Refund Transaction Entity

```dart
class RefundTransaction {
  // Identifiers
  String id;
  String reservationId;   // FK tới Reservation (Flow A) — KHÔNG phải Booking legacy
  String userId;
  
  // Amounts
  long originalDeposit;     // BVC
  long refundAmount;        // BVC, trả về availableBalance của host
  long forfeitedAmount;     // BVC, theo BR-REFUND-02
  
  // Calculation
  double playedRatio;
  int playedMinutes;
  int scheduledMinutes;
  
  // Reason
  RefundReason reason;
  String? staffNotes;
  
  // Override
  bool isOverridden;
  String? overrideBy;       // Staff ID
  String? overrideReason;
  
  // Status
  RefundTransactionStatus status;
  
  // Audit
  DateTime createdAt;
  DateTime processedAt;
}

enum RefundReason {
  CANCEL_BEFORE_24H,     // Hủy trước 24h
  CANCEL_AFTER_24H,     // Hủy sau 24h
  EARLY_CHECKOUT,       // Về sớm
  NO_SHOW,              // Không check-in
  TECHNICAL_ISSUE,      // Lỗi kỹ thuật
  EMERGENCY,            // Khẩn cấp
  STAFF_OVERRIDE,       // Staff override
  OTHER,                // Khác
}

enum RefundTransactionStatus {
  PENDING,
  PROCESSING,
  COMPLETED,
  FAILED,
  CANCELLED,
}
```

### 9.5 Karma Record Entity

```dart
class KarmaRecord {
  // Identifiers
  String id;
  String userId;
  String reservationId;   // FK tới Reservation — KHÔNG phải Booking legacy
  
  // Violation Details
  DateTime violationDate;
  int scheduledMinutes;
  int playedMinutes;
  double playedRatio;
  
  // Karma Impact
  int karmaPointsAdded;
  int totalKarmaScore;
  
  // Status
  KarmaStatus status;
  
  // Warning/Restriction
  bool warningSent;
  DateTime? warningSentAt;
  bool restrictionApplied;
  DateTime? restrictionAppliedAt;
  
  // Appeal
  bool appealRequested;
  String? appealReason;
  DateTime? appealReviewedAt;
  String? appealReviewedBy;
  bool? appealApproved;
}

enum KarmaStatus {
  ACTIVE,      // Đang active
  EXPIRED,     // Đã hết 30 ngày, không có violation mới
  CLEARED,     // Đã cleared sau appeal
}

enum KarmaLevel {
  NORMAL,      // 0-2 violations
  WARNING,     // 3-4 violations
  RESTRICTED,  // 5+ violations
}
```

### 9.6 Database Schema Changes

```sql
-- New Tables

-- 1. Add columns to reservations table (BR-RES entity)
-- BR-RES-07: start_time + end_time là bắt buộc (NOT NULL)
-- BR-RES-08: end_time phải cùng ngày start_time (validate trong service, không enforce ở DB)
-- BR-RES-09: end_time auto-resolve từ time_slot
ALTER TABLE reservations ADD COLUMN checked_in_at TIMESTAMP;
ALTER TABLE reservations ADD COLUMN actual_end_at TIMESTAMP;
ALTER TABLE reservations ADD COLUMN played_ratio DECIMAL(5,4);
ALTER TABLE reservations ADD COLUMN end_reason VARCHAR(50);
ALTER TABLE reservations ADD COLUMN karma_score INT DEFAULT 0;
ALTER TABLE reservations ADD COLUMN walk_in_window_id VARCHAR(36);
ALTER TABLE reservations ADD COLUMN time_slot VARCHAR(20) NOT NULL;  -- BR-NEW-15 enum
ALTER TABLE reservations ADD COLUMN play_date DATE NOT NULL;        -- BR-NEW-04
ALTER TABLE reservations ADD COLUMN start_time TIMESTAMP NOT NULL;  -- BR-RES-07
ALTER TABLE reservations ADD COLUMN end_time TIMESTAMP NOT NULL;    -- BR-RES-07 (BR-RES-08 enforced in service)
ALTER TABLE reservations ADD COLUMN recruitment_deadline TIMESTAMP;  -- BR-LOBBY-01
ALTER TABLE reservations ADD COLUMN risk_multiplier DECIMAL(3,2);  -- BR-RISK-03
ALTER TABLE reservations ADD COLUMN idempotency_key VARCHAR(64);   -- BR-REQUIRED §17.1

-- Constraint: end_time >= start_time (BR-RES-02)
ALTER TABLE reservations ADD CONSTRAINT chk_reservation_time_order
  CHECK (end_time >= start_time);

-- 2. Walk-in Windows (BR-WALKIN)
CREATE TABLE walk_in_windows (
    id VARCHAR(36) PRIMARY KEY,
    source_reservation_id VARCHAR(36),  -- Reservation nào tạo ra window này
    cafe_id VARCHAR(36) NOT NULL,
    window_start TIMESTAMP NOT NULL,
    window_end TIMESTAMP NOT NULL,
    total_seats INT NOT NULL,
    available_seats INT NOT NULL,
    version INT DEFAULT 1,
    status VARCHAR(20) DEFAULT 'ACTIVE',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMP,
    
    INDEX idx_cafe_window (cafe_id, window_start, window_end),
    INDEX idx_status (status),
    
    FOREIGN KEY (source_reservation_id) REFERENCES reservations(id)
);

-- 3. Walk-in Bookings (Flow phụ trợ — BR-WALKIN-03 POS only)
CREATE TABLE walk_in_bookings (
    id VARCHAR(36) PRIMARY KEY,
    walk_in_window_id VARCHAR(36) NOT NULL,
    cafe_id VARCHAR(36) NOT NULL,
    guest_name VARCHAR(100) NOT NULL,
    guest_phone VARCHAR(20),
    start_time TIMESTAMP NOT NULL,
    end_time TIMESTAMP NOT NULL,
    seats INT NOT NULL,
    hourly_rate DECIMAL(10,2) NOT NULL,
    total_amount DECIMAL(10,2) NOT NULL,
    payment_status VARCHAR(20) DEFAULT 'PENDING',
    pos_staff_id VARCHAR(36),
    status VARCHAR(20) DEFAULT 'PENDING',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    cancelled_at TIMESTAMP,
    cancel_reason VARCHAR(255),
    
    INDEX idx_window (walk_in_window_id),
    INDEX idx_cafe_time (cafe_id, start_time),
    
    FOREIGN KEY (walk_in_window_id) REFERENCES walk_in_windows(id)
);

-- 4. Refund Transactions (Flow A — BVC refund)
CREATE TABLE refund_transactions (
    id VARCHAR(36) PRIMARY KEY,
    reservation_id VARCHAR(36) NOT NULL,
    user_id VARCHAR(36) NOT NULL,
    original_deposit DECIMAL(10,2) NOT NULL,
    refund_amount DECIMAL(10,2) NOT NULL,
    forfeited_amount DECIMAL(10,2) NOT NULL,
    played_ratio DECIMAL(5,4),
    played_minutes INT,
    scheduled_minutes INT,
    reason VARCHAR(50) NOT NULL,
    staff_notes TEXT,
    is_overridden BOOLEAN DEFAULT FALSE,
    override_by VARCHAR(36),
    override_reason TEXT,
    status VARCHAR(20) DEFAULT 'PENDING',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    processed_at TIMESTAMP,
    
    INDEX idx_reservation (reservation_id),
    INDEX idx_user (user_id),
    
    FOREIGN KEY (reservation_id) REFERENCES reservations(id)
);

-- 5. Karma Records
CREATE TABLE karma_records (
    id VARCHAR(36) PRIMARY KEY,
    user_id VARCHAR(36) NOT NULL,
    reservation_id VARCHAR(36) NOT NULL,
    violation_date TIMESTAMP NOT NULL,
    scheduled_minutes INT NOT NULL,
    played_minutes INT NOT NULL,
    played_ratio DECIMAL(5,4) NOT NULL,
    karma_points_added INT NOT NULL,
    total_karma_score INT NOT NULL,
    status VARCHAR(20) DEFAULT 'ACTIVE',
    warning_sent BOOLEAN DEFAULT FALSE,
    warning_sent_at TIMESTAMP,
    restriction_applied BOOLEAN DEFAULT FALSE,
    restriction_applied_at TIMESTAMP,
    appeal_requested BOOLEAN DEFAULT FALSE,
    appeal_reason TEXT,
    appeal_reviewed_at TIMESTAMP,
    appeal_reviewed_by VARCHAR(36),
    appeal_approved BOOLEAN,
    
    INDEX idx_user_karma (user_id, status),
    INDEX idx_violation_date (violation_date),
    
    FOREIGN KEY (reservation_id) REFERENCES reservations(id)
);
```

---

## 10. API Endpoints

### 10.1 Reservation APIs (Flow A — canonical, theo rule `booking-vs-reservation.mdc`)

```dart
// POST /api/v1/reservations/quote
// Tạo quote trước khi confirm — user chỉ gửi playDate + timeSlot, backend tự resolve startTime/endTime
// BR-RES-07: endTime bắt buộc (không open-ended)
// BR-RES-08: endTime cùng ngày startTime
// BR-RES-09: endTime auto-resolve từ timeSlot qua CafeSchedule
{
  "cafeId": "cafe_123",
  "gameId": "game_catan_456",
  "playDate": "2026-08-15",          // BR-NEW-04: chỉ ngày
  "timeSlot": "AFTERNOON",            // BR-NEW-15 enum — 4 giá trị cố định
  "preferredStartTime": "13:00",      // optional, trong [timeSlot.startTime, timeSlot.endTime]
  "minPlayers": 2,
  "maxPlayers": 4
}
Response: {
  "quoteId": "quote_001",
  "reservationFee": { "depositAmount": 50000, "currency": "BVC" },
  "expiresAt": "2026-08-14T12:10:00Z",  // quote hết hạn 5 phút
  "timeSlot": {                         // Backend trả về start/end đã resolve
    "playDate": "2026-08-15",
    "timeSlot": "AFTERNOON",
    "startTime": "2026-08-15T13:00:00Z",  // backend resolve
    "endTime": "2026-08-15T18:00:00Z",    // backend resolve (cùng ngày ✅)
    "durationMinutes": 300
  },
  "lobbyConfig": { "minPlayers": 2, "maxPlayers": 4 },
  "cafeAvailability": { "availableSeats": 12, "gameCopiesAvailable": 3 },
  "userWallet": { "availableBalance": 80000, "heldBalance": 0, "missingAmount": 0 }
}

// POST /api/v1/reservations/confirm
// Atomic: trừ BVC + giữ ghế + giữ game + tạo Reservation + tạo Lobby
// BR-REQUIRED §17.4 — 1 transaction duy nhất
{
  "quoteId": "quote_001",
  "idempotencyKey": "host_uuid_4f7c..."  // BR-REQUIRED §17.1
}
Response: {
  "reservationId": "res_789",
  "reservationCode": "AB3K9P2",          // 8-char alphanumeric, POS lookup
  "lobbyId": "lobby_888",
  "status": "HOLDING",                    // ReservationStatus enum
  "lobbyStatus": "PENDING_ACTIVATION",    // LobbyStatus enum
  "depositAmount": 50000,
  "heldBalance": 50000,
  "qrCode": "...",
  "confirmationDetails": {
    "cafe": "BoardVerse Cafe",
    "playDate": "2026-08-12",
    "timeSlot": "AFTERNOON",
    "startTime": "13:00",
    "endTime": "18:00",                   // Auto-resolve từ CafeSchedule
    "minPlayers": 2,
    "maxPlayers": 4,
    "deposit": "50,000 BVC",
    "recruitmentDeadline": "2026-08-11T18:00:00Z"
  }
}

// GET /api/v1/reservations/{id}
// Xem chi tiết reservation (host/member/admin)
Response: {
  "reservationId": "res_789",
  "status": "HOLDING",
  "lobbyId": "lobby_888",
  "lobbyStatus": "OPEN",
  "currentPlayers": 1,   // chỉ host
  "minPlayers": 2,
  "maxPlayers": 4,
  "playDate": "2026-08-12",
  "timeSlot": "AFTERNOON",
  "depositAmount": 50000,
  "createdAt": "2026-08-11T12:05:00Z"
}

// POST /api/v1/reservations/{id}/cancel
// Host hủy theo BR-REFUND-02 (24h/6h milestone) hoặc BR-REFUND-03 (grace 15 phút)
Response: {
  "reservationId": "res_789",
  "status": "CANCELLED_BY_PLAYER",
  "refund": {
    "eligible": true,
    "refundAmount": 50000,    // BVC về availableBalance
    "forfeitedAmount": 0,
    "percentage": 1.0,
    "reason": "CANCEL_BEFORE_24H"  // hoặc CANCEL_GRACE_PERIOD
  }
}

// GET /api/v1/reservations/{id}/availability-check
// Check availability trước khi host confirm (gọi từ FE)
{
  "playDate": "2026-08-12",
  "timeSlot": "AFTERNOON",
  "maxPlayers": 4
}
Response: {
  "available": true,
  "availableSeats": 12,
  "availableGameCopies": 3,
  "conflictingReservations": []
}

// POST /api/v1/reservations/{id}/check-in
// Host quét QR tại quán → tạo ActiveSession
Response: {
  "reservationId": "res_789",
  "status": "CHECKED_IN",
  "checkedInAt": "2026-08-12T13:02:00Z",
  "scheduledEndTime": "2026-08-12T18:00:00Z",
  "activeSessionId": "session_777",
  "seatsHeld": 4
}

// POST /api/v1/reservations/{id}/extend
// Extend reservation qua slot kế (nếu available)
// Hiện tại dùng ReservationExtensionService (planned)
// Tạm thời flow B vẫn qua BookingExtensionController — sẽ migrate
{
  "requestedEndTime": "2026-08-12T19:00:00Z"
}
Response: {
  "available": true,
  "extraMinutes": 60,
  "extraCharge": 6000,  // BVC
  "conflictReservation": null
}
OR
Response: {
  "available": false,
  "reason": "SLOT_CONFLICT",
  "conflictReservation": {
    "id": "res_101",
    "playDate": "2026-08-12",
    "timeSlot": "EVENING"
  },
  "suggestions": [
    "Extend 30 minutes (until 18:30)",
    "New reservation needed for evening slot"
  ]
}

// POST /api/v1/reservations/{id}/end
// POS action — end ActiveSession, tính refund theo playedRatio
{
  "actualEndTime": "2026-08-12T15:30:00Z",
  "endReason": "EARLY_LEAVE"
}
Response: {
  "reservationId": "res_789",
  "status": "EARLY_CHECKOUT",
  "playedRatio": 0.5,
  "playedMinutes": 150,
  "scheduledMinutes": 300,
  "refund": {
    "eligible": true,
    "amount": 15000,        // BVC refund
    "percentage": 0.3,
    "reason": "EARLY_CHECKOUT_50_PLUS"
  },
  "walkInWindow": {
    "created": true,
    "windowId": "window_999",
    "windowStart": "2026-08-12T15:30:00Z",
    "windowEnd": "2026-08-12T18:00:00Z",
    "availableSeats": 4
  }
}
```

### 10.2 Walk-in APIs

```dart
// GET /api/pos/walk-in-windows
// Get available walk-in windows for cafe
{
  "cafeId": "cafe_123",
  "date": "2026-08-12",
}
Response: {
  "windows": [
    {
      "windowId": "window_999",
      "windowStart": "2026-08-12T15:30:00Z",
      "windowEnd": "2026-08-12T18:00:00Z",
      "availableSeats": 4,
      "sourceReservation": "Nhóm A (đặt 13:00-18:00, về sớm)",
    }
  ]
}

// POST /api/pos/walk-in-bookings
// Create walk-in booking (POS only)
{
  "windowId": "window_999",
  "guestName": "Nguyễn Văn B",
  "guestPhone": "0901234567",
  "startTime": "2026-08-12T15:30:00Z",
  "endTime": "2026-08-12T17:30:00Z",
  "seats": 3,
}
Response: {
  "walkInBookingId": "walkin_888",
  "status": "CONFIRMED",
  "totalAmount": 60000,
  "seatsRemaining": 1,
}

// POST /api/pos/walk-in-bookings/{id}/check-in
// Check-in walk-in
Response: {
  "walkInBookingId": "walkin_888",
  "status": "IN_PROGRESS",
  "startedAt": "2026-08-12T15:30:00Z",
}

// POST /api/pos/walk-in-bookings/{id}/end
// End walk-in session
{
  "actualEndTime": "2026-08-12T17:30:00Z",
}
Response: {
  "walkInBookingId": "walkin_888",
  "status": "COMPLETED",
  "totalAmount": 60000,
  "paymentStatus": "PENDING",
}
```

### 10.3 POS Session APIs

```dart
// GET /api/pos/sessions/active
// Get all active sessions for cafe (handle cả Reservation + WalkInBooking)
Response: {
  "sessions": [
    {
      "sessionId": "session_777",
      "reservationId": "res_789",       // Flow A — Reservation
      "type": "RESERVATION",
      "lobbyName": "Catan Room",
      "playDate": "2026-08-12",
      "timeSlot": "AFTERNOON",
      "startTime": "2026-08-12T13:00:00Z",
      "scheduledEnd": "2026-08-12T18:00:00Z",
      "actualEnd": null,
      "seats": 4,
      "status": "IN_PROGRESS",
      "timeRemaining": "2h 30m",
      "isNearEnd": false,
    },
    {
      "walkInBookingId": "walkin_888",  // Walk-in flow
      "type": "WALK_IN",
      "guestName": "Nguyễn Văn B",
      "startTime": "2026-08-12T15:30:00Z",
      "endTime": "2026-08-12T17:30:00Z",
      "seats": 3,
      "status": "IN_PROGRESS",
      "timeRemaining": "1h",
      "isNearEnd": false,
    }
  ]
}

// GET /api/pos/sessions/{id}/warning
// Get warning info for near-end sessions
Response: {
  "sessionId": "session_777",
  "reservationId": "res_789",
  "timeUntilEnd": "5 minutes",
  "warningLevel": "YELLOW",  // YELLOW, ORANGE, RED
  "message": "Còn 5 phút nữa hết giờ",
  "actions": [
    {
      "type": "EXTEND",
      "available": true,
      "options": ["30 minutes", "1 hour"],
    },
    {
      "type": "END",
      "available": true,
    }
  ]
}

// POST /api/pos/sessions/{id}/force-end
// Force end session (staff action)
{
  "reason": "STAFF_ENDED",
  "actualEndTime": "2026-08-12T17:45:00Z",
  "staffNotes": "Player requested early end"
}
Response: {
  "sessionId": "session_777",
  "reservationId": "res_789",
  "status": "EARLY_CHECKOUT",
  // ... standard end response
}
```

### 10.4 Karma APIs

```dart
// GET /api/users/{id}/karma
// Get user karma status
Response: {
  "userId": "user_123",
  "karmaScore": 4,
  "karmaLevel": "WARNING",
  "violations": [
    {
      "date": "2026-08-10",
      "reservationId": "res_001",   // FK tới Reservation, không phải Booking
      "playedRatio": 0.3,
      "karmaPoints": 1,
    },
    // ... more violations
  ],
  "restrictions": {
    "restricted": true,
    "restrictionType": "MAX_4_HOURS",
    "appliedAt": "2026-08-11T10:00:00Z",
    "expiresAt": "2026-09-10T10:00:00Z",
  },
  "warnings": {
    "warningSent": true,
    "warningSentAt": "2026-08-08T10:00:00Z",
  }
}

// POST /api/users/{id}/karma/appeal
// Appeal karma restriction
{
  "reason": "Tôi có việc đột xuất cần về sớm 3 lần đó",
  "supportingDocuments": ["medical_cert.pdf"],
}
Response: {
  "appealId": "appeal_555",
  "status": "PENDING",
  "estimatedReviewTime": "24 hours",
}

// GET /api/karma/analytics
// Get karma analytics for admin
{
  "cafeId": "cafe_123",
  "dateRange": {
    "from": "2026-08-01",
    "to": "2026-08-11",
  }
}
Response: {
  "totalViolations": 45,
  "repeatOffenders": 12,
  "restrictedUsers": 5,
  "appeals": {
    "total": 3,
    "approved": 1,
    "rejected": 2,
  },
  "topViolations": [
    {"ratio": "20-30%", "count": 15},
    {"ratio": "10-20%", "count": 20},
    {"ratio": "<10%", "count": 10},
  ]
}
```

---

## 11. UX/UI Design

### 11.1 App - Reservation Flow

```
┌─────────────────────────────────────────┐
│  📅 Chọn ngày (playDate — BR-NEW-04)     │
│  ┌─────┬─────┬─────┬─────┬─────┬─────┐ │
│  │ 10  │ 11  │ 12* │ 13  │ 14  │ 15  │ │
│  │ T6  │ T7  │ CN  │ T2  │ T3  │ T4  │ │
│  └─────┴─────┴─────┴─────┴─────┴─────┘ │
│                                         │
│  🕐 Chọn TimeSlot (BR-NEW-15)           │
│  ┌─────────────────────────────────┐    │
│  │  AFTERNOON (13:00 - 18:00)   ▼  │    │
│  └─────────────────────────────────┘    │
│                                         │
│  👥 Số người:                            │
│  • Min: 2 người                          │
│  • Max: 4 người                          │
│                                         │
│  📊 Thời gian chơi: 5 tiếng (auto)       │
│  💰 Deposit: 50,000 BVC (formula)         │
│  💡 Refund: theo BR-REFUND-02             │
│                                         │
│  ┌─────────────────────────────────┐    │
│  │      XÁC NHẬN ĐẶT CỌC          │    │
│  └─────────────────────────────────┘    │
└─────────────────────────────────────────┘
```

> **Lưu ý UX**: User không nhập endTime — hệ thống tự resolve từ `playDate + timeSlot` qua `CafeSchedule.GetEndTime(timeSlot)`. Nếu có `preferredStartTime` thì chỉ là tham chiếu cho member, không thay thế deadline.

### 11.2 App - My Reservation

```
┌─────────────────────────────────────────┐
│  📋 Đặt chỗ của tôi (My Reservations)   │
│                                         │
│  ─── Sắp tới ───                       │
│  ┌─────────────────────────────────┐    │
│  │ 🏠 BoardVerse Cafe              │    │
│  │ 📅 12/08/2026 (Thứ 4)          │    │
│  │ 🕐 AFTERNOON (13:00 - 18:00)    │    │
│  │ 👥 4 người                      │    │
│  │ 💰 Đã đặt cọc: 50,000 BVC      │    │
│  │ 🎮 Lobby: Catan Room (3/4 người)│    │
│  │                                   │    │
│  │ [QR Code] [Hủy đặt]             │    │
│  └─────────────────────────────────┘    │
│                                         │
│  ─── Đã hoàn thành ───                  │
│  ┌─────────────────────────────────┐    │
│  │ 🏠 BoardVerse Cafe              │    │
│  │ 📅 10/08/2026                   │    │
│  │ 🕐 AFTERNOON (14:00 - 18:00)    │    │
│  │ ✅ Đã chơi 4 tiếng (100%)      │    │
│  │ 💰 Deposit: Capture (doanh thu) │    │
│  └─────────────────────────────────┘    │
│                                         │
│  ⚠️ Karma Warning                       │
│  Bạn đã 3 lần đặt slot dài nhưng        │
│  chơi ít. Vui lòng đặt thời gian        │
│  phù hợp để tránh bị giới hạn.          │
└─────────────────────────────────────────┘
```

### 11.3 POS - Active Sessions

```
┌─────────────────────────────────────────┐
│  📋 Sessions đang hoạt động            │
│                                         │
│  ┌─────────────────────────────────┐    │
│  │ 🔵 Nhóm A (Res #res_789)        │    │
│  │    Catan Room                   │    │
│  │    ⏰ 13:00 ────●──── 18:00    │    │
│  │    👥 4/4 ghế | Còn: 2h 30m    │    │
│  │    🟡 Sắp hết giờ (30 phút)   │    │
│  │                                   │    │
│  │ [Extend] [End Session]          │    │
│  └─────────────────────────────────┘    │
│                                         │
│  ┌─────────────────────────────────┐    │
│  │ 🟢 Walk-in: Nguyễn Văn B       │    │
│  │    ⏰ 15:30 ────●──── 17:30    │    │
│  │    👥 3 ghế | Còn: 1h          │    │
│  │                                   │    │
│  │ [End Session]                    │    │
│  └─────────────────────────────────┘    │
│                                         │
│  ─── Sắp bắt đầu ───                    │
│  ┌─────────────────────────────────┐    │
│  │ ⏳ Nhóm B (Res #res_790)        │    │
│  │    ⏰ 18:00 - 22:00            │    │
│  │    👥 4 ghế                    │    │
│  │    Chưa check-in               │    │
│  └─────────────────────────────────┘    │
│                                         │
│  ─── Walk-in Windows ───                 │
│  ┌─────────────────────────────────┐    │
│  │ 🚶 15:00-18:00: 4 ghế trống    │    │
│  │    (Nhóm A về sớm)             │    │
│  │    [Add Walk-in]                │    │
│  └─────────────────────────────────┘    │
└─────────────────────────────────────────┘
```

### 11.4 POS - End Session Dialog

```
┌─────────────────────────────────────────┐
│  🔚 Kết thúc Session                    │
│                                         │
│  ─── Nhóm A ───                         │
│  Res #res_789 | Catan Room               │
│                                         │
│  ⏱ Thời gian:                          │
│  • Bắt đầu: 13:05                      │
│  • Kết thúc: 15:30                      │
│  • Đã chơi: 2h 25p                      │
│  • Slot đặt: 5 tiếng                   │
│  • Played ratio: 48%                   │
│                                         │
│  💰 Thanh toán:                         │
│  • Tiền giờ: 2h 25p × 10k = 24,500đ    │
│  • Đã đặt cọc: 15,000đ                 │
│                                         │
│  📊 Refund:                             │
│  ┌─────────────────────────────────┐    │
│  │ ⚠️ Đã chơi dưới 50%            │    │
│  │ Deposit không được refund       │    │
│  │ (Forfeit: 15,000đ)             │    │
│  └─────────────────────────────────┘    │
│                                         │
│  🚶 Walk-in Window:                    │
│  ┌─────────────────────────────────┐    │
│  │ Có thể tạo window:             │    │
│  │ 15:30 - 18:00 (2h 30p)         │    │
│  │ 4 ghế available                 │    │
│  │                                   │    │
│  │ [Tạo Walk-in Window]           │    │
│  └─────────────────────────────────┘    │
│                                         │
│  [ Hủy ]           [ Xác nhận ]        │
└─────────────────────────────────────────┘
```

### 11.5 POS - Add Walk-in

```
┌─────────────────────────────────────────┐
│  🚶 Thêm Walk-in                        │
│                                         │
│  Chọn Window:                           │
│  ┌─────────────────────────────────┐    │
│  │ ● 15:30 - 18:00 (2h 30p)       │    │
│  │   4 ghế trống                  │    │
│  │   (Nhóm A về sớm)              │    │
│  └─────────────────────────────────┘    │
│                                         │
│  👤 Tên khách: *                        │
│  ┌─────────────────────────────────┐    │
│  │ Nguyễn Văn B                    │    │
│  └─────────────────────────────────┘    │
│                                         │
│  📱 Số điện thoại:                      │
│  ┌─────────────────────────────────┐    │
│  │ 0901234567                      │    │
│  └─────────────────────────────────┘    │
│                                         │
│  👥 Số người: *                         │
│  ┌─────────────────────────────────┐    │
│  │ 3  người (còn 1 ghế)           │    │
│  └─────────────────────────────────┘    │
│                                         │
│  ⏰ Thời gian:                          │
│  • Bắt đầu: 15:30 (mặc định)          │
│  • Kết thúc: 17:30                     │
│                                         │
│  💰 Tạm tính:                          │
│  • 2 tiếng × 10k × 3 người = 60,000đ  │
│  (Thanh toán cuối session)              │
│                                         │
│  [ Hủy ]           [ Xác nhận ]        │
└─────────────────────────────────────────┘
```

### 11.6 Staff Notification - Near End

```
┌─────────────────────────────────────────┐
│  ⚠️ Nhắc nhở                            │
│                                         │
│  Nhóm A (Res #res_789) sắp hết giờ     │
│  Còn 30 phút nữa                        │
│                                         │
│  Lựa chọn:                              │
│  ┌─────────────────────────────────┐    │
│  │ [1] Gia hạn 30 phút             │    │
│  │     (+5,000đ)                   │    │
│  └─────────────────────────────────┘    │
│  ┌─────────────────────────────────┐    │
│  │ [2] Gia hạn 1 tiếng             │    │
│  │     (+10,000đ)                  │    │
│  │     ⚠️ Slot 18:00-22:00 đã có  │    │
│  └─────────────────────────────────┘    │
│  ┌─────────────────────────────────┐    │
│  │ [3] Kết thúc đúng giờ           │    │
│  └─────────────────────────────────┘    │
│                                         │
│  [Xem chi tiết]                         │
└─────────────────────────────────────────┘
```

---

## 12. Implementation Notes

### 12.1 Phased Rollout

```
Phase 1: Core Reservation (Week 1-2)
  - Reservation với playDate + timeSlot + auto-resolve endTime (BR-NEW-15a)
  - Check-in, check-out
  - Basic refund calculation (BR-REFUND-01..07)
  - No walk-in window, no karma tracking

Phase 2: Walk-in (Week 3-4)
  - Walk-in window creation (khi reservation early checkout)
  - Walk-in booking (POS only, BR-WALKIN-03)
  - POS walk-in flow
  - Auto-release after grace

Phase 3: Extension & Karma (Week 5-6)
  - Reservation extension logic (ReservationExtensionService — Phase 4 planning)
  - Karma system (BR-KARMA-01..05)
  - Override controls (POS staff)
  - Analytics

Phase 4: Optimization (Week 7-8)
  - Performance tuning
  - Edge case handling
  - UI/UX refinement
  - Migration từ Flow B (Booking legacy) sang Flow A (Reservation) hoàn tất
```

### 12.2 Testing Strategy

```
Unit Tests:
  - Refund calculation (BR-REFUND-01..07)
  - playedRatio calculation
  - Karma scoring (BR-KARMA-01..05)
  - Conflict detection (overlap seat inventory)
  - **Reservation atomic transaction** (BR-REQUIRED §17.4):
    - All steps succeed → reservation + lobby + ledger all created
    - Any step fail → full rollback, no partial state
    - Idempotency key trùng → trả cùng result (BR-REQUIRED §17.1)
  - **TimeSlot auto-resolve endTime** (BR-NEW-15a)
  - **BR-RES-07 validation**: quote không có endTime / null endTime → throw RESERVATION_REQUIRES_START_AND_END
  - **BR-RES-08 validation**: endTime.date != startTime.date → throw RESERVATION_END_TIME_DIFFERENT_DAY
  - **BR-RES-09 validation**: timeSlot không thuộc BR-NEW-15 enum → throw RESERVATION_INVALID_TIMESLOT
  - **edge case**: night slot 19:00-24:00 vẫn cùng ngày (endTime = 24:00 = ngày hôm sau 00:00?) — phải explicit ASSERT endTime.date == playDate
```

Integration Tests:
  - Reservation flow end-to-end (Flow A canonical)
  - Walk-in window creation
  - Extension flow
  - Cancel flow + BR-REFUND-02 timing

E2E Tests:
  - Happy path scenarios
  - All edge cases
  - Race conditions (2 cùng confirm atomic)
  - Error scenarios

Load Testing:
  - Concurrent reservation confirm attempts
  - Concurrent walk-in creation
  - POS stress test
```

### 12.3 Monitoring & Alerting

```
Key Metrics (Flow A — Reservation):
  - Reservation conversion rate (quote → confirm)
  - No-show rate
  - Early checkout rate
  - Walk-in fill rate
  - Extension success rate
  - Refund amount (BVC)
  - Karma violations
  - Atomic transaction failure rate
  - Average heldBalance per user

Alerts:
  - Oversell detected (CRITICAL)
  - High no-show rate
  - System errors
  - Staff override spike
  - Atomic transaction rollback spike (BR-REQUIRED §17.4)

Dashboards:
  - Real-time cafe status
  - Daily revenue (BVC)
  - Capacity utilization
  - Karma analytics
```

### 12.4 Rollback Plan

```
If issues detected:
  1. Disable new features (walk-in window, karma)
  2. Fallback to simple reservation (chỉ hold ghế, không tạo lobby recruitment)
  3. Keep data intact (reservation + lobby giữ nguyên, không rollback DB)
  4. Fix issues
  5. Re-enable features gradually

Database:
  - Full backup before migration
  - Point-in-time recovery available
  - Feature flags for toggling
  - Migration script từ `bookings` (legacy) → `reservations` reversible
```

---

## 13. Glossary

| Term | Definition |
|------|------------|
| **Time-slot** | Khoảng thời gian cố định mà player đặt để chơi |
| **playDate** | Ngày dự kiến chơi (DateOnly — BR-NEW-04) |
| **timeSlot** | Enum cố định 4 giá trị: morning (09-13), afternoon (13-18), evening (18-23), night (19-24) — BR-NEW-15 |
| **startTime** | Thời điểm bắt đầu = `playDate + timeSlot.startTime` (auto-resolve) — BR-RES-07 |
| **endTime** | Thời điểm kết thúc = `playDate + timeSlot.endTime` (auto-resolve, cùng ngày startTime) — BR-RES-07, BR-RES-08 |
| **preferredStartTime** | Optional, tham chiếu trong khoảng [timeSlot.startTime, timeSlot.endTime] — BR-NEW-15b |
| **CafeSchedule** | Anchor table chứa startTime/endTime chuẩn cho từng TimeSlot, dùng để resolve endTime |
| **Early Checkout** | Player về trước scheduled end time |
| **Played Ratio** | Tỷ lệ thời gian chơi thực tế / thời gian đã đặt |
| **Walk-in Window** | Khoảng thời gian trống có thể bán cho walk-in (cùng playDate với reservation early checkout) |
| **Karma System** | Hệ thống track và phạt player abuse slot dài |
| **Soft Release** | Release ghế khi player về sớm (so với hard release) |
| **Grace Period** | Thời gian buffer sau end time, không tính extra |
| **OCC** | Optimistic Concurrency Control - kỹ thuật tránh race condition |
| **Cross-day reservation** | Reservation có endTime khác playDate với startTime — **BỊ CẤM** (BR-RES-08) |
| **Open-ended reservation** | Reservation chỉ có startTime, không có endTime — **BỊ CẤM** (BR-RES-07) |

---

## 14. Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-08-11 | Design Team | Initial draft (booking legacy naming) |
| 2.0 | 2026-08-13 | Design Team | **Naming refactor**: đổi `Booking` → `Reservation` theo rule `booking-vs-reservation.mdc`. File này mô tả **Flow A canonical** (atomic đặt cọc tạo Lobby theo BR-REQUIRED §17.4). Flow B (Booking legacy SePay) đã được tách sang `docs/api/booking.md`. Thêm `TimeSlot` enum, `playDate` field, `idempotencyKey`, `riskMultiplier` theo rule `lobby-booking-deposit-bvc.mdc`. |
| 2.1 | 2026-08-13 | Design Team | **Thêm BR-RES-07, BR-RES-08, BR-RES-09**: ràng buộc reservation BẮT BUỘC có startTime + endTime, endTime cùng ngày startTime, endTime auto-resolve từ TimeSlot. Thêm `ReservationValidationError` enum với 3 codes. Update SQL schema: `start_time` + `end_time` NOT NULL + CHECK constraint. Thêm Glossary + ví dụ hợp lệ/không hợp lệ. |

---

## 15. Approval

| Role | Name | Date | Signature |
|------|------|------|-----------|
| Product Owner | | | |
| Tech Lead | | | |
| QA Lead | | | |
| Operations | | | |

---

*Document End*
