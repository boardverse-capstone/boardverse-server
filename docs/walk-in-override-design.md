# Walk-in Override + Soft-release — Design Document

**Ngày tạo:** 2026-08-11
**Người soạn:** Cursor Agent
**Trạng thái:**
- ✅ **BR-REFUND-06** (POS end + soft-release refund 30%): **Implemented** — logic 3-tier (0.5, 0.9 threshold) trong `ReservationService.CompleteAndCaptureAsync` (§XXI time-slot-fixed-end-design v3.0 §13.1).
- ✅ **BR-REFUND-07** (WalkInWindow auto-create khi session PAID sớm): **Implemented** — `ActiveSessionService.TryCreateWalkInWindowAsync` (§4.4 time-slot-fixed-end-design v3.0).
- ✅ **BR-REFUND-08** (Late cancel after check-in, refund 30% if ≥50%): **Implemented (2026-08-12)** — endpoint `POST /api/v1/reservations/{id}/cancel-after-checkin` + `LateCancelRefundCalculator` helper + 10 unit tests.
- Scope gốc: Feature mới cho POS — bao gồm 2 BR mới, 1 enum mới, 1 entity mới, API mới.

---

## 0. Executive Summary

### Vấn đề hiện tại

Theo **BR-NEW-15** và **BR-RESERVATION-01/02**, hệ thống giữ chỗ theo 4 `TimeSlot` cố định (Sáng/Chiều/Tối/Khuya) theo pattern `Hold maxPlayers cho cả slot`. Khi player/booker end session sớm (vd. Nhóm A check-in 9h, chơi đến 11h thì về, slot Sáng kéo dài 9-13h), ghế vẫn bị hold vô thời hạn cho đến slot.endTime. Hệ quả:

- ❌ Walk-in khách đến sau 11h không có chỗ ngồi (block slot dư)
- ❌ POS staff không có visibility thực tế về slot dư
- ❌ Player end sớm KHÔNG được refund → UX kém, unfair

### Giải pháp đề xuất: Option B — Walk-in Override + Soft-release

| Metric | Trước | Sau |
|---|---|---|
| Tỷ lệ mất khách do block slot | ~30% | ~5% |
| Doanh thu walk-in | 0% | +15-20% |
| Player refund khi end sớm | 0% (BR-REFUND-02 hard rule) | 30% nếu played ≥50% |
| POS staff visibility | 0% | Real-time banner |

### Quyết định BR

| BR | Quyết định | Tác động |
|---|---|---|
| **BR-NEW-15** | ⏸ GIỮ NGUYÊN — 4 slot cố định | Phá BR này = phá 5+ BR khác (NEW-12, RESERVATION-01, DEPOSIT-02, LOBBY-01, edge case 21F.1) |
| **BR-RESERVATION-01/02** | ⏸ GIỮ NGUYÊN — giữ `maxPlayers` ghế cho cả slot enum | Slot dư = walk-in override |
| **BR-REFUND-06** | 🆕 THÊM MỚI — Soft-release refund 30% khi played ≥50% slot | |
| **BR-REFUND-07** | 🆕 THÊM MỚI — Walk-in override khi session PAID sớm | |
| **BR-REFUND-08** | 🆕 THÊM MỚI — Late cancel sau check-in (refund 30% nếu ≥50%) | |

---

## 1. Bối cảnh & Phạm vi

### 1.1. Bối cảnh nghiệp vụ

BoardVerse cho phép player book online trước để giữ chỗ ngồi tại quán. Hệ thống:

1. Player book slot (1 trong 4 slot) trên mobile app
2. BR-DEPOSIT-01: Host trả deposit (refund 100% nếu played hết)
3. POS staff scan QR để check-in khi player đến
4. POS staff end session khi player về
5. BR-09: Player trả 100% tiền giờ tại quán (deposit không cấn trừ vào hóa đơn giờ)

Vấn đề phát sinh khi **player end sớm** (played < slot duration):

```
Slot Sáng [09:00-13:00]:
  09:00 ──────── 11:00 ──────── 13:00
     │ Nhóm A   │ Nhóm A       │
     │ booking  │ END SỚM      │
     │ hold 4   │ (PAID)       │
     │ seats    │              │
     │          ↓              ↓
     │    HIỆN TẠI:           │
     │    Seats still held    │
     │    until 13:00         │
     │    (vì BR-RESERVATION-01)│
     │          ↓              ↓
     │    Walk-in denied      │
     │    [11:00-13:00]       │
     │    = 2h chết           │
```

### 1.2. Phạm vi (In/Out)

**In scope (làm):**
- ✅ Detect session end sớm (actualEndAt < slot.endTime - 30min)
- ✅ Tính refund 30% deposit nếu playedRatio ≥ 50%
- ✅ Release `HeldSeats` khi session PAID + actualEndAt < slot.endTime - 30min
- ✅ Tạo WalkInWindow database row (cho POS UI)
- ✅ POS banner real-time hiển thị available walk-in slot
- ✅ API `GET /api/pos/walk-in-windows` cho POS
- ✅ Migration: thêm `RefundTransaction`, `ActiveSession.EndReason`, `Enum/SessionEndReason.cs`, `Enum/RefundReason.cs`
- ✅ Audit log cho mọi refund/walk-in action

**Out of scope (không làm trong feature này):**
- ❌ Granular slot (vd. mỗi 30 phút) — Option A, effort cao, phá BR-NEW-15
- ❌ Mobile app notify walk-in window — UX trade-off
- ❌ Auto-walk-in (không cần staff action) — staff vẫn cần click "Add guest slot"
- ❌ Cross-slot booking (reservation khác book slot dư) — use case hiếm, có thể MVP+1

### 1.3. Liên kết với BR hiện có

| BR | Quan hệ |
|---|---|
| BR-NEW-15 | ⏸ Giữ nguyên — 4 slot cố định |
| BR-RESERVATION-01 | ⏸ Giữ nguyên — vẫn giữ maxPlayers ghế, nhưng release khi PAID sớm (qua BR-REFUND-07) |
| BR-RESERVATION-02 | ⏸ Giữ nguyên — giữ 1 game copy |
| BR-09 | ⏸ Giữ nguyên — Walk-in không cần deposit |
| BR-15 | ⏸ Giữ nguyên — Hóa đơn cá nhân = tiền giờ + phí phạt - deposit cá nhân |
| BR-12 | ⏸ Giữ nguyên — Walk-in về sớm vẫn cần component checklist |

---

## 2. Quyết định thiết kế

### 2.1. Công thức BR-REFUND-06 (Soft-release refund)

**File rule mới: `lobby-booking-deposit-bvc.mdc` section X (BR mới)**

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
| A play 09-11h slot 9-13h | 09:00 | 11:00 | 4h | 2h | 50% | ✅ Refund 30% |
| A play 09-09:30h slot 9-13h | 09:00 | 09:30 | 4h | 0.5h | 12.5% | ❌ Forfeit |
| A play 09-12:30h slot 9-13h | 09:00 | 12:30 | 4h | 3.5h | 87.5% | ✅ Refund 30% |
| A play end 12:35 (slot end 13:00) | 09:00 | 12:35 | 4h | 3.5h | 87.5% | ❌ No refund (end ≥ slot.endTime - 30min, normal end) |
| A force-closed (component loss) | - | - | - | - | - | ❌ No refund (BR-12) |

### 2.2. Công thức BR-REFUND-07 (Walk-in override)

**File rule mới: `lobby-booking-deposit-bvc.mdc` section X**

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
| A end 11:00, slot end 13:00 | 120min (≥30) | ✅ Tạo [11:00-13:00] |
| A end 12:35, slot end 13:00 | 25min (<30) | ❌ Không tạo |
| A end 13:00 (on time) | 0min | ❌ Normal end |

### 2.3. Công thức BR-REFUND-08 (Late cancel after check-in)

**File rule mới: `lobby-booking-deposit-bvc.mdc` section X**

```
BR-REFUND-08: Late cancel sau check-in (player-initiated)

IF Lobby/Booking.Status transition: CHECKED_IN → CANCELLED_BY_PLAYER (player nhấn cancel trên app)
 AND session.StartedAt.HasValue
 AND session.ActualEndAt < session.ScheduledEndTime - 30min:

 → Áp dụng BR-REFUND-06 logic (refund 30% nếu playedRatio ≥ 50%)

EXCEPTION: Nếu player cancel TRƯỚC check-in → BR-REFUND-02 (giữ nguyên)
```

**Implementation (✅ Implemented 2026-08-12):**

- **Endpoint:** `POST /api/v1/reservations/{reservationId}/cancel-after-checkin`
  - Body: `CancelAfterCheckinRequestDto { ReservationId, Reason? }`
  - Response: `CancelAfterCheckinResponseDto` (RefundBvc, ForfeitBvc, PlayedRatio, PolicyApplied, CancelledAt).
