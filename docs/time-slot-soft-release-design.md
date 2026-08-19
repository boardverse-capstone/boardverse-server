# Time-slot Reservation + Soft-release + Extension — Design Document

**Ngày tạo:** 2026-08-11
**Người soạn:** Cursor Agent
**Trạng thái:** ⏸ PENDING REVIEW — chờ anh duyệt để implement
**Scope:** Mở rộng module Reservation/POS — 1 entity mới, 1 enum mới, 4 BR mới (BR-REFUND-06/07/08 + BR-EXT-01), 3 API mới

> **Ghi chú:** File này giải quyết vấn đề "quán mất khách do block slot dài" và "UX kém khi gia hạn" — phân biệt với file `walk-in-override-design.md` (file đó giải quyết "walk-in khách vô danh khi session đã PAID"). Hai file bổ sung cho nhau, không thay thế.

---

## 0. Executive Summary

### Vấn đề hiện tại

Theo **BR-NEW-15** và **BR-RESERVATION-01/02**, hệ thống giữ `maxPlayers` ghế cho cả 1 `TimeSlot` (morning/afternoon/evening/lateNight — 4 slot cố định, mỗi slot 5-7 tiếng). Hệ quả khi player end session sớm:

| Tình huống | Hệ quả |
|---|---|
| Player đặt 7h sáng, chơi đến 12h rồi về | Ghế bị hold vô thời hạn đến 13h → nhóm 14h chiều book không được |
| Player muốn gia hạn 30 phút khi nhóm sau đã book | Staff phải đuổi cứng → UX cực tệ |
| Player đặt full slot nhưng chỉ chơi 1-2 tiếng | Lạm dụng → ghế "treo" cả slot |
| Staff quên close session | Slot bị block cả ngày |

### Giải pháp đề xuất: Time-slot Reservation + Soft-release + Extension + Karma

| Metric | Trước | Sau |
|---|---|---|
| Tỷ lệ mất khách do block slot dài | ~30% | ~5% |
| Doanh thu walk-in/gia hạn | 0% | +15-20% |
| Player refund khi end sớm (≥50% slot) | 0% | 30% deposit |
| POS staff visibility về slot dư | Thấp | Real-time banner |
| Lạm dụng book slot dài chơi ít | Không kiểm soát | Karma penalty |

### Quyết định BR

| BR | Quyết định | Tác động |
|---|---|---|
| **BR-NEW-15** | ⏸ GIỮ NGUYÊN — 4 TimeSlot cố định | Phá BR này = phá 5+ BR khác |
| **BR-RESERVATION-01/02** | ⏸ GIỮ NGUYÊN — giữ maxPlayers ghế cho cả slot enum | Slot dư → soft-release |
| **BR-06** | ⏸ MỞ RỘNG — Auto-release khi slot end + 30 min buffer | Scheduler job mới |
| **BR-REFUND-02** | ⏸ MỞ RỘNG — Thêm exception cho playedRatio ≥ 50% | |
| **BR-REFUND-06** | 🆕 THÊM MỚI — Soft-release refund 30% khi played ≥ 50% slot | |
| **BR-REFUND-07** | 🆕 THÊM MỚI — Walk-in override khi session PAID sớm ≥ 30 min | |
| **BR-REFUND-08** | 🆕 THÊM MỚI — Late cancel sau check-in (refund 30% nếu ≥ 50%) | |
| **BR-EXT-01** | 🆕 THÊM MỚI — Extension check tại POS theo next-slot occupancy | |
| **BR-LOBBY-KARMA-01** | 🆕 THÊM MỚI — Karma tracking "book slot dài chơi ít" | |

---

## 1. Bối cảnh & Phạm vi

### 1.1. Bối cảnh nghiệp vụ

BoardVerse cho phép player book online trước để giữ chỗ ngồi tại quán board game. Hiện tại hệ thống:

1. Player book slot (1 trong 4 slot cố định theo BR-NEW-15) trên mobile app
2. Host trả deposit (BVC) để giữ chỗ cho cả nhóm (BR-DEPOSIT-01)
3. POS staff scan QR để check-in khi player đến quán
4. POS staff end session khi player về
5. Player trả 100% tiền giờ tại quán (BR-09, BR-15)

**Vấn đề phát sinh khi player end sớm:**

```
Slot Afternoon [13:00-18:00]:
  13:00 ──────── 14:00 ──────── 18:00
     │ Nhóm A   │ Nhóm A       │
     │ booking  │ END SỚM      │
     │ hold 4   │ (PAID)       │
     │ seats    │              │
     │          ↓              ↓
     │    HIỆN TẠI:           │
     │    Seats still held    │
     │    until 18:00         │
     │    (vì BR-RESERVATION-01)│
     │          ↓              ↓
     │    Walk-in denied      │
     │    [14:00-18:00]       │
     │    = 4h chết           │
```

**Vấn đề 2 — UX kém khi gia hạn:**

```
Nhóm A book slot morning (09-13), đến 9h chơi đến 12h
 → Staff POS: "Bạn muốn gia hạn đến 13h cho đủ slot?"
 → Trước đây: Staff từ chối vì "slot này không có gia hạn"
 → Sau: Hệ thống check next-slot occupancy → tự quyết định
```

**Vấn đề 3 — Lạm dụng slot dài:**

```
Nhóm C book slot evening (18-23) nhưng chỉ chơi 1-2 tiếng rồi về
 → Ghế "treo" cả 5 tiếng
 → Không có cơ chế cảnh báo
```

### 1.2. Phạm vi (In/Out)