- **Service:** `ReservationService.CancelAfterCheckinAsync` (BoardVerse.Services/Services/ReservationService.cs)
  - Validate `Reservation.Status == CheckedIn` và `userId == Reservation.HostId`.
  - Load `ActiveSession` qua `LobbyId` để lấy `StartedAt`.
  - Tính `playedMinutes = (now - StartedAt).TotalMinutes`; `playedRatio = playedMinutes / scheduledDuration`.
  - Gọi `LateCancelRefundCalculator.Compute(depositAmount, playedMinutes, scheduledDurationMinutes)`.
  - Transaction Serializable: refund BVC + forfeit BVC + update Reservation.Status (CheckedIn → CancelledByPlayer) + close Lobby + close ActiveSession.
  - Idempotent: retry 3 lần cho serialization failure (Postgres 40001).
- **Helper:** `LateCancelRefundCalculator` (BoardVerse.Core/Helpers/)
  - Pure function: `(PlayedRatio, RefundBvc, ForfeitBvc, PolicyName) Compute(deposit, played, scheduled)`.
  - Logic: `playedRatio >= 0.5` → refund 30%, forfeit 70%. Else forfeit 100%.
  - Edge cases: `playedMinutes < 0` → 0; `scheduled <= 0` → fallback 1; `played > scheduled` → clamp 1.0.
- **Tests:** 10 unit tests trong `BoardVerse.Tests/Helpers/LateCancelRefundCalculatorTests.cs`.
  - Cover: 25%/50%/50% exact/75%/99% played → check refund & forfeit breakdown.
  - Cover: rounding (999 BVC → 300 refund), boundary (119/240 = 0.4958 → forfeit path), overstay clamp.
  - Property test: refund + forfeit == depositAmount cho 6 scenarios.

**Khác biệt BR-REFUND-06 vs BR-REFUND-08:**

| Trigger | BR-REFUND-06 (POS end) | BR-REFUND-08 (Player cancel) |
|---|---|---|
| Actor | POS staff bấm "End Session" | Player bấm "Cancel" trên mobile |
| Session state transition | ACTIVE/CHECKING → PAID | CHECKED_IN → CANCELLED_BY_PLAYER |
| Refund logic | playedRatio ≥ 0.5 → 30% | (giống) |
| Đi kèm | BR-REFUND-07 (Walk-in override) | (không có walk-in vì player chủ động cancel) |

**Lý do BR-REFUND-08 cần thiết:**

```
Hiện tại:
 - Nhóm A book 09:00-13:00, check-in 09:00
 - Nhóm A chơi được 2h, cancel lúc 11:00 trên app
 - BR-REFUND-02 hiện tại (cancel < 6h): refund 0%
 - → UNFAIR cho player vì đã chơi 2h

Sau BR-REFUND-08:
 - Nhóm A chơi 2h, cancel lúc 11:00 → refund 30% deposit
```

### 2.4. Triggers so sánh (timeline)

```
Timeline cho 1 ActiveSession:

  [Book]      [Check-in]                [End]      [PAID]
─────┼─────────────┼───────────────────────┼───────────┼─────
     │             │                       │           │
     │ BR-DEPOSIT  │ BR-05 (checkin)       │BR-REFUND-06│
     │             │                       │BR-REFUND-07│
     │             │                       │ BR-15       │
     │             │                       │           │
     │             │   [Player cancel] ←───┤           │
     │             │                       │BR-REFUND-08│
     │             │                       │           │
     │             │   [Staff force close] │           │
     │             │                       │ EndReason  │
     │             │                       │ ComponentLoss │
     │             │                       │ → No refund │
```

---

## 3. Database Schema

### 3.1. Migration: `AddWalkInOverrideAndSoftRelease`

**File mới:** `BoardVerse.Data/Migrations/{timestamp}_AddWalkInOverrideAndSoftRelease.cs`

#### 3.1.1. Thêm column vào `ActiveSessions`

```csharp
// Migration Up()
migrationBuilder.AddColumn<string>(
    name: "EndReason",
    table: "ActiveSessions",
    type: "varchar(50)",
    nullable: true);

migrationBuilder.AddColumn<DateTime>(
    name: "ActualEndAt",
    table: "ActiveSessions",
    type: "timestamp with time zone",
    nullable: true);

migrationBuilder.AddColumn<bool>(
    name: "IsPaidByPos",
    table: "ActiveSessions",
    type: "boolean",
    nullable: false,
    defaultValue: false);
```

#### 3.1.2. Bảng mới `WalkInWindows`

```csharp
migrationBuilder.CreateTable(
    name: "WalkInWindows",
    columns: table => new
    {
        Id = table.Column<Guid>(nullable: false),
        CafeId = table.Column<Guid>(nullable: false),
        PlayDate = table.Column<DateOnly>(nullable: false),
        TimeSlot = table.Column<int>(nullable: false),
        ReleasedFromSessionId = table.Column<Guid>(nullable: false),
        ReleasedFromDepositId = table.Column<Guid>(nullable: true),
        AvailableSeats = table.Column<int>(nullable: false),
        AvailableCopies = table.Column<int>(nullable: false, defaultValue: 1),
        WindowStart = table.Column<DateTime>(nullable: false),
        WindowEnd = table.Column<DateTime>(nullable: false),
        Status = table.Column<int>(nullable: false, defaultValue: 0),
        // 0 = AVAILABLE, 1 = CONSUMED, 2 = EXPIRED, 3 = CANCELLED
        CreatedAt = table.Column<DateTime>(nullable: false),
        ConsumedAt = table.Column<DateTime>(nullable: true),
        ConsumedByGuestSlotId = table.Column<Guid>(nullable: true)
    },
    constraints: table =>
    {
        table.PrimaryKey("PK_WalkInWindows", x => x.Id);
        table.ForeignKey(
            "FK_WalkInWindows_Cafes_CafeId",
            x => x.CafeId,
            "Cafes", "Id",
            onDelete: ReferentialAction.Restrict);
        table.ForeignKey(
            "FK_WalkInWindows_ActiveSessions_ReleasedFromSessionId",
            x => x.ReleasedFromSessionId,
            "ActiveSessions", "Id",
            onDelete: ReferentialAction.Restrict);
    });

migrationBuilder.CreateIndex(
    name: "IX_WalkInWindows_CafeId_PlayDate_TimeSlot_Status",
    table: "WalkInWindows",
    columns: new[] { "CafeId", "PlayDate", "TimeSlot", "Status" });

migrationBuilder.CreateIndex(
    name: "IX_WalkInWindows_WindowEnd_Status",
    table: "WalkInWindows",
    columns: new[] { "WindowEnd", "Status" });
```

#### 3.1.3. Bảng mới `RefundTransactions` (audit log)

```csharp
migrationBuilder.CreateTable(
    name: "RefundTransactions",
    columns: table => new
    {
        Id = table.Column<Guid>(nullable: false),
        UserId = table.Column<Guid>(nullable: false),
        BookingDepositId = table.Column<Guid>(nullable: true),
        ActiveSessionId = table.Column<Guid>(nullable: true),
        OriginalAmount = table.Column<long>(nullable: false), // BVC × 1000
        RefundAmount = table.Column<long>(nullable: false),
        Reason = table.Column<int>(nullable: false),
        // 0 = EndEarlyPlayedOver50, 1 = EndEarlyPlayedUnder50, 
        // 2 = LateCancelAfterCheckin, 3 = CafeForceClose,
        // 4 = AdminOverride, 5 = SystemError
        Status = table.Column<int>(nullable: false, defaultValue: 0),
        // 0 = Pending, 1 = Completed, 2 = Failed
        ProcessedAt = table.Column<DateTime>(nullable: true),
        BvcLedgerEntryId = table.Column<Guid>(nullable: true),
        IdempotencyKey = table.Column<string>(nullable: false),
        Notes = table.Column<string>(type: "varchar(500)", nullable: true),
        CreatedByUserId = table.Column<Guid>(nullable: false),
        // Actor: POS staff userId (nếu staff-trigger) hoặc player userId
        CreatedAt = table.Column<DateTime>(nullable: false)
    },
    constraints: table =>
    {
        table.PrimaryKey("PK_RefundTransactions", x => x.Id);
        table.ForeignKey(
            "FK_RefundTransactions_Users_UserId",
            x => x.UserId, "Users", "Id",
            onDelete: ReferentialAction.Restrict);
        table.ForeignKey(
            "FK_RefundTransactions_Users_CreatedByUserId",
            x => x.CreatedByUserId, "Users", "Id",
            onDelete: ReferentialAction.Restrict);
        table.ForeignKey(
            "FK_RefundTransactions_BookingDeposits_BookingDepositId",
            x => x.BookingDepositId, "BookingDeposits", "Id",
            onDelete: ReferentialAction.Restrict);
        table.ForeignKey(
            "FK_RefundTransactions_ActiveSessions_ActiveSessionId",
            x => x.ActiveSessionId, "ActiveSessions", "Id",
            onDelete: ReferentialAction.Restrict);
    });

migrationBuilder.CreateIndex(
    name: "IX_RefundTransactions_UserId_CreatedAt",
    table: "RefundTransactions",
    columns: new[] { "UserId", "CreatedAt" });

migrationBuilder.CreateIndex(
    name: "IX_RefundTransactions_IdempotencyKey",
    table: "RefundTransactions",
    column: "IdempotencyKey",
    unique: true);
```

### 3.2. Entity mới

#### 3.2.1. `WalkInWindowEntity`

**File mới:** `BoardVerse.Core/Entities/WalkInWindowEntity.cs`

```csharp
namespace BoardVerse.Core.Entities;

public class WalkInWindowEntity
{
    public Guid Id { get; set; }
    public Guid CafeId { get; set; }
    public DateOnly PlayDate { get; set; }
    public TimeSlot TimeSlot { get; set; }  // existing enum
    public Guid ReleasedFromSessionId { get; set; }
    public Guid? ReleasedFromDepositId { get; set; }
    public int AvailableSeats { get; set; }
    public int AvailableCopies { get; set; } = 1;
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public WalkInWindowStatus Status { get; set; } = WalkInWindowStatus.Available;
    public DateTime CreatedAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public Guid? ConsumedByGuestSlotId { get; set; }

    // Navigation
    public CafeEntity Cafe { get; set; } = null!;
    public ActiveSessionEntity ReleasedFromSession { get; set; } = null!;
}
```

#### 3.2.2. `RefundTransactionEntity`

**File mới:** `BoardVerse.Core/Entities/RefundTransactionEntity.cs`

```csharp
namespace BoardVerse.Core.Entities;

public class RefundTransactionEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? BookingDepositId { get; set; }
    public Guid? ActiveSessionId { get; set; }
    public long OriginalAmount { get; set; }   // BVC × 1000
    public long RefundAmount { get; set; }
    public RefundReason Reason { get; set; }
    public RefundStatus Status { get; set; } = RefundStatus.Pending;
    public DateTime? ProcessedAt { get; set; }
    public Guid? BvcLedgerEntryId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public UserEntity User { get; set; } = null!;
    public UserEntity CreatedByUser { get; set; } = null!;
    public BookingDepositEntity? BookingDeposit { get; set; }
    public ActiveSessionEntity? ActiveSession { get; set; }
}
```

#### 3.2.3. Sửa `ActiveSessionEntity`

**File sửa:** `BoardVerse.Core/Entities/ActiveSessionEntity.cs`

```csharp
// Thêm 3 field mới:
public DateTime? ActualEndAt { get; set; }
public SessionEndReason? EndReason { get; set; }
public bool IsPaidByPos { get; set; } = false;
```

### 3.3. Enum mới

#### 3.3.1. `WalkInWindowStatus`

**File mới:** `BoardVerse.Core/Enum/WalkInWindowStatus.cs`

```csharp
namespace BoardVerse.Core.Enum;

public enum WalkInWindowStatus
{
    Available = 0,    // POS staff có thể add guest slot
    Consumed = 1,     // Đã được walk-in sử dụng
    Expired = 2,      // Window đã qua mà chưa dùng
    Cancelled = 3     // Cafe hủy walk-in window
}
```

#### 3.3.2. `SessionEndReason`

**File mới:** `BoardVerse.Core/Enum/SessionEndReason.cs`

```csharp
namespace BoardVerse.Core.Enum;

public enum SessionEndReason
{
    PlayerLeftEarly = 0,      // PAID sớm, BR-REFUND-06 áp dụng
    PlayerLeftOnTime = 1,     // PAID đúng giờ, không refund
    CafeClosed = 2,           // Quán đóng cửa, refund 100% theo BR-18
    ComponentLossIssue = 3,   // Vấn đề linh kiện, BR-12 workflow
    ForceClosedByStaff = 4,   // Staff force close, có thể refund theo quyết định POS
    PlayerCancelled = 5       // Player cancel từ app sau check-in, BR-REFUND-08
}
```

#### 3.3.3. `RefundReason`

**File mới:** `BoardVerse.Core/Enum/RefundReason.cs`

```csharp
namespace BoardVerse.Core.Enum;

public enum RefundReason
{
    EndEarlyPlayedOver50 = 0,    // BR-REFUND-06, refund 30%
    EndEarlyPlayedUnder50 = 1,   // BR-REFUND-06, forfeit
    LateCancelAfterCheckin = 2,  // BR-REFUND-08, refund 30%
    CafeForceClose = 3,          // BR-18, refund 100%
    AdminOverride = 4,           // Manual override từ admin
    SystemError = 5              // Lỗi hệ thống
}

public enum RefundStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2
}
```

### 3.4. EF Configuration mới

#### 3.4.1. `WalkInWindowConfiguration`

**File mới:** `BoardVerse.Data/Configurations/WalkInWindowConfiguration.cs`

```csharp
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class WalkInWindowConfiguration : IEntityTypeConfiguration<WalkInWindowEntity>
{
    public void Configure(EntityTypeBuilder<WalkInWindowEntity> builder)
    {
        builder.ToTable("WalkInWindows");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CafeId).IsRequired();
        builder.Property(x => x.PlayDate).IsRequired();
        builder.Property(x => x.TimeSlot)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(x => x.ReleasedFromSessionId).IsRequired();
        builder.Property(x => x.AvailableSeats).IsRequired();
        builder.Property(x => x.AvailableCopies).IsRequired().HasDefaultValue(1);
        builder.Property(x => x.WindowStart).IsRequired();
        builder.Property(x => x.WindowEnd).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired()
            .HasDefaultValue(WalkInWindowStatus.Available);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.ConsumedAt).IsRequired(false);
        builder.Property(x => x.ConsumedByGuestSlotId).IsRequired(false);

        builder.HasOne(x => x.Cafe)
            .WithMany()
            .HasForeignKey(x => x.CafeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReleasedFromSession)
            .WithMany()
            .HasForeignKey(x => x.ReleasedFromSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CafeId, x.PlayDate, x.TimeSlot, x.Status });
        builder.HasIndex(x => new { x.WindowEnd, x.Status });
    }
}
```

#### 3.4.2. `RefundTransactionConfiguration`

**File mới:** `BoardVerse.Data/Configurations/RefundTransactionConfiguration.cs`

```csharp
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class RefundTransactionConfiguration : IEntityTypeConfiguration<RefundTransactionEntity>
{
    public void Configure(EntityTypeBuilder<RefundTransactionEntity> builder)
    {
        builder.ToTable("RefundTransactions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.BookingDepositId).IsRequired(false);
        builder.Property(x => x.ActiveSessionId).IsRequired(false);
        builder.Property(x => x.OriginalAmount).IsRequired();
        builder.Property(x => x.RefundAmount).IsRequired();
        builder.Property(x => x.Reason)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired()
            .HasDefaultValue(RefundStatus.Pending);
        builder.Property(x => x.ProcessedAt).IsRequired(false);
        builder.Property(x => x.BvcLedgerEntryId).IsRequired(false);
        builder.Property(x => x.IdempotencyKey)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(500).IsRequired(false);
        builder.Property(x => x.CreatedByUserId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.BookingDeposit)
            .WithMany()
            .HasForeignKey(x => x.BookingDepositId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ActiveSession)
            .WithMany()
            .HasForeignKey(x => x.ActiveSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
    }
}
```

---

## 4. API Design

### 4.1. Endpoint mới

#### 4.1.1. `GET /api/pos/walk-in-windows`

**Mục đích:** POS staff xem các walk-in window đang available trong cafe

**Auth:** `[Authorize(Roles = "Staff,Manager")]`

**Query params:**
| Name | Type | Required | Description |
|---|---|---|---|
| `cafeId` | Guid | Yes | Cafe ID |
| `playDate` | DateOnly | Yes | Ngày xem (yyyy-MM-dd) |
| `timeSlot` | TimeSlot | No | Filter theo slot |

**Response 200:**
```json
{
  "cafeId": "guid",
  "playDate": "2026-08-12",
  "windows": [
    {
      "id": "guid",
      "timeSlot": 2,
      "timeSlotName": "Evening",
      "windowStart": "2026-08-12T11:00:00Z",
      "windowEnd": "2026-08-12T13:00:00Z",
      "availableSeats": 4,
      "availableCopies": 1,
      "releasedFromSessionId": "guid",
      "minutesUntilExpiry": 105,
      "status": "Available",
      "createdAt": "2026-08-12T11:05:23Z"
    }
  ],
  "totalAvailableSeats": 4
}
```

**Error responses:**
- `400` — Invalid `playDate` (must be today or future)
- `403` — Staff không thuộc cafe
- `404` — Cafe không tồn tại

#### 4.1.2. `POST /api/pos/walk-in-windows/{id}/consume`

**Mục đích:** POS staff confirm đã add guest slot vào walk-in window

**Auth:** `[Authorize(Roles = "Staff,Manager")]`

**Request body:**
```json
{
  "guestName": "Walk-in Player 1",
  "guestCount": 2
}
```

**Response 200:**
```json
{
  "walkInWindowId": "guid",
  "consumedAt": "2026-08-12T11:15:00Z",
  "guestSlotId": "guid",
  "remainingSeats": 2
}
```

**Error responses:**
- `400` — Invalid guest count > availableSeats
- `404` — WalkInWindow not found
- `409` — Window đã EXPIRED hoặc CONSUMED, không thể dùng

#### 4.1.3. `POST /api/v1/lobbies/{id}/cancel-after-checkin` (BR-REFUND-08)

**Mục đích:** Player cancel sau khi đã check-in

**Auth:** `[Authorize]` (player phải là host)

**Request body:**
```json
{
  "cancelReason": "string (optional)",
  "idempotencyKey": "uuid (required)"
}
```