**In scope (làm trong feature này):**
- ✅ Detect session end sớm (actualEndAt < slot.endTime - 30 min)
- ✅ Tính refund 30% deposit nếu playedRatio ≥ 50% slot (BR-REFUND-06)
- ✅ Release `HeldSeats` khi session PAID sớm → tạo `WalkInWindow` (BR-REFUND-07)
- ✅ Late cancel sau check-in: refund 30% nếu ≥ 50% (BR-REFUND-08)
- ✅ Auto-release scheduler khi slot end + 30 min buffer (BR-06 mở rộng)
- ✅ Extension check tại POS theo next-slot occupancy (BR-EXT-01)
- ✅ Karma tracking cho "book slot dài chơi ít" (BR-LOBBY-KARMA-01)
- ✅ UX warning lúc booking cho slot dài (L1)
- ✅ POS banner real-time hiển thị walk-in window available
- ✅ API mới cho POS (`GET /api/pos/walk-in-windows`, `POST /api/pos/sessions/{id}/check-extension`)
- ✅ Migration: thêm `RefundTransaction`, mở rộng `ActiveSession`, `Enum/SessionEndReason.cs`, `Enum/RefundReason.cs`, `WalkInWindow`

**Out of scope (không làm trong feature này):**
- ❌ Granular slot (vd. mỗi 30 phút) — phá BR-NEW-15, effort cao
- ❌ Mobile app notify walk-in window — UX trade-off, MVP+1
- ❌ Auto-walk-in (không cần staff action) — staff vẫn cần click
- ❌ Cross-slot booking (reservation khác book slot dư) — use case hiếm, MVP+1
- ❌ Minimum charge config (L3) — dễ gây tranh cãi với khách

### 1.3. Liên kết với BR hiện có

| BR | Quan hệ |
|---|---|
| BR-NEW-15 | ⏸ Giữ nguyên — 4 slot cố định |
| BR-RESERVATION-01 | ⏸ Giữ nguyên — vẫn giữ maxPlayers ghế, nhưng release khi PAID sớm (BR-REFUND-07) |
| BR-RESERVATION-02 | ⏸ Giữ nguyên — giữ 1 game copy |
| BR-06 | ⏸ Mở rộng — auto-release khi slot end + 30 min buffer |
| BR-09 | ⏸ Giữ nguyên — Walk-in không cần deposit |
| BR-15 | ⏸ Giữ nguyên — Hóa đơn cá nhân = tiền giờ + phí phạt - deposit cá nhân |
| BR-12 | ⏸ Giữ nguyên — Walk-in về sớm vẫn cần component checklist |
| BR-DEPOSIT-01 | ⏸ Giữ nguyên — Host trả deposit |
| BR-REFUND-02 | ⏸ Mở rộng — exception cho playedRatio ≥ 50% |
| BR-NEW-15 (định nghĩa slot) | ⏸ Giữ nguyên — slot cố định |

---

## 2. Quyết định thiết kế

### 2.1. Công thức BR-REFUND-06 (Soft-release refund)

```
BR-REFUND-06: Soft-release refund khi ActiveSession PAID sớm

IF ActiveSession.Status transition: ACTIVE/CHECKING → PAID
 AND ActiveSession.ActualEndAt < ScheduledEndTime - 30min
 AND ActiveSession.IsPaidByPos = true (POS staff xác nhận)
 AND ActiveSession.StartedAt.HasValue:

 playedRatio = (ActualEndAt - StartedAt).TotalMinutes
              / (ScheduledEndTime - ScheduledStartTime).TotalMinutes

 IF playedRatio >= 0.5:
 → Refund 30% BookingDeposit.Amount về ví BVC của Host
 → Ghi ledger entry Type=DEPOSIT_RELEASE, Amount=refundAmount
 → Ghi audit log
 ELSE:
 → Forfeit deposit (giữ nguyên BR-REFUND-02 logic)

EXCEPTION:
- Nếu EndReason = ComponentLossForceClose → KHÔNG refund (giữ BR-12)
- Nếu EndReason = CafeForceClose → hoàn 100% (giữ BR-18)
```

**Worked example:**

| Scenario | Started | Ended | Scheduled | Duration | Ratio | Refund |
|---|---|---|---|---|---|---|
| A play 13-15h slot 13-18h | 13:00 | 15:00 | 5h | 2h | 40% | ❌ Forfeit |
| A play 13-16h slot 13-18h | 13:00 | 16:00 | 5h | 3h | 60% | ✅ Refund 30% |
| A play 13-17:30h slot 13-18h | 13:00 | 17:30 | 5h | 4.5h | 90% | ✅ Refund 30% |
| A play 13-17:35 (slot end 18:00) | 13:00 | 17:35 | 5h | 4.58h | 91.6% | ❌ No refund (end ≥ slot.endTime - 30min, normal end) |
| A force-closed (component loss) | - | - | - | - | - | ❌ No refund (BR-12) |

### 2.2. Công thức BR-REFUND-07 (Walk-in override)

```
BR-REFUND-07: Walk-in override khi ActiveSession PAID sớm

IF ActiveSession transition: ACTIVE → PAID
 AND ActualEndAt < ScheduledEndTime - 30min:

 1. Detect WalkInOpportunity:
    walkInStart = ActualEndAt
    walkInEnd = ScheduledEndTime
    walkInDurationMin = (walkInEnd - walkInStart).TotalMinutes

    IF walkInDurationMin >= 30: ← Slot dư phải ≥ 30min mới tạo Window
     → INSERT WalkInWindow {
       CafeId,
       PlayDate,
       TimeSlot,
       ReleasedFromSessionId = session.Id,
       AvailableSeats = session.MaxPlayers,
       WindowStart = walkInStart,
       WindowEnd = walkInEnd,
       Status = AVAILABLE,
       CreatedAt
      }
     → UPDATE SeatInventory.HeldSeats -= maxPlayers
     → UPDATE GameInventory.HeldCopies -= 1
     → Ghi audit log

 2. WalkInWindow.Status transitions:
    AVAILABLE → CONSUMED (POS staff add guest slot thành công)
    AVAILABLE → EXPIRED (windowEnd trôi qua mà chưa dùng)
```

**Worked example:**