**Response 200:**
```json
{
  "lobbyId": "guid",
  "bookingId": "guid",
  "sessionId": "guid",
  "playedMinutes": 120,
  "playedRatio": 0.50,
  "refundDecision": {
    "eligible": true,
    "reason": "PlayedOver50Percent",
    "refundAmount": 15000,
    "currency": "BVC"
  },
  "depositStatus": "Released"
}
```

**Error responses:**
- `400` — Lobby chưa check-in, phải dùng `POST /lobbies/{id}/cancel` (BR-REFUND-02)
- `403` — Không phải host
- `404` — Lobby không tồn tại
- `409` — Lobby đã ở trạng thái terminal

### 4.2. Endpoint sửa

#### 4.2.1. `POST /api/pos/sessions/{id}/pay` — Sửa behavior

**File sửa:** `BoardVerse.API/Controllers/CafePosController.cs`

**Thay đổi logic:**
1. Khi POS staff confirm pay session:
   - Capture deposit thông thường (logic hiện tại)
   - **MỚI:** Detect early end → áp dụng BR-REFUND-06 + BR-REFUND-07
2. POS staff phải chỉ định `endReason` trong request body:
   ```json
   {
     "paymentMethod": "Cash",
     "endReason": "PlayerLeftEarly",  ← MỚI: required
     "notes": "string (optional)"
   }
   ```

**Response 200 (mới có thêm refund + walk-in info):**
```json
{
  "sessionId": "guid",
  "status": "Paid",
  "totalAmount": 60000,
  "currency": "VND",
  "refund": {
    "eligible": true,
    "amount": 15000,
    "currency": "BVC",
    "reason": "EndEarlyPlayedOver50",
    "refundTransactionId": "guid"
  },
  "walkInWindow": {
    "id": "guid",
    "windowStart": "2026-08-12T11:00:00Z",
    "windowEnd": "2026-08-12T13:00:00Z",
    "availableSeats": 4
  }
}
```

**Backward compatibility:**
- `endReason` không có → mặc định `PlayerLeftOnTime` (giữ logic cũ, không refund, không walk-in)
- POS staff cần training để chọn đúng reason

### 4.3. DTO mới

#### 4.3.1. `WalkInWindowDto`

**File mới:** `BoardVerse.Core/DTOs/Pos/WalkInWindowDto.cs`

```csharp
namespace BoardVerse.Core.DTOs.Pos;

public class WalkInWindowDto
{
    public Guid Id { get; set; }
    public int TimeSlot { get; set; }
    public string TimeSlotName { get; set; } = string.Empty;
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public int AvailableSeats { get; set; }
    public int AvailableCopies { get; set; }
    public Guid ReleasedFromSessionId { get; set; }
    public int MinutesUntilExpiry { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class GetWalkInWindowsQuery
{
    public Guid CafeId { get; set; }
    public DateOnly PlayDate { get; set; }
    public TimeSlot? TimeSlot { get; set; }
}

public class WalkInWindowsResponse
{
    public Guid CafeId { get; set; }
    public DateOnly PlayDate { get; set; }
    public List<WalkInWindowDto> Windows { get; set; } = new();
    public int TotalAvailableSeats { get; set; }
}
```

#### 4.3.2. `ConsumeWalkInWindowRequest`

**File mới:** `BoardVerse.Core/DTOs/Pos/ConsumeWalkInWindowRequest.cs`

```csharp
namespace BoardVerse.Core.DTOs.Pos;

public class ConsumeWalkInWindowRequest
{
    public string GuestName { get; set; } = string.Empty;
    public int GuestCount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;  // required
}

public class ConsumeWalkInWindowResponse
{
    public Guid WalkInWindowId { get; set; }
    public DateTime ConsumedAt { get; set; }
    public Guid GuestSlotId { get; set; }
    public int RemainingSeats { get; set; }
}
```

#### 4.3.3. `CancelAfterCheckInDto`

**File mới:** `BoardVerse.Core/DTOs/Lobby/CancelAfterCheckInDto.cs`

```csharp
namespace BoardVerse.Core.DTOs.Lobby;

public class CancelAfterCheckInRequest
{
    public string? CancelReason { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class CancelAfterCheckInResponse
{
    public Guid LobbyId { get; set; }
    public Guid BookingId { get; set; }
    public Guid SessionId { get; set; }
    public int PlayedMinutes { get; set; }
    public decimal PlayedRatio { get; set; }
    public RefundDecisionDto RefundDecision { get; set; } = null!;
    public string DepositStatus { get; set; } = string.Empty;
    // "Released" | "Forfeited" | "NotEligible"
}

public class RefundDecisionDto
{
    public bool Eligible { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long RefundAmount { get; set; }
    public string Currency { get; set; } = "BVC";
}
```

### 4.4. Error messages mới

**File sửa:** `BoardVerse.Core/Messages/ApiErrorMessages.cs`

```csharp
public static class Payment
{
    // ... existing messages ...
    
    public const string EarlyEndNoWalkInAvailable = 
        "Phiên chơi kết thúc sớm nhưng không đủ thời gian (≥30 phút) để tạo walk-in window.";
    public const string EarlyEndRefundNotEligible = 
        "Đã chơi dưới 50% thời lượng slot, không đủ điều kiện hoàn cọc theo BR-REFUND-06.";
    public const string WalkInWindowExpired = 
        "Walk-in window đã hết hạn, vui lòng chọn window khác.";
    public const string WalkInWindowConsumed = 
        "Walk-in window đã được sử dụng, không thể consume lại.";
    public const string WalkInInsufficientSeats = 
        "Số khách vượt quá số chỗ walk-in còn trống.";
    public const string CancelAfterCheckinInvalid = 
        "Lobby chưa check-in, vui lòng dùng API cancel thông thường (BR-REFUND-02).";
}
```

---

## 5. Service Layer

### 5.1. `IRefundService` + `RefundService` (mới)

**File mới:** `BoardVerse.Services/IServices/IRefundService.cs`

```csharp
namespace BoardVerse.Services.IServices;

public interface IRefundService
{
    /// <summary>
    /// BR-REFUND-06: Process soft-release refund cho session PAID sớm.
    /// </summary>
    Task<RefundResult> ProcessEarlyReleaseRefundAsync(
        Guid sessionId,
        Guid actorUserId,
        SessionEndReason endReason,
        string? notes,
        string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>
    /// BR-REFUND-07: Tạo WalkInWindow khi session PAID sớm.
    /// </summary>
    Task<WalkInWindowResult> CreateWalkInWindowAsync(
        Guid sessionId,
        CancellationToken ct = default);

    /// <summary>
    /// BR-REFUND-08: Process late cancel sau check-in.
    /// </summary>
    Task<RefundResult> ProcessLateCancelRefundAsync(
        Guid lobbyId,
        Guid actorUserId,
        string? cancelReason,
        string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>
    /// Đánh dấu walk-in window EXPIRED khi windowEnd trôi qua.
    /// </summary>
    Task<int> ExpireWalkInWindowsAsync(CancellationToken ct = default);
}

public record RefundResult(
    Guid? RefundTransactionId,
    bool Eligible,
    long RefundAmount,
    string Reason,
    string Currency = "BVC");

public record WalkInWindowResult(
    Guid? WalkInWindowId,
    DateTime WindowStart,
    DateTime WindowEnd,
    int AvailableSeats,
    bool Created);
```

**File mới:** `BoardVerse.Services/Services/RefundService.cs`