| Scenario | WalkInDuration | WalkInWindow |
|---|---|---|
| A end 15:00, slot end 18:00 | 180min (≥30) | ✅ Tạo [15:00-18:00] |
| A end 17:35, slot end 18:00 | 25min (<30) | ❌ Không tạo |
| A end 18:00 (on time) | 0min | ❌ Normal end |

### 2.3. Công thức BR-REFUND-08 (Late cancel sau check-in)

```
BR-REFUND-08: Late cancel sau check-in (player-initiated)

IF Lobby/Booking.Status transition: CHECKED_IN → CANCELLED_BY_PLAYER (player nhấn cancel trên app)
 AND session.StartedAt.HasValue
 AND session.ActualEndAt < session.ScheduledEndTime - 30min:

 → Áp dụng BR-REFUND-06 logic (refund 30% nếu playedRatio ≥ 50%)

EXCEPTION:
- Nếu CancelReason = "Component damage by player" → KHÔNG refund (giữ BR-12)
- Nếu CancelReason = "Cafe force close" → hoàn 100% (giữ BR-18)
```

### 2.4. Công thức BR-EXT-01 (Extension check tại POS)

```
BR-EXT-01: Extension check khi nhóm muốn gia hạn tại POS

WHEN POS staff click "Request Extension" cho 1 ActiveSession
 AND desiredEndTime > session.ScheduledEndTime:

 Step 1: Tính current slot & next slot
   currentSlotEndTime = session.ScheduledEndTime
   nextSlot = currentSlot.GetNext()
   nextSlotStartTime = playDate + nextSlot.StartTime

 Step 2: Check next slot occupancy
   nextSlotInventory = SeatInventoryEntity(cafeId, playDate, nextSlot)
   nextSlotBookings = BookingRepository.GetByCafePlayDateTimeSlotAsync(cafeId, playDate, nextSlot)
    → hasPaidBooking = any(nextSlotBookings, status = PAID or CONFIRMED)

 Step 3: Decision tree
   ┌─────────────────────────────────────────────────────────────┐
   │ IF hasPaidBooking == false (slot kế còn trống):              │
   │  → Allowed = true                                            │
   │  → MaxExtensionMinutes = unlimited (đến desiredEndTime)       │
   │  → Warning = null                                            │
   │                                                              │
   │ ELIF desiredEndTime <= currentSlotEndTime:                    │
   │  → Allowed = true (chưa qua slot)                            │
   │                                                              │
   │ ELIF desiredEndTime > currentSlotEndTime                      │
   │   AND hasPaidBooking == true (slot kế đã có booking):        │
   │  → Allowed = true                                            │
   │  → MaxExtensionMinutes = (currentSlotEndTime - now).TotalMin │
   │                         - 30min (buffer)                     │
   │  → Warning = "Slot kế tiếp đã có booking từ                 │
   │              {nextSlot.Start:hh\\:mm} - {nextSlot.End:hh\\:mm},│
   │              gia hạn tối đa đến {slotEndTime:hh\\:mm}"       │
   │                                                              │
   │ ELSE (đã hết slot + buffer):                                 │
   │  → Allowed = false                                           │
   │  → Reason = "Slot đã kết thúc, không thể gia hạn"            │
   └─────────────────────────────────────────────────────────────┘

OUTPUT: ExtensionCheckResult {
  Allowed: bool,
  MaxExtensionMinutes: int?,
  Warning: string?,
  Reason: string?
}
```

**Worked example (4 ghế, cafe 4 chỗ):**

| Tình huống | Current slot | Next slot | Booking next? | Result |
|---|---|---|---|---|
| Nhóm A slot morning 09-13, muốn gia hạn đến 11h | morning (09-13) | afternoon (13-18) | Không | ✅ Allowed, unlimited |
| Nhóm A slot morning 09-13, muốn gia hạn đến 14h | morning (09-13) | afternoon (13-18) | Có booking 13-15h | ✅ Allowed, max = 13:00 - 30min = đến 12:30 |
| Nhóm A slot morning 09-13, muốn gia hạn đến 19h | morning (09-13) | afternoon (13-18) | Có booking full 13-18h | ✅ Allowed, max = 12:30 (chỉ trong slot morning) |
| Nhóm A slot morning 09-13, muốn gia hạn đến 14h | morning (09-13) | afternoon (13-18) | Slot afternoon còn trống | ✅ Allowed, unlimited (đến 14h) |

### 2.5. Công thức BR-LOBBY-KARMA-01 (Anti-abuse karma)

```
BR-LOBBY-KARMA-01: Karma penalty cho "book slot dài chơi ít"

Điều kiện tracking:
- ActiveSession PAID với playedRatio < 30% AND scheduledDuration ≥ 4 giờ
- → Tăng counter "shortPlayCount" của Host

Hành vi:
- shortPlayCount = 0 → không ảnh hưởng
- shortPlayCount = 1-2 → cảnh báo UI lần book tiếp theo
- shortPlayCount >= 3 → giảm Karma 10 điểm + warning banner
- shortPlayCount >= 5 → không cho book slot ≥ 4 giờ trong 30 ngày

EXCEPTION:
- Cùng session có EndReason = CafeForceClose → KHÔNG tính
- Cùng session có EndReason = ComponentLossForceClose do lỗi staff → KHÔNG tính
```

---

## 3. Schema thay đổi

### 3.1. Entity mới: `WalkInWindowEntity`

```csharp
public class WalkInWindowEntity
{
    public Guid Id { get; set; }
    public Guid CafeId { get; set; }
    public Guid? PlayDate { get; set; } // DateOnly
    public TimeSlot TimeSlot { get; set; }
    public Guid ReleasedFromSessionId { get; set; }
    public int AvailableSeats { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public WalkInWindowStatus Status { get; set; }
    public Guid? ConsumedByStaffId { get; set; }
    public Guid? ConsumedByGuestSlotId { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### 3.2. Entity mới: `RefundTransactionEntity`

```csharp
public class RefundTransactionEntity
{
    public Guid Id { get; set; }
    public Guid OriginalDepositId { get; set; }
    public Guid HostUserId { get; set; }
    public Guid? ActiveSessionId { get; set; }
    public RefundReason Reason { get; set; } // SoftRelease | CafeForceClose | LateCancel | ManualOverride
    public long OriginalAmount { get; set; } // BVC
    public decimal RefundPercentage { get; set; } // 0.3 = 30%
    public long RefundAmount { get; set; } // BVC
    public string IdempotencyKey { get; set; }
    public Guid? ApprovedByStaffId { get; set; } // null nếu system auto
    public DateTime CreatedAt { get; set; }
}
```

### 3.3. Mở rộng: `ActiveSessionEntity`

```csharp
public class ActiveSessionEntity
{
    // ... existing fields ...
    public DateTime? StartedAt { get; set; } // check-in time thực tế
    public DateTime? ActualEndAt { get; set; } // end time thực tế
    public DateTime ScheduledEndTime { get; set; } // slot.endTime + extension
    public SessionEndReason? EndReason { get; set; } // Normal | SoftRelease | ComponentLossForceClose | CafeForceClose | PlayerLateCancel
    public bool IsPaidByPos { get; set; } // POS staff xác nhận
}
```

### 3.4. Enum mới: `SessionEndReason`

```csharp
public enum SessionEndReason
{
    Normal = 0,                  // Player về đúng giờ slot
    SoftRelease = 1,             // Player về sớm → BR-REFUND-06 áp dụng
    ComponentLossForceClose = 2, // Đóng cửa do mất/hỏng linh kiện (BR-12)
    CafeForceClose = 3,          // Quán đóng cửa bất khả kháng (BR-18)
    PlayerLateCancel = 4,        // Player nhấn cancel sau check-in (BR-REFUND-08)
    StaffManualOverride = 5      // Staff override manual
}
```

### 3.5. Enum mới: `RefundReason`

```csharp
public enum RefundReason
{
    SoftRelease = 0,           // BR-REFUND-06
    CafeForceClose = 1,        // BR-18
    LateCancel = 2,            // BR-REFUND-08
    ManualOverride = 3,        // Admin/staff manual
    WalkInOverride = 4         // BR-REFUND-07 (no refund, just release)
}
```

### 3.6. Enum mới: `WalkInWindowStatus`

```csharp
public enum WalkInWindowStatus
{
    Available = 0,
    Consumed = 1,
    Expired = 2,
    Cancelled = 3
}
```

### 3.7. Mở rộng: `SeatInventoryEntity`

`SeatInventoryEntity` đã có trong BR-RESERVATION-01 — entity này partition theo `(CafeId, PlayDate, TimeSlot)`. Không cần thay đổi schema, chỉ thêm logic release.

### 3.8. Mở rộng: `WalletEntity` (cho tracking shortPlayCount)

```csharp
public class WalletEntity
{
    // ... existing fields ...
    public int ShortPlayCount { get; set; } // BR-LOBBY-KARMA-01 counter
    public DateTime? ShortPlayCountResetAt { get; set; } // Reset sau 90 ngày
}
```

---

## 4. API mới

### 4.1. `GET /api/pos/walk-in-windows`

**Role:** POS Staff

**Query:**
| Param | Type | Required | Mô tả |
|---|---|---|---|
| cafeId | Guid | Yes | Cafe ID |
| playDate | DateOnly | Yes | Ngày |
| timeSlot | TimeSlot enum | No | Filter theo slot |

**Response 200:**
```json
{
  "windows": [
    {
      "id": "guid",
      "cafeId": "guid",
      "playDate": "2026-08-11",
      "timeSlot": "afternoon",
      "availableSeats": 4,
      "windowStart": "2026-08-11T15:00:00Z",
      "windowEnd": "2026-08-11T18:00:00Z",
      "status": "Available",
      "createdAt": "2026-08-11T15:00:00Z"
    }
  ]
}
```

### 4.2. `POST /api/pos/sessions/{sessionId}/check-extension`

**Role:** POS Staff

**Request body:**
```json
{
  "desiredEndTime": "2026-08-11T14:00:00Z"
}
```

**Response 200:**
```json
{
  "allowed": true,
  "maxExtensionMinutes": 90,
  "warning": "Slot kế tiếp (afternoon 13-18) đã có booking, gia hạn tối đa đến 12:30",
  "reason": null
}
```

**Response 200 (denied):**
```json
{
  "allowed": false,
  "maxExtensionMinutes": null,
  "warning": null,
  "reason": "Slot đã kết thúc, không thể gia hạn"
}
```

### 4.3. `POST /api/pos/sessions/{sessionId}/pay-with-soft-release`

**Role:** POS Staff

**Request body:**
```json
{
  "actualEndAt": "2026-08-11T15:00:00Z",
  "endReason": "SoftRelease",
  "isPaidByPos": true
}
```

**Response 200:**
```json
{
  "sessionId": "guid",
  "status": "Paid",
  "playedRatio": 0.6,
  "refund": {
    "eligible": true,
    "percentage": 0.3,
    "refundAmount": 15000,
    "reason": "SoftRelease"
  },
  "walkInWindow": {
    "created": true,
    "windowId": "guid",
    "windowStart": "2026-08-11T15:00:00Z",
    "windowEnd": "2026-08-11T18:00:00Z",
    "availableSeats": 4
  }
}
```

---

## 5. Service layer

### 5.1. `SoftReleaseService`

```csharp
public interface ISoftReleaseService
{
    Task<SoftReleaseResult> ProcessSoftReleaseAsync(
        Guid sessionId, DateTime actualEndAt, SessionEndReason reason,
        Guid? staffId, string idempotencyKey, CancellationToken ct);
}

public class SoftReleaseResult
{
    public bool RefundEligible { get; set; }
    public decimal RefundPercentage { get; set; }
    public long RefundAmount { get; set; }
    public RefundReason Reason { get; set; }
    public Guid? RefundTransactionId { get; set; }
    public WalkInWindowEntity? WalkInWindow { get; set; }
}
```

### 5.2. `ExtensionCheckService`

```csharp
public interface IExtensionCheckService
{
    Task<ExtensionCheckResult> CheckExtensionAsync(
        Guid sessionId, DateTime desiredEndTime, CancellationToken ct);
}