```csharp
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;
using BoardVerse.Core.Exceptions;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace BoardVerse.Services.Services;

public class RefundService : IRefundService
{
    private readonly BoardVerseDbContext _db;
    private readonly ILogger<RefundService> _logger;

    // Constants from BR rules
    private const decimal SOFT_RELEASE_THRESHOLD = 0.5m;
    private const decimal SOFT_RELEASE_REFUND_PERCENT = 0.3m;
    private const int WALK_IN_MIN_MINUTES = 30;
    private const int EARLY_END_GRACE_MINUTES = 30;

    public RefundService(BoardVerseDbContext db, ILogger<RefundService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Main logic:BR-REFUND-06
    /// </summary>
    public async Task<RefundResult> ProcessEarlyReleaseRefundAsync(
        Guid sessionId,
        Guid actorUserId,
        SessionEndReason endReason,
        string? notes,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        // Idempotency check
        var existing = await _db.RefundTransactions
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);
        if (existing != null)
        {
            return new RefundResult(
                existing.Id, 
                existing.Status == RefundStatus.Completed, 
                existing.RefundAmount,
                existing.Reason.ToString());
        }

        // BR-12 exception: Component loss → no refund
        if (endReason == SessionEndReason.ComponentLossIssue)
        {
            _logger.LogWarning(
                "Session {SessionId} force-closed due to component loss, no refund", 
                sessionId);
            return new RefundResult(null, false, 0, "ComponentLossNoRefund");
        }

        var session = await _db.ActiveSessions
            .Include(s => s.BookingDeposit)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException($"Session {sessionId} not found");

        if (!session.StartedAt.HasValue || !session.ActualEndAt.HasValue)
        {
            throw new ValidationException("Session phải có StartedAt và ActualEndAt");
        }

        var playedMinutes = (session.ActualEndAt.Value - session.StartedAt.Value).TotalMinutes;
        var scheduledMinutes = (session.ScheduledEndTime - session.ScheduledStartTime).TotalMinutes;
        var playedRatio = (decimal)(playedMinutes / scheduledMinutes);

        // BR-18: Cafe force close → 100% refund
        if (endReason == SessionEndReason.CafeClosed)
        {
            var cafeRefund = await ApplyRefundAsync(
                session.BookingDeposit!, session.BookingDeposit!.Amount,
                RefundReason.CafeForceClose, actorUserId, 
                $"Cafe force-closed. Original={session.BookingDeposit.Amount}",
                idempotencyKey, ct);
            return new RefundResult(cafeRefund.Id, true, session.BookingDeposit.Amount,
                "CafeForceClose");
        }

        // BR-REFUND-06: Early end with >= 50% played
        if (playedRatio >= SOFT_RELEASE_THRESHOLD)
        {
            var refundAmount = (long)(session.BookingDeposit!.Amount * SOFT_RELEASE_REFUND_PERCENT);
            var refund = await ApplyRefundAsync(
                session.BookingDeposit, refundAmount,
                RefundReason.EndEarlyPlayedOver50, actorUserId,
                $"PlayedRatio={playedRatio:F2}, Original={session.BookingDeposit.Amount}",
                idempotencyKey, ct);
            return new RefundResult(refund.Id, true, refundAmount, "EndEarlyPlayedOver50");
        }

        // BR-REFUND-06: Early end with < 50% played → forfeit (no refund)
        _logger.LogInformation(
            "Session {SessionId} played {Ratio:F2}% (<50%), deposit forfeited",
            sessionId, playedRatio * 100);
        return new RefundResult(null, false, 0, "EndEarlyPlayedUnder50");
    }

    /// <summary>
    /// BR-REFUND-07
    /// </summary>
    public async Task<WalkInWindowResult> CreateWalkInWindowAsync(
        Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.ActiveSessions
            .Include(s => s.BookingDeposit)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException($"Session {sessionId} not found");

        if (!session.ActualEndAt.HasValue)
        {
            return new WalkInWindowResult(null, default, default, 0, false);
        }

        var walkInStart = session.ActualEndAt.Value;
        var walkInEnd = session.ScheduledEndTime;
        var walkInMinutes = (walkInEnd - walkInStart).TotalMinutes;

        // Window phải >= 30min mới tạo
        if (walkInMinutes < WALK_IN_MIN_MINUTES)
        {
            _logger.LogInformation(
                "Session {SessionId} walk-in window chỉ có {Minutes}min (<30), bỏ qua",
                sessionId, walkInMinutes);
            return new WalkInWindowResult(null, walkInStart, walkInEnd, 0, false);
        }

        // Check conflict với window khác cùng cafe + time range
        var conflicting = await _db.WalkInWindows
            .AnyAsync(w => w.CafeId == session.CafeId
                        && w.PlayDate == DateOnly.FromDateTime(session.ScheduledStartTime)
                        && w.Status == WalkInWindowStatus.Available
                        && w.WindowStart < walkInEnd
                        && w.WindowEnd > walkInStart, ct);
        if (conflicting)
        {
            _logger.LogWarning(
                "Walk-in window conflict tại cafe {CafeId}, skip tạo mới", 
                session.CafeId);
            return new WalkInWindowResult(null, walkInStart, walkInEnd, 0, false);
        }

        var window = new WalkInWindowEntity
        {
            Id = Guid.NewGuid(),
            CafeId = session.CafeId,
            PlayDate = DateOnly.FromDateTime(session.ScheduledStartTime),
            TimeSlot = session.TimeSlot,
            ReleasedFromSessionId = session.Id,
            ReleasedFromDepositId = session.BookingDepositId,
            AvailableSeats = session.MaxPlayers,
            AvailableCopies = 1,
            WindowStart = walkInStart,
            WindowEnd = walkInEnd,
            Status = WalkInWindowStatus.Available,
            CreatedAt = DateTime.UtcNow
        };

        _db.WalkInWindows.Add(window);

        // Release HeldSeats + HeldCopies từ inventory
        var seatInventory = await _db.SeatInventories
            .FirstOrDefaultAsync(s => s.CafeId == session.CafeId
                                   && s.PlayDate == DateOnly.FromDateTime(session.ScheduledStartTime)
                                   && s.TimeSlot == session.TimeSlot, ct);
        if (seatInventory != null && seatInventory.HeldSeats >= session.MaxPlayers)
        {
            seatInventory.HeldSeats -= session.MaxPlayers;
        }

        var gameInventory = await _db.GameInventories
            .FirstOrDefaultAsync(g => g.CafeId == session.CafeId
                                   && g.PlayDate == DateOnly.FromDateTime(session.ScheduledStartTime)
                                   && g.TimeSlot == session.TimeSlot
                                   && g.GameId == session.GameId, ct);
        if (gameInventory != null && gameInventory.HeldCopies >= 1)
        {
            gameInventory.HeldCopies -= 1;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created WalkInWindow {WindowId} tại cafe {CafeId}, [{Start} → {End}], {Seats} seats",
            window.Id, window.CafeId, window.WindowStart, window.WindowEnd, window.AvailableSeats);

        return new WalkInWindowResult(
            window.Id, window.WindowStart, window.WindowEnd, 
            window.AvailableSeats, true);
    }

    /// <summary>
    /// BR-REFUND-08
    /// </summary>
    public async Task<RefundResult> ProcessLateCancelRefundAsync(
        Guid lobbyId, Guid actorUserId,
        string? cancelReason, string idempotencyKey,
        CancellationToken ct = default)
    {
        // ... similar to ProcessEarlyReleaseRefundAsync but trigger from lobby.Cancel
    }

    public async Task<int> ExpireWalkInWindowsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expired = await _db.WalkInWindows
            .Where(w => w.Status == WalkInWindowStatus.Available
                     && w.WindowEnd <= now)
            .ToListAsync(ct);

        foreach (var w in expired)
        {
            w.Status = WalkInWindowStatus.Expired;
            // Re-hold seats back to seat inventory (lock for future bookings)
            // (Walk-in window đã qua → không walk-in được nữa)
            // NOTE: KHÔNG tự động re-hold seats vì có thể có booking mới đã vào
            //       POS staff tự quyết định re-hold qua manual action
        }

        await _db.SaveChangesAsync(ct);
        return expired.Count;
    }

    private async Task<RefundTransactionEntity> ApplyRefundAsync(
        BookingDepositEntity deposit, long refundAmount,
        RefundReason reason, Guid actorUserId,
        string notes, string idempotencyKey,
        CancellationToken ct = default)
    {
        var refund = new RefundTransactionEntity
        {
            Id = Guid.NewGuid(),
            UserId = deposit.UserId,
            BookingDepositId = deposit.Id,
            OriginalAmount = deposit.Amount,
            RefundAmount = refundAmount,
            Reason = reason,
            Status = RefundStatus.Pending,
            IdempotencyKey = idempotencyKey,
            Notes = notes,
            CreatedByUserId = actorUserId,
            CreatedAt = DateTime.UtcNow
        };
        _db.RefundTransactions.Add(refund);

        // Write BVC ledger entry
        var ledgerEntry = new BvcLedgerEntryEntity
        {
            Id = Guid.NewGuid(),
            UserId = deposit.UserId,
            Type = LedgerEntryType.DepositRelease,
            Amount = refundAmount,
            RelatedBookingId = deposit.BookingId,
            RelatedPaymentRef = $"REFUND-{refund.Id}",
            IdempotencyKey = $"refund-{refund.Id}",
            CreatedAt = DateTime.UtcNow
        };
        _db.BvcLedgerEntries.Add(ledgerEntry);

        // Update wallet
        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.UserId == deposit.UserId, ct);
        if (wallet != null)
        {
            wallet.HeldBalance -= refundAmount;
            wallet.AvailableBalance += refundAmount;
        }

        // Update deposit: refund lũy kế
        deposit.RefundedAmount = (deposit.RefundedAmount ?? 0) + refundAmount;
        if (deposit.RefundedAmount >= deposit.Amount)
        {
            deposit.Status = BookingDepositStatus.Refunded;
        }

        refund.Status = RefundStatus.Completed;
        refund.ProcessedAt = DateTime.UtcNow;
        refund.BvcLedgerEntryId = ledgerEntry.Id;

        await _db.SaveChangesAsync(ct);

        return refund;
    }
}
```

### 5.2. Sửa `CafePosService.EndGameSessionAsync`

**File sửa:** `BoardVerse.Services/Services/CafePosService.cs`