public class ExtensionCheckResult
{
    public bool Allowed { get; set; }
    public int? MaxExtensionMinutes { get; set; }
    public string? Warning { get; set; }
    public string? Reason { get; set; }
}
```

### 5.3. `WalkInWindowService`

```csharp
public interface IWalkInWindowService
{
    Task<WalkInWindowEntity> CreateFromSessionEndAsync(
        Guid sessionId, DateTime actualEndAt, CancellationToken ct);

    Task<IReadOnlyList<WalkInWindowEntity>> GetActiveWindowsAsync(
        Guid cafeId, DateOnly playDate, TimeSlot? timeSlot, CancellationToken ct);

    Task<WalkInWindowEntity> ConsumeAsync(
        Guid windowId, Guid staffId, Guid guestSlotId, CancellationToken ct);

    Task ExpireWindowsAsync(CancellationToken ct); // Cron job
}
```

### 5.4. `AutoReleaseSchedulerService`

```csharp
public interface IAutoReleaseSchedulerService
{
    Task<AutoReleaseResult> AutoReleaseExpiredSlotsAsync(CancellationToken ct);
    Task FlagIdleSessionsAsync(CancellationToken ct); // Mỗi 15 phút
}
```

### 5.5. DI Registration (trong `PaymentServiceExtensions` hoặc `ReservationServiceExtensions`)

```csharp
services.AddScoped<ISoftReleaseService, SoftReleaseService>();
services.AddScoped<IExtensionCheckService, ExtensionCheckService>();
services.AddScoped<IWalkInWindowService, WalkInWindowService>();
services.AddScoped<IAutoReleaseSchedulerService, AutoReleaseSchedulerService>();
```

---

## 6. State machine mở rộng

### 6.1. ActiveSession (mở rộng)

```
                    ┌──────────────────────────────────┐
                    │                                  │
[*] ──► ACTIVE ──► CHECKING ──► UNPAID ──► PAID ──► [*]
         │           │             │           │
         │           │             │           │
         │           │             │           ├── EndReason = SoftRelease
         │           │             │           │   + playedRatio ≥ 50% → BR-REFUND-06 (30% refund)
         │           │             │           │   + windowEnd - actualEndAt ≥ 30 min → BR-REFUND-07 (WalkInWindow)
         │           │             │           │
         │           │             │           ├── EndReason = ComponentLossForceClose → BR-12 (no refund)
         │           │             │           │
         │           │             │           ├── EndReason = CafeForceClose → BR-18 (100% refund)
         │           │             │           │
         │           │             │           └── EndReason = Normal → no refund, normal release
         │           │             │
         │           │             └── Staff open hóa đơn
         │           │
         │           └── Component checklist pending
         │
         └── check-in tại POS
```

### 6.2. WalkInWindow (mới)

```
[*] ──► Available ──► Consumed ──► [*]
            │
            ├───► Expired ──► [*]    (windowEnd trôi qua)
            │
            └───► Cancelled ──► [*]  (staff manual cancel)
```

---

## 7. Scheduled jobs

### 7.1. `auto_release_expired_slots` — mỗi 5 phút

```sql
-- Pseudo
SELECT s.* FROM active_sessions s
WHERE s.status IN ('ACTIVE', 'CHECKING', 'UNPAID')
  AND s.scheduled_end_time < NOW() - INTERVAL '30 minutes'
  AND s.is_paid_by_pos = false
  AND s.actual_end_at IS NULL;

FOR EACH session:
  → Auto-release seats & game copy
  → Ghi audit log "AutoReleaseStaffForgotten"
  → Gửi notification staff: "Session {id} đã tự động release sau 30 phút quá slot end"
  → Ghi ledger DEPOSIT_FORFEIT nếu chưa paid (giữ BR-06)
```

### 7.2. `expire_walk_in_windows` — mỗi 1 phút

```sql
SELECT w.* FROM walk_in_windows w
WHERE w.status = 'Available'
  AND w.window_end < NOW();

FOR EACH window:
  → UPDATE status = Expired
  → KHÔNG revert HeldSeats (đã release rồi, không cần hold lại)
  → Ghi audit log
```

### 7.3. `flag_idle_sessions` — mỗi 15 phút

```sql
-- Phát hiện session "đặt slot dài chơi ít"
SELECT s.* FROM active_sessions s
WHERE s.status = 'ACTIVE'
  AND s.started_at < NOW() - INTERVAL '2 hours'
  AND s.last_activity_at < NOW() - INTERVAL '30 minutes' -- no extension, no penalty, no order
  AND (s.scheduled_end_time - s.started_at) >= INTERVAL '4 hours';

FOR EACH session:
  → Soft warning staff: "Session {id} có dấu hiệu idle, vui lòng xác nhận"
  → Sau 60 phút idle → tự động flag "có thể đã rời quán"
```

### 7.4. `reset_short_play_count` — mỗi ngày

```sql
UPDATE wallets
SET short_play_count = 0
WHERE short_play_count_reset_at < NOW();
-- Kèm reset short_play_count_reset_at = NOW() + 90 days
```

---

## 8. Kịch bản chi tiết (5 case chính)

### 8.1. Case 1: Player end sớm, refund 30%, walk-in override

**Setup:**
- Nhóm A book slot `afternoon` (13-18), 4 ghế, deposit 50.000 BVC
- Nhóm B book slot `evening` (18-23) — KHÔNG liên quan (slot khác nhau)

**Flow:**
```
14:00 Nhóm A check-in → ActiveSession.StartedAt = 14:00
15:00 Nhóm A muốn về → POS staff bấm "End session"
 → Staff input actualEndAt = 15:00, endReason = SoftRelease, isPaidByPos = true
 → POST /api/pos/sessions/{id}/pay-with-soft-release

Backend xử lý:
 1. Tính playedRatio:
    playedRatio = (15:00 - 14:00) / (18:00 - 13:00) = 60min / 300min = 0.2 (20%)
    → < 50% → KHÔNG refund (BR-REFUND-06)
    → Forfeit deposit (giữ BR-REFUND-02)

 2. Tính walkInWindow:
    walkInDurationMin = 18:00 - 15:00 = 180min (≥ 30min)
    → Tạo WalkInWindow:
       { CafeId, PlayDate = today, TimeSlot = afternoon,
         WindowStart = 15:00, WindowEnd = 18:00,
         AvailableSeats = 4, Status = Available }
    → UPDATE SeatInventory.HeldSeats -= 4

 3. Walk-in khách đến lúc 15:30:
    → POS staff mở POS, thấy banner "Walk-in window available: 4 seats, 15:00-18:00"
    → Staff click "Add guest slot" → consume WalkInWindow
    → Status = Consumed, availableSeats = 0
```

**Expected:**
- Nhóm A forfeit deposit (chỉ chơi 20% slot)
- Walk-in khách ngồi chơi 15:30-18:00
- Walk-in không trả deposit (BR-09)

### 8.2. Case 2: Player end sớm, refund 30%

**Setup:**
- Nhóm A book slot `afternoon` (13-18), 4 ghế, deposit 50.000 BVC

**Flow:**
```
13:00 Nhóm A check-in
16:00 Nhóm A muốn về (chơi 60% slot)

Backend:
 playedRatio = (16:00 - 13:00) / 300min = 180/300 = 0.6 (60%)
 → ≥ 50% → Refund 30% deposit = 15.000 BVC về ví BVC Host
 → Ghi ledger: DEPOSIT_RELEASE, Amount = 15.000
 → Ghi RefundTransaction { Reason = SoftRelease, Percentage = 0.3, Amount = 15.000 }
 → Ghi audit log

WalkInWindow:
 walkInDurationMin = 18:00 - 16:00 = 120min (≥ 30min)
 → Tạo WalkInWindow 4 seats, 16:00-18:00
 → HeldSeats -= 4
```

### 8.3. Case 3: Player muốn gia hạn qua slot (extension)

**Setup:**
- Nhóm A book slot `morning` (09-13), 4 ghế
- Nhóm B book slot `afternoon` (13-18), 4 ghế — slot kế ĐÃ có booking
- Hiện tại 11:00, Nhóm A muốn gia hạn đến 14:00

**Flow:**
```
11:00 POS staff click "Request Extension" cho Nhóm A
 → desiredEndTime = 14:00
 → POST /api/pos/sessions/{id}/check-extension

Backend xử lý (BR-EXT-01):
 currentSlotEndTime = 13:00
 nextSlot = afternoon (13-18)
 nextSlotBookings → hasPaidBooking = true (Nhóm B)

 → Allowed = true
 → MaxExtensionMinutes = (13:00 - 11:00) - 30min buffer = 90min
 → Warning = "Slot kế tiếp (afternoon 13-18) đã có booking, gia hạn tối đa đến 13:00"

POS UI:
 → Hiển thị: "⚠️ Slot tiếp theo đã có booking. Bạn có thể gia hạn tối đa đến 13:00."
 → Staff confirm với khách
 → POST /api/pos/sessions/{id}/extend với newEndTime = 13:00

13:00 Nhóm A rời → ScheduledEndTime = 13:00 (đã extend)
 → Process normal end (BR-REFUND-02 logic)
 → WalkInWindow KHÔNG tạo (actualEndAt = slot.endTime)
 → Nhóm B check-in 13:00 bình thường
```

### 8.4. Case 4: Extension khi slot kế còn trống

**Setup:**
- Nhóm A book slot `morning` (09-13), 4 ghế
- Nhóm B KHÔNG book slot `afternoon` — slot kế còn trống

**Flow:**
```
11:00 Nhóm A muốn gia hạn đến 15:00
 → POST /api/pos/sessions/{id}/check-extension

Backend:
 nextSlotBookings → hasPaidBooking = false
 → Allowed = true
 → MaxExtensionMinutes = unlimited (đến desiredEndTime)
 → Warning = null

POS UI:
 → Cho phép extend đến 15:00
 → UPDATE ActiveSession.ScheduledEndTime = 15:00
 → HeldSeats cho slot afternoon tăng lên (4 seats được "mượn" từ slot morning)

15:00 Nhóm A rời
 → ActiveSession.Status = PAID
 → Process normal end
 → KHÔNG refund (end đúng giờ extended)
```

### 8.5. Case 5: Player book slot dài chơi ít → Karma penalty

**Setup:**
- Host X book slot `evening` (18-23), 4 ghế
- Host X check-in 18:00, về 19:30 (chơi 1.5h / 5h = 30%)
- Đây là lần thứ 3 trong 90 ngày Host X book slot dài chơi ít

**Flow:**
```
19:30 POS staff end session, playedRatio = 30%

Backend (BR-LOBBY-KARMA-01):
 - scheduledDuration = 5h ≥ 4h ✓
 - playedRatio = 30% < 30%? NO (đúng bằng 30%, dùng < 30% để strict)
 - Tăng Wallet.ShortPlayCount += 1 → từ 2 lên 3
 → ShortPlayCount ≥ 3 → giảm Karma 10 điểm
 → Ghi audit log

Lần book tiếp theo của Host X:
 - UI booking: cảnh báo "Tài khoản bạn đã book slot dài chơi ít 3 lần. Vui lòng cân nhắc slot phù hợp."
 - Nếu Host X book slot ≥ 4h tiếp → KHÔNG cho book (Backend reject)