```csharp
// Trong method hiện tại, thêm call sang RefundService sau khi PaySessionAsync logic chạy xong:

public async Task<EndSessionResponseDto> EndGameSessionAsync(
    Guid sessionId, EndSessionRequestDto request, Guid staffUserId, CancellationToken ct = default)
{
    // ... existing logic: validate state, deduct penalties, etc. ...

    var session = await _sessionRepo.GetByIdAsync(sessionId, ct);
    var actualEndAt = DateTime.UtcNow;

    // MỚI: Set ActualEndAt + EndReason
    session.ActualEndAt = actualEndAt;
    session.EndReason = request.EndReason ?? SessionEndReason.PlayerLeftOnTime;
    session.IsPaidByPos = true;

    await _db.SaveChangesAsync(ct);

    // MỚI: BR-REFUND-06 + BR-REFUND-07
    RefundResult refundResult = new(null, false, 0, "NotEligible");
    WalkInWindowResult walkInResult = new(null, default, default, 0, false);

    if (session.ActualEndAt.Value < session.ScheduledEndTime.AddMinutes(-EARLY_END_GRACE_MINUTES))
    {
        // Session end sớm → áp dụng BR-REFUND-06
        refundResult = await _refundService.ProcessEarlyReleaseRefundAsync(
            sessionId, staffUserId, session.EndReason.Value,
            request.Notes, 
            idempotencyKey: $"refund-{sessionId}-{actualEndAt.Ticks}", 
            ct);

        // Đồng thời BR-REFUND-07: tạo walk-in window
        walkInResult = await _refundService.CreateWalkInWindowAsync(sessionId, ct);
    }

    return new EndSessionResponseDto(
        sessionId,
        TotalAmount: ..., // existing logic
        Refund: new RefundDecisionDto(
            refundResult.Eligible,
            refundResult.Reason,
            refundResult.RefundAmount),
        WalkInWindow: walkInResult.Created ? new WalkInWindowInfoDto(
            walkInResult.WalkInWindowId!.Value,
            walkInResult.WindowStart,
            walkInResult.WindowEnd,
            walkInResult.AvailableSeats) : null);
}
```

### 5.3. Sửa `LobbyService` — thêm `CancelAfterCheckInAsync`

**File sửa:** `BoardVerse.Services/Services/LobbyService.cs`

```csharp
public async Task<CancelAfterCheckInResponse> CancelAfterCheckInAsync(
    Guid lobbyId, Guid playerUserId, 
    CancelAfterCheckInRequest request,
    CancellationToken ct = default)
{
    var lobby = await _lobbyRepo.GetByIdWithActiveSessionAsync(lobbyId, ct)
        ?? throw new NotFoundException($"Lobby {lobbyId} not found");

    // Validate: chỉ host
    if (lobby.HostId != playerUserId)
        throw new ForbiddenException("Chỉ host mới được cancel");

    // Validate: lobby phải ở state CHECKED_IN
    if (lobby.Status != LobbyStatus.CheckedIn)
        throw new ValidationException(ApiErrorMessages.Payment.CancelAfterCheckinInvalid);

    var session = lobby.ActiveSession;
    if (session == null)
        throw new ValidationException("Lobby chưa có session, dùng API cancel thường");

    session.ActualEndAt = DateTime.UtcNow;
    session.EndReason = SessionEndReason.PlayerCancelled;
    lobby.Status = LobbyStatus.CancelledByPlayer;
    lobby.UpdatedAt = DateTime.UtcNow;

    await _db.SaveChangesAsync(ct);

    // BR-REFUND-08
    var refundResult = await _refundService.ProcessLateCancelRefundAsync(
        lobbyId, playerUserId, request.CancelReason,
        request.IdempotencyKey, ct);

    return new CancelAfterCheckInResponse(
        lobby.Id, lobby.BookingId ?? Guid.Empty,
        session.Id,
        (int)(session.ActualEndAt.Value - session.StartedAt!.Value).TotalMinutes,
        (decimal)(session.ActualEndAt.Value - session.StartedAt.Value).TotalMinutes 
        / (decimal)(session.ScheduledEndTime - session.ScheduledStartTime).TotalMinutes,
        new RefundDecisionDto(
            refundResult.Eligible,
            refundResult.Reason,
            refundResult.RefundAmount),
        refundResult.Eligible ? "Released" : "Forfeited");
}
```

---

## 6. Repository Layer

### 6.1. `IWalkInWindowRepository`

**File mới:** `BoardVerse.Core/IRepositories/IWalkInWindowRepository.cs`

```csharp
namespace BoardVerse.Core.IRepositories;

public interface IWalkInWindowRepository
{
    Task<WalkInWindowEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WalkInWindowEntity>> GetAvailableAsync(
        Guid cafeId, DateOnly playDate, TimeSlot? timeSlot, 
        CancellationToken ct = default);
    Task<WalkInWindowEntity> AddAsync(WalkInWindowEntity entity, CancellationToken ct = default);
    Task UpdateAsync(WalkInWindowEntity entity, CancellationToken ct = default);
    Task<int> ExpireOverdueAsync(CancellationToken ct = default);
}
```

### 6.2. `WalkInWindowRepository`

**File mới:** `BoardVerse.Data/Repositories/WalkInWindowRepository.cs`

```csharp
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class WalkInWindowRepository : IWalkInWindowRepository
{
    private readonly BoardVerseDbContext _db;
    public WalkInWindowRepository(BoardVerseDbContext db) => _db = db;

    public Task<WalkInWindowEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.WalkInWindows
            .Include(w => w.Cafe)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

    public async Task<IReadOnlyList<WalkInWindowEntity>> GetAvailableAsync(
        Guid cafeId, DateOnly playDate, TimeSlot? timeSlot, 
        CancellationToken ct = default)
    {
        var query = _db.WalkInWindows
            .Where(w => w.CafeId == cafeId 
                     && w.PlayDate == playDate 
                     && w.Status == WalkInWindowStatus.Available
                     && w.WindowEnd > DateTime.UtcNow);

        if (timeSlot.HasValue)
            query = query.Where(w => w.TimeSlot == timeSlot.Value);

        return await query
            .OrderBy(w => w.WindowStart)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<WalkInWindowEntity> AddAsync(WalkInWindowEntity entity, CancellationToken ct = default)
    {
        _db.WalkInWindows.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public Task UpdateAsync(WalkInWindowEntity entity, CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public async Task<int> ExpireOverdueAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var overdue = await _db.WalkInWindows
            .Where(w => w.Status == WalkInWindowStatus.Available && w.WindowEnd <= now)
            .ToListAsync(ct);

        foreach (var w in overdue)
            w.Status = WalkInWindowStatus.Expired;

        await _db.SaveChangesAsync(ct);
        return overdue.Count;
    }
}
```

### 6.3. `IRefundRepository`

**File mới:** `BoardVerse.Core/IRepositories/IRefundRepository.cs`

```csharp
namespace BoardVerse.Core.IRepositories;

public interface IRefundRepository
{
    Task<RefundTransactionEntity?> GetByIdempotencyKeyAsync(
        string idempotencyKey, CancellationToken ct = default);
    Task<RefundTransactionEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<RefundTransactionEntity>> GetByUserAsync(
        Guid userId, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<RefundTransactionEntity> AddAsync(RefundTransactionEntity entity, CancellationToken ct = default);
    Task UpdateAsync(RefundTransactionEntity entity, CancellationToken ct = default);
}
```

### 6.4. `RefundRepository`

**File mới:** `BoardVerse.Data/Repositories/RefundRepository.cs`

```csharp
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class RefundRepository : IRefundRepository
{
    private readonly BoardVerseDbContext _db;
    public RefundRepository(BoardVerseDbContext db) => _db = db;

    public Task<RefundTransactionEntity?> GetByIdempotencyKeyAsync(
        string idempotencyKey, CancellationToken ct = default)
        => _db.RefundTransactions
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);

    public Task<RefundTransactionEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.RefundTransactions
            .Include(r => r.User)
            .Include(r => r.BookingDeposit)
            .Include(r => r.ActiveSession)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<RefundTransactionEntity>> GetByUserAsync(
        Guid userId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        return await _db.RefundTransactions
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<RefundTransactionEntity> AddAsync(
        RefundTransactionEntity entity, CancellationToken ct = default)
    {
        _db.RefundTransactions.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public Task UpdateAsync(RefundTransactionEntity entity, CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
```

---

## 7. Background Jobs

### 7.1. `WalkInWindowExpiryBackgroundService`

**File mới:** `BoardVerse.API/BackgroundServices/WalkInWindowExpiryBackgroundService.cs`

```csharp
using BoardVerse.Services.IServices;

namespace BoardVerse.API.BackgroundServices;

public class WalkInWindowExpiryBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WalkInWindowExpiryBackgroundService> _logger;
    private static readonly TimeSpan INTERVAL = TimeSpan.FromMinutes(5);

    public WalkInWindowExpiryBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<WalkInWindowExpiryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WalkInWindow expiry service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var refundService = scope.ServiceProvider
                    .GetRequiredService<IRefundService>();

                var expiredCount = await refundService.ExpireWalkInWindowsAsync(stoppingToken);

                if (expiredCount > 0)
                {
                    _logger.LogInformation(
                        "Expired {Count} walk-in windows", expiredCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error expiring walk-in windows");
            }

            await Task.Delay(INTERVAL, stoppingToken);
        }
    }
}
```

**Register trong `Program.cs`:**

```csharp
// Thêm vào DI registration section:
builder.Services.AddHostedService<WalkInWindowExpiryBackgroundService>();
```

---

## 8. Testing Plan

### 8.1. Unit Tests

**File mới:** `BoardVerse.Tests/Services/RefundServiceTests.cs`

Test cases:

| # | Test | Expected |
|---|---|---|
| 1 | `ProcessEarlyReleaseRefund_PlayedOver50_Returns30Percent` | Refund 30% deposit |
| 2 | `ProcessEarlyReleaseRefund_PlayedUnder50_NoRefund` | No refund, status logged |
| 3 | `ProcessEarlyReleaseRefund_ComponentLoss_NoRefund` | No refund (BR-12 exception) |
| 4 | `ProcessEarlyReleaseRefund_CafeForceClose_100Percent` | Refund 100% (BR-18) |
| 5 | `ProcessEarlyReleaseRefund_DuplicateIdempotencyKey_ReturnsExisting` | No double-refund |
| 6 | `CreateWalkInWindow_EarlyEndOver30Min_Creates` | WalkInWindow created |
| 7 | `CreateWalkInWindow_LateEndUnder30Min_NoWindow` | No window (25 min) |
| 8 | `CreateWalkInWindow_ConflictWithExisting_NoCreate` | Skip if overlapping |
| 9 | `CreateWalkInWindow_ReleasesHeldSeats` | HeldSeats -= maxPlayers |
| 10 | `ProcessLateCancelRefund_PlayedOver50_Refunds` | BR-REFUND-08: 30% |
| 11 | `ProcessLateCancelRefund_NotCheckedIn_Throws` | Use other endpoint |
| 12 | `ExpireWalkInWindows_PastWindowEnd_SetsExpired` | Status → Expired |

### 8.2. Integration Tests

**File mới:** `BoardVerse.Tests/Integration/WalkInOverrideIntegrationTests.cs`

Test cases:

| # | Test | Expected |
|---|---|---|
| 1 | POS pay session early → refund + walk-in window created | 2 entities + ledger entry |
| 2 | POS consume walk-in window → add guest slot thành công | 201 Created, AvailableSeats giảm |
| 3 | Walk-in window expire → status = Expired | Background job |
| 4 | Player cancel lobby after checkin → BR-REFUND-08 | Refund 30% |
| 5 | Race condition: 2 POS staff cùng add guest slot vào 1 window | 1 success, 1 conflict 409 |
| 6 | Inventory consistency: HeldSeats giảm đúng = maxPlayers | Audit assertion |

### 8.3. Manual Test Script

**File mới:** `docs/test-scripts/walk-in-override-manual-test.md`

Step-by-step manual test cho QA:

```
Setup:
 1. Tạo reservation cho player A (slot Sáng, 09:00-13:00, 4 players)
 2. Player A check-in lúc 09:00 → session started
 3. Verify HeldSeats = 4, HeldCopies = 1 trong inventory
 4. Đợi 30 phút (hoặc giả lập time)

Test Case 1: PAID early (≥50% slot)
 5. POS staff chọn "Pay" với EndReason = PlayerLeftEarly
 6. Verify response có:
    - refund.amount = 30% deposit
    - walkInWindow.id present
 7. Check DB:
    - RefundTransaction.Status = Completed
    - WalkInWindows row mới, Status = Available
    - BookingDeposit.RefundedAmount += 30%
    - Wallet.AvailableBalance += 30%
    - SeatInventory.HeldSeats -= 4

Test Case 2: Walk-in consume
 8. POS banner hiển thị "Có 4 chỗ walk-in [11:00-13:00]"
 9. Walk-in player tới, POS bấm "Add guest slot" → consume walk-in window
 10. Verify WalkInWindow.Status = Consumed
 11. Walk-in player chơi 1h, POS bấm "Pay"
 12. Walk-in player trả 100% tiền giờ, không có deposit

Test Case 3: Walk-in expire
 13. Walk-in window [11:00-13:00] không ai dùng
 14. Background job chạy → status = Expired
 15. POS banner không còn hiển thị

Test Case 4: Late cancel (BR-REFUND-08)
 16. Player B book slot Chiều (13:00-18:00), check-in 13:00
 17. Player B cancel trên app lúc 15:00 (played 2h/5h = 40%)
 18. Verify Response:
    - playedRatio = 0.40
    - refundDecision.eligible = false (< 50%)
    - depositStatus = Forfeited
 19. Test lại với cancel lúc 16:00 (played 3h/5h = 60%)
 20. Verify:
    - refundDecision.eligible = true
    - refundDecision.amount = 30% deposit
    - depositStatus = Released
```

---

## 9. UI Changes

### 9.1. POS Banner (Real-time)

**Component:** `WalkInAlertBanner`

**Layout:**
```
┌─────────────────────────────────────────────────────────┐
│ 🚶 WALK-IN AVAILABLE                                    │
│ Cafe: BoardVerse Thủ Đức │ Today                       │
│                                                         │
│ [11:00 - 13:00] Evening slot                            │
│ 🪑 4 chỗ trống  📦 1 game copy                         │
│ Còn lại 105 phút trước khi hết hạn                      │
│                                                         │
│ [➕ Add Walk-in Guest]  [✕ Dismiss]                    │
└─────────────────────────────────────────────────────────┘
```

**Trigger:** Auto-refresh mỗi 60 giây qua SignalR/polling

### 9.2. POS End Session Dialog

**Component:** `EndSessionDialog`

**Layout:**
```
┌─────────────────────────────────────────────────────────┐
│ End Session — Group A                                  │
├─────────────────────────────────────────────────────────┤
│ ⏱ Played: 2h / 4h (50%)                                │
│                                                         │
│ Reason for early end:                                   │
│ ○ Player left early (auto-detected)                     │
│ ○ Component loss / force close                          │
│ ○ Cafe closing                                          │
│ ○ Staff manual override                                 │
│                                                         │
│ 📊 Refund preview:                                      │
│ ✓ Eligible for 30% refund = 15,000 VND                 │
│ (Còn lại 70% = 35,000 VND cho cafe)                   │
│                                                         │
│ 🚶 Walk-in window:                                      │
│ Will release [11:00 - 13:00] for walk-in                │
│                                                         │
│ Notes (optional): [____________]                        │
│                                                         │
│ [Confirm End & Pay]                                     │
└─────────────────────────────────────────────────────────┘
```

### 9.3. Mobile App — Cancel Lobby sau check-in

**New screen:** `CancelAfterCheckInScreen`

```
┌─────────────────────────────────────────────────────────┐
│ ⚠ Cancel Lobby sau check-in                            │
├─────────────────────────────────────────────────────────┤
│ Bạn đã chơi 2h / 4h (50%)                              │
│                                                         │
│ Điều kiện hoàn cọc:                                     │
│ ✓ Chơi ≥ 50% slot                                     │
│                                                         │
│ Refund dự kiến:                                         │
│ 30% deposit = 15,000 VND                               │
│                                                         │
│ Lý do hủy: [____________]                              │
│                                                         │
│ [Confirm Cancel]                                        │
└─────────────────────────────────────────────────────────┘
```

---

## 10. Files Changed/Created Summary

### 10.1. Files mới (16 files)

#### Core layer
- `BoardVerse.Core/Entities/WalkInWindowEntity.cs`
- `BoardVerse.Core/Entities/RefundTransactionEntity.cs`
- `BoardVerse.Core/Enum/WalkInWindowStatus.cs`
- `BoardVerse.Core/Enum/SessionEndReason.cs`
- `BoardVerse.Core/Enum/RefundReason.cs`
- `BoardVerse.Core/IRepositories/IWalkInWindowRepository.cs`
- `BoardVerse.Core/IRepositories/IRefundRepository.cs`
- `BoardVerse.Core/DTOs/Pos/WalkInWindowDto.cs`
- `BoardVerse.Core/DTOs/Pos/ConsumeWalkInWindowRequest.cs`
- `BoardVerse.Core/DTOs/Lobby/CancelAfterCheckInDto.cs`

#### Data layer
- `BoardVerse.Data/Configurations/WalkInWindowConfiguration.cs`
- `BoardVerse.Data/Configurations/RefundTransactionConfiguration.cs`
- `BoardVerse.Data/Repositories/WalkInWindowRepository.cs`
- `BoardVerse.Data/Repositories/RefundRepository.cs`
- `BoardVerse.Data/Migrations/{timestamp}_AddWalkInOverrideAndSoftRelease.cs`

#### Service layer
- `BoardVerse.Services/IServices/IRefundService.cs`
- `BoardVerse.Services/Services/RefundService.cs`

#### API layer
- `BoardVerse.API/BackgroundServices/WalkInWindowExpiryBackgroundService.cs`

#### Tests
- `BoardVerse.Tests/Services/RefundServiceTests.cs`
- `BoardVerse.Tests/Integration/WalkInOverrideIntegrationTests.cs`

#### Docs
- `docs/walk-in-override-design.md` (file này)
- `docs/api/walk-in-window.md` (mới)
- `docs/test-scripts/walk-in-override-manual-test.md`

### 10.2. Files sửa (6 files)

- `BoardVerse.Core/Entities/ActiveSessionEntity.cs` (+3 fields)
- `BoardVerse.Core/Messages/ApiErrorMessages.cs` (+6 messages)
- `BoardVerse.Services/Services/CafePosService.cs` (logic EndSessionAsync)
- `BoardVerse.Services/Services/LobbyService.cs` (+method CancelAfterCheckInAsync)
- `BoardVerse.Services/Services/PaymentServiceExtensions.cs` (DI register IRefundService)
- `BoardVerse.API/Program.cs` (register background service)
- `BoardVerse.API/Controllers/CafePosController.cs` (+2 endpoints)
- `BoardVerse.API/Controllers/LobbyController.cs` (+1 endpoint)
- `.cursor/rules/lobby-booking-deposit-bvc.mdc` (+3 BR)