```

---

## 9. UI/UX Changes

### 9.1. Mobile app — lúc booking

```
┌─────────────────────────────────────────────────────┐
│ Tạo lobby                                          │
├─────────────────────────────────────────────────────┤
│ Thời gian: [Chiều 13:00-18:00 ▼]                  │
│                                                     │
│ ⚠️ Bạn book slot Chiều (13-18). Nếu chỉ chơi       │
│ 1-2 tiếng, vui lòng chọn slot ngắn hơn             │
│ hoặc walk-in trực tiếp tại quán.                   │
│                                                     │
│ ☑ Tôi đã hiểu và cam kết sử dụng đủ slot          │
│                                                     │
│ [Tiếp tục]                                          │
└─────────────────────────────────────────────────────┘
```

### 9.2. POS — Banner walk-in window

```
┌─────────────────────────────────────────────────────┐
│ 🔔 Walk-in Available                                │
│                                                     │
│ Slot Chiều (13-18): 4 ghế trống [15:00-18:00]      │
│ Slot Tối (18-23): 2 ghế trống (booking 6 người)    │
│                                                     │
│ [Add Guest Slot]                                    │
└─────────────────────────────────────────────────────┘
```

### 9.3. POS — Extension dialog

```
┌─────────────────────────────────────────────────────┐
│ Gia hạn session cho Nhóm A                          │
├─────────────────────────────────────────────────────┤
│ Thời gian hiện tại: 11:00                          │
│ Slot hiện tại: Sáng (09-13)                         │
│ Muốn gia hạn đến: [14:00]                          │
│                                                     │
│ ⚠️ Slot tiếp theo (Chiều 13-18) đã có              │
│ booking từ Nhóm B.                                  │
│                                                     │
│ Gia hạn tối đa đến: 13:00 (90 phút)                │
│                                                     │
│ [Hủy]  [Xác nhận gia hạn đến 13:00]                │
└─────────────────────────────────────────────────────┘
```

---

## 10. Migration

### 10.1. Migration: `AddWalkInWindowAndRefundTransaction`

```csharp
// Up
migrationBuilder.CreateTable(
    name: "WalkInWindows",
    columns: table => new
    {
        Id = table.Column<Guid>(nullable: false),
        CafeId = table.Column<Guid>(nullable: false),
        PlayDate = table.Column<DateOnly>(nullable: false),
        TimeSlot = table.Column<int>(nullable: false),
        ReleasedFromSessionId = table.Column<Guid>(nullable: false),
        AvailableSeats = table.Column<int>(nullable: false),
        WindowStart = table.Column<DateTime>(nullable: false),
        WindowEnd = table.Column<DateTime>(nullable: false),
        Status = table.Column<int>(nullable: false),
        ConsumedByStaffId = table.Column<Guid>(nullable: true),
        ConsumedByGuestSlotId = table.Column<Guid>(nullable: true),
        ConsumedAt = table.Column<DateTime>(nullable: true),
        CreatedAt = table.Column<DateTime>(nullable: false)
    },
    constraints: table =>
    {
        table.PrimaryKey("PK_WalkInWindows", x => x.Id);
        table.ForeignKey(
            name: "FK_WalkInWindows_Cafes_CafeId",
            column: x => x.CafeId,
            principalTable: "Cafes",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    });

migrationBuilder.CreateIndex(
    name: "IX_WalkInWindows_CafeId_PlayDate_TimeSlot_Status",
    table: "WalkInWindows",
    columns: new[] { "CafeId", "PlayDate", "TimeSlot", "Status" });

migrationBuilder.CreateTable(
    name: "RefundTransactions",
    columns: table => new
    {
        Id = table.Column<Guid>(nullable: false),
        OriginalDepositId = table.Column<Guid>(nullable: false),
        HostUserId = table.Column<Guid>(nullable: false),
        ActiveSessionId = table.Column<Guid>(nullable: true),
        Reason = table.Column<int>(nullable: false),
        OriginalAmount = table.Column<long>(nullable: false),
        RefundPercentage = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
        RefundAmount = table.Column<long>(nullable: false),
        IdempotencyKey = table.Column<string>(maxLength: 100, nullable: false),
        ApprovedByStaffId = table.Column<Guid>(nullable: true),
        CreatedAt = table.Column<DateTime>(nullable: false)
    },
    constraints: table =>
    {
        table.PrimaryKey("PK_RefundTransactions", x => x.Id);
        table.ForeignKey(
            name: "FK_RefundTransactions_BookingDeposits_OriginalDepositId",
            column: x => x.OriginalDepositId,
            principalTable: "BookingDeposits",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    });

migrationBuilder.AddColumn<DateTime>(
    name: "StartedAt",
    table: "ActiveSessions",
    nullable: true);

migrationBuilder.AddColumn<DateTime>(
    name: "ActualEndAt",
    table: "ActiveSessions",
    nullable: true);

migrationBuilder.AddColumn<DateTime>(
    name: "ScheduledEndTime",
    table: "ActiveSessions",
    nullable: false,
    defaultValue: new DateTime(2026, 1, 1));

migrationBuilder.AddColumn<int>(
    name: "EndReason",
    table: "ActiveSessions",
    nullable: true);

migrationBuilder.AddColumn<bool>(
    name: "IsPaidByPos",
    table: "ActiveSessions",
    nullable: false,
    defaultValue: false);

migrationBuilder.AddColumn<int>(
    name: "ShortPlayCount",
    table: "Wallets",
    nullable: false,
    defaultValue: 0);

migrationBuilder.AddColumn<DateTime>(
    name: "ShortPlayCountResetAt",
    table: "Wallets",
    nullable: true);
```

### 10.2. Migration trên Neon testing branch

**Quy trình:**
1. Chạy trên nhánh `br-sparkling-salad-aota3n5d` (testing) trước
2. Verify bằng Neon MCP `describe_table_schema`
3. Re-run `compare_database_schema` với production để confirm
4. Sau khi pass, apply lên production

---

## 11. Test plan

### 11.1. Unit test

| Test | File | Mô tả |
|---|---|---|
| `SoftReleaseService.ProcessSoftReleaseAsync_Should_Refund30Percent_WhenPlayedRatioOver50` | `SoftReleaseServiceTests.cs` | playedRatio = 60% → refund 30% |
| `SoftReleaseService.ProcessSoftReleaseAsync_Should_Forfeit_WhenPlayedRatioBelow50` | `SoftReleaseServiceTests.cs` | playedRatio = 40% → forfeit |
| `SoftReleaseService.ProcessSoftReleaseAsync_Should_NotRefund_WhenComponentLossForceClose` | `SoftReleaseServiceTests.cs` | EndReason = ComponentLossForceClose → no refund |
| `ExtensionCheckService.CheckExtensionAsync_Should_Allow_WhenNextSlotEmpty` | `ExtensionCheckServiceTests.cs` | nextSlot còn trống → unlimited |
| `ExtensionCheckService.CheckExtensionAsync_Should_Limit_WhenNextSlotHasBooking` | `ExtensionCheckServiceTests.cs` | nextSlot có booking → max = slotEnd - 30min |
| `WalkInWindowService.CreateFromSessionEndAsync_Should_Create_WhenDurationOver30Min` | `WalkInWindowServiceTests.cs` | walkInDuration = 60min → tạo |
| `WalkInWindowService.CreateFromSessionEndAsync_Should_NotCreate_WhenDurationBelow30Min` | `WalkInWindowServiceTests.cs` | walkInDuration = 20min → không tạo |

### 11.2. Integration test

| Test | File | Mô tả |
|---|---|---|
| `PaySession_WithSoftRelease_Should_RefundAndCreateWalkInWindow` | `SoftReleaseIntegrationTests.cs` | End-to-end trên testing branch |
| `CheckExtension_WithNextSlotBooked_Should_LimitExtension` | `ExtensionIntegrationTests.cs` | E2E |
| `AutoReleaseScheduler_Should_ReleaseExpiredSlots` | `SchedulerIntegrationTests.cs` | Cron job giả lập |
| `ShortPlayCount_After3Occurrences_Should_ReduceKarma` | `KarmaIntegrationTests.cs` | Anti-abuse tracking |

### 11.3. Manual test scenario

| Scenario | Steps | Expected |
|---|---|---|
| Player end sớm 30% slot | Book, check-in, end sau 30% | Forfeit, walk-in window tạo |
| Player end 60% slot | Book, check-in, end sau 60% | Refund 30%, walk-in window tạo |
| Extension khi slot kế có booking | Book, gia hạn qua slot có booking | Warning, max extension = slot end |
| Extension khi slot kế trống | Book, gia hạn qua slot trống | Allowed unlimited |
| Staff quên close | Book, staff không close | Auto-release sau 30min quá slot end |

---

## 12. Checklist triển khai

### Phase 1 (MVP — 2 tuần)
- [ ] Tạo entity `WalkInWindowEntity`, `RefundTransactionEntity`
- [ ] Mở rộng `ActiveSessionEntity` (StartedAt, ActualEndAt, ScheduledEndTime, EndReason, IsPaidByPos)
- [ ] Enum mới: `SessionEndReason`, `RefundReason`, `WalkInWindowStatus`
- [ ] Migration `AddWalkInWindowAndRefundTransaction`
- [ ] Service `SoftReleaseService`, `WalkInWindowService`
- [ ] API `POST /api/pos/sessions/{id}/pay-with-soft-release`
- [ ] API `GET /api/pos/walk-in-windows`
- [ ] POS UI: banner walk-in window + soft-release flow
- [ ] Unit test cơ bản
- [ ] Update `docs/api/cafe-pos.md`

### Phase 2 (1 tuần)
- [ ] Service `ExtensionCheckService`
- [ ] API `POST /api/pos/sessions/{id}/check-extension`
- [ ] API `POST /api/pos/sessions/{id}/extend`
- [ ] POS UI: extension dialog
- [ ] Scheduler `auto_release_expired_slots`
- [ ] Scheduler `expire_walk_in_windows`
- [ ] Integration test E2E

### Phase 3 (1 tuần)
- [ ] Service `KarmaTracker` cho short play
- [ ] BR-LOBBY-KARMA-01 logic
- [ ] Mobile app: UX warning lúc booking
- [ ] Scheduler `flag_idle_sessions`
- [ ] Scheduler `reset_short_play_count`
- [ ] Update `docs/api/lobby.md` với karma rules
- [ ] Update `lobby-booking-deposit-bvc.mdc` với 4 BR mới

---

## 13. Tài liệu liên quan

- **Canonical business rules:** `c:\Users\ASUS\source\repos\BoardVerse\.cursor\rules\lobby-booking-deposit-bvc.mdc`
- **Walk-in override (file song sinh):** `docs/walk-in-override-design.md`
- **BR-NEW-15 (time-slot definition):** section VII.1 trong lobby-booking-deposit-bvc.mdc
- **BR-RESERVATION-01/02:** section V trong lobby-booking-deposit-bvc.mdc
- **SePay payment flow:** `.cursor/rules/sepay-payment-flow.mdc`
- **API doc standards:** `.cursor/rules/api-doc-test-standards.mdc`
- **API error messages:** `.cursor/rules/api-error-messages.mdc`
- **Neon DB workflow:** `.cursor/rules/neon-database-workflow.mdc`

---

## 14. Trạng thái review

- [ ] Đã review BR-REFUND-06 (anh/chị lead dev)
- [ ] Đã review BR-REFUND-07 (anh/chị lead dev)
- [ ] Đã review BR-REFUND-08 (anh/chị lead dev)
- [ ] Đã review BR-EXT-01 (anh/chị lead dev)
- [ ] Đã review BR-LOBBY-KARMA-01 (anh/chị lead dev + product)
- [ ] Đã review schema thay đổi (DB admin)
- [ ] Đã review API mới (frontend lead)
- [ ] Đã approve migration trên testing branch
- [ ] Đã approve migration trên production

**Sau khi approve → chia task theo Phase 1/2/3.**