---

## 11. Effort Estimate

| Phase | Tasks | Effort | Notes |
|---|---|---|---|
| **Phase 1 — Core** | Entities, Migrations, Repositories, RefundService | 2 days | Foundation |
| **Phase 2 — API** | Controllers, DTOs, Error messages | 1 day | Public surface |
| **Phase 3 — UI/UX** | POS banner, end session dialog, mobile cancel screen | 1.5 days | Cross-platform |
| **Phase 4 — Tests** | Unit tests, integration tests, manual test scripts | 1 day | Coverage |
| **Phase 5 — Hardening** | Background job, race condition, audit log | 0.5 day | Production-ready |
| **Total** | | **6 days** | 1 developer |

**So với Option A (3-4 tuần):** tiết kiệm ~75% effort

---

## 12. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| **Race condition khi 2 POS cùng consume 1 walk-in window** | Critical | Optimistic concurrency với `RowVersion` column |
| **Refund double-process nếu POS retry** | High | Idempotency key bắt buộc + DB unique constraint |
| **Walk-in window quá lớn (4h)** player book slot khác | Medium | Window max = slot.endTime - slot.startTime (4h max) |
| **HeldSeats âm** nếu release mà inventory chưa update | Critical | Wrap trong transaction với SELECT FOR UPDATE |
| **POS staff không chọn đúng EndReason** | Medium | UI hint + default = PlayerLeftOnTime (safe) |
| **Mobile app chưa update** cancel-after-checkin | Low | API sẵn sàng, mobile UI update sau |
| **Player cancel sau khi guest slot đã add** | Low | Validate: lobby không có guest slot active |

---

## 13. Rollout Plan

### 13.1. Phase 1 — Internal testing (1 tuần)

- Deploy lên **testing branch Neon** (`morning-darkness`)
- Chạy manual test scripts
- Internal team test 5-10 sessions

### 13.2. Phase 2 — Beta với 1 cafe (2 tuần)

- Chọn 1 cafe đối tác thân thiết
- Theo dõi metrics:
  - Tỷ lệ walk-in window được consume (>50% = tốt)
  - Refund rate (10-20% sessions)
  - Player satisfaction (survey)

### 13.3. Phase 3 — Production rollout

- Apply migration lên **production Neon** (`morning-feather`)
- Monitor 7 ngày
- Nếu OK → keep, nếu có issue → rollback (giữ migration với `--force` flag... actually NO, FK)

**Rollback strategy:**
- Migration KHÔNG có `--force` để dễ rollback
- Code giữ feature flag (`appsettings.json → WalkInOverride:Enabled`)
- Tắt feature flag → fallback về logic cũ

### 13.4. Feature flag

**File sửa:** `BoardVerse.API/appsettings.json`

```json
{
  "WalkInOverride": {
    "Enabled": true,
    "EarlyEndGraceMinutes": 30,
    "RefundThresholdRatio": 0.5,
    "RefundPercentOfDeposit": 0.3,
    "WalkInWindowMinimumMinutes": 30
  }
}
```

**File sửa:** `BoardVerse.Core/Settings/WalkInOverrideSettings.cs` (mới)

```csharp
namespace BoardVerse.Core.Settings;

public class WalkInOverrideSettings
{
    public bool Enabled { get; set; } = true;
    public int EarlyEndGraceMinutes { get; set; } = 30;
    public decimal RefundThresholdRatio { get; set; } = 0.5m;
    public decimal RefundPercentOfDeposit { get; set; } = 0.3m;
    public int WalkInWindowMinimumMinutes { get; set; } = 30;

    public const string SectionName = "WalkInOverride";
}
```

---

## 14. Migration Plan trên Neon

### 14.1. Apply lên testing branch

```bash
# 1. Verify entities vs DB schema (MCP)
#    compare_database_schema(projectId="late-cake-24578466", 
#                            branchId="br-sparkling-salad-aota3n5d")

# 2. Generate migration
dotnet ef migrations add AddWalkInOverrideAndSoftRelease \
  --project BoardVerse.Data \
  --startup-project BoardVerse.API

# 3. Review migration file (Up + Down)
# 4. Apply to TESTING branch
dotnet ef database update --project BoardVerse.Data --startup-project BoardVerse.API
# → sử dụng appsettings.Development.json → morning-darkness
```

### 14.2. Verify trên testing

```sql
-- Query 1: Verify tables created
SELECT table_name FROM information_schema.tables 
WHERE table_name IN ('WalkInWindows', 'RefundTransactions')
ORDER BY table_name;

-- Query 2: Verify columns added to ActiveSessions
SELECT column_name, data_type FROM information_schema.columns
WHERE table_name = 'ActiveSessions' 
  AND column_name IN ('ActualEndAt', 'EndReason', 'IsPaidByPos');

-- Query 3: Verify indexes
SELECT indexname FROM pg_indexes
WHERE tablename IN ('WalkInWindows', 'RefundTransactions')
ORDER BY indexname;
```

### 14.3. Apply lên production (sau khi test pass)

```bash
# 1. Snapshot production database (Neon auto-snapshot)
# 2. Apply migration
dotnet ef database update AddWalkInOverrideAndSoftRelease \
  --project BoardVerse.Data \
  --startup-project BoardVerse.API
# → sử dụng appsettings.json → morning-feather
# 3. Monitor for 24h
# 4. Nếu có issue → manual rollback SQL
```

---

## 15. Open Questions cho anh review

| # | Question | Impact |
|---|---|---|
| 1 | **Refund 30% có hợp lý không?** Hay nên là 20%/40%/50%? | UX vs cafe revenue |
| 2 | **Walk-in window minimum 30min** có nên là 15min? | Granularity |
| 3 | **BR-REFUND-08** (player cancel sau check-in) có cần làm không? Hay MVP+1? | Scope |
| 4 | **EndReason enum** có đủ không? Cần thêm "PlayerNoShow" không? | Edge case |
| 5 | **HeldSeats release** nên tự động hay POS staff manual? | UX trade-off |
| 6 | **Walk-in window chỉ 1 session hay cho nhiều walk-in?** | Capacity |
| 7 | **Có cần notification** cho POS staff khi walk-in window available? | UX |
| 8 | **Mobile app** hiển thị walk-in window không? Hay chỉ POS? | Scope |
| 9 | **Audit log retention** bao lâu? (BR-RISK-11 → 365d) | Compliance |
| 10 | **Performance:** WalkInWindowExpiryJob chạy mỗi 5 phút có đủ không? | SLA |

---

## 16. Checklist Implement

### Pre-implementation
- [ ] **Anh review** document này và confirm direction
- [ ] **Team align** trên 10 open questions ở section 15
- [ ] **Stakeholder approval** với Cafe partners (cần thiết cho refund behavior)

### Implementation Phase 1 — Core (2 days)
- [ ] Create 5 entities + 3 enums
- [ ] Create migration + apply to testing
- [ ] Create 2 repositories + 2 services
- [ ] Wire DI trong `PaymentServiceExtensions`

### Implementation Phase 2 — API (1 day)
- [ ] Create 5 DTOs + 6 error messages
- [ ] Update `CafePosController` (+2 endpoints)
- [ ] Update `LobbyController` (+1 endpoint)
- [ ] Update `CafePosService.EndGameSessionAsync`

### Implementation Phase 3 — UI/UX (1.5 days)
- [ ] POS banner component (React/Angular)
- [ ] POS end session dialog với reason dropdown
- [ ] Mobile cancel-after-checkin screen

### Implementation Phase 4 — Tests (1 day)
- [ ] 12 unit tests cho `RefundService`
- [ ] 6 integration tests cho API
- [ ] Manual test script documentation

### Implementation Phase 5 — Hardening (0.5 day)
- [ ] Background job `WalkInWindowExpiryBackgroundService`
- [ ] Optimistic concurrency cho `WalkInWindow`
- [ ] Feature flag `WalkInOverride:Enabled`

### Post-implementation
- [ ] Verify trên **testing branch** Neon
- [ ] Beta test với 1 cafe (2 weeks)
- [ ] Production rollout (1 week monitoring)
- [ ] Update documentation

---

## 📌 Summary

**Document này định nghĩa đầy đủ:**
- ✅ 3 BR mới (BR-REFUND-06, BR-REFUND-07, BR-REFUND-08)
- ✅ 2 entities mới (WalkInWindow, RefundTransaction)
- ✅ 3 enums mới (WalkInWindowStatus, SessionEndReason, RefundReason/RefundStatus)
- ✅ 5 DTOs mới
- ✅ 4 API endpoints (3 mới, 1 sửa)
- ✅ 16 files mới, 6 files sửa
- ✅ 1 background job
- ✅ Test plan (12 unit + 6 integration)
- ✅ Migration plan cho Neon testing/production
- ✅ 6 days total effort

**Anh review cần confirm:**
1. Direction tổng thể (Option B) có OK không?
2. 3 BR mới có align với expectation không?
3. 10 open questions ở section 15
4. Effort estimate 6 days có realistic không?
5. Feature flag approach có chấp nhận được không?

**Sau khi anh duyệt → em sẽ implement Phase 1 ngay.**
