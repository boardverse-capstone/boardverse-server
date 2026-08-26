# Tests — Cafe Booking / Availability / Reservation Overlap

Bộ test mới cover 3 gaps phát hiện trong fix `CafeBookingService.GetAvailabilityAsync` + `CafeRepository.GetAvailableSeatsByTimeSlotAsync` (ngày 2026-08-18).

## Tổng quan

| File test | Số test | Gap cover | Loại |
|---|---|---|---|
| `CafeBookingServiceTests.cs` | 10 | **G4** — `GetAvailabilityAsync` trừ Reservation + WalkIn | Service unit (Moq) |
| `ReservationRepositoryOverlapTests.cs` | 8 | **G2** — `GetOverlappingReservationsAsync` logic | Repository unit (FakeDbContext) |
| `CafeRepositoryAvailableSeatsTests.cs` | 7 | **G3** — `GetAvailableSeatsByTimeSlotAsync` fallback khi SeatInventory null | Repository unit (FakeDbContext) |
| **Tổng** | **25** | | |

**Build:** `0 errors`. **Test run:** `25 passed / 0 failed` (Duration: ~13s).

---

## 1. `CafeBookingServiceTests.cs`

Test service layer cho `CafeBookingController.GetAvailability` — verify công thức capacity trừ đúng 4 nguồn (Booking + ActiveSession + Reservation + WalkIn).

### Setup

- Moq `ICafeRepository`, `ICafeTableRepository`, `IBookingRepository`, `IActiveSessionRepository`, `ICafePosRepository`, `IReservationRepository`, `IWalkInWindowRepository`.
- Default MockBehavior.Loose + setup default `ReturnsAsync(new List<T>())` cho các call chưa match → tránh `Sum()` crash vì null IEnumerable.

### Test cases

| # | Test | Mục đích |
|---|---|---|
| 1 | `GetAvailabilityAsync_Should_IncludeReservationHeldSeats_WhenHolding` | Reservation `Holding` trừ vào capacity |
| 2 | `GetAvailabilityAsync_Should_IncludeReservationHeldSeats_WhenConfirmed` | Reservation `Confirmed` trừ vào capacity |
| 3 | `GetAvailabilityAsync_Should_IncludeWalkInWindowHeldSeats` | WalkInWindow `Available` trừ `HeldSeats` |
| 4 | `GetAvailabilityAsync_Should_CombineAllFlowsCorrectly` | Cộng dồn 4 nguồn: Booking + Session + Reservation + WalkIn |
| 5 | `GetAvailabilityAsync_Should_IgnoreCancelledReservation` | Reservation `CancelledByPlayer` / `Expired` KHÔNG trừ |
| 6 | `GetAvailabilityAsync_Should_IgnoreClosedWalkInWindow` | WalkInWindow `Closed` KHÔNG trừ |
| 7 | `GetAvailabilityAsync_Should_NotReturnNegative_WhenFullyBooked` | Edge case held > total → Math.Max(0,...) |
| 8 | `GetAvailabilityAsync_Should_FindAlternativeSlots_ExcludingReservation` | Alt-slot loop cũng trừ Reservation |
| 9 | `GetAvailabilityAsync_Should_ThrowBadRequest_WhenEndBeforeStart` | Validation `endTime <= startTime` |
| 10 | `GetAvailabilityAsync_Should_ThrowNotFound_WhenCafeMissing` | Cafe không tồn tại |

### Công thức test verify

```
AvailableSeats = max(0,
    TotalSeats
    - sum(Booking.PlayerQuantity overlap)
    - sum(ActiveSession.SeatCount của table gán)
    - sum(Reservation.MaxPlayers status ∈ {Holding, Confirmed, AwaitingDeposit})
    - sum(WalkInWindow.HeldSeats status ∈ {Available, Full})
)
```

---

## 2. `ReservationRepositoryOverlapTests.cs`

Test repository `ReservationRepository.GetOverlappingReservationsAsync` (mới thêm ngày 2026-08-18).

### Setup

- `FakeDbContext` (auto Postgres nếu có `DATABASE_URL`, ngược lại InMemory).
- Seed: `User` (manager) → `Cafe` (FK ManagerId) → `GameTemplate` → `User` (host) → `Reservation` (FK HostId, CafeId, GameId, IdempotencyKey unique).

### Test cases

| # | Test | Mục đích |
|---|---|---|
| 1 | `GetOverlappingReservationsAsync_Should_ReturnReservationsInRange` | Happy path |
| 2 | `GetOverlappingReservationsAsync_Should_IncludeAllStatuses` | Repository trả về mọi status, caller tự filter |
| 3 | `GetOverlappingReservationsAsync_Should_ExcludeNonOverlappingReservation` | Reservation kết thúc trước / bắt đầu sau query range |
| 4 | `GetOverlappingReservationsAsync_Should_FilterByCafe` | Reservation ở cafe khác bị loại |
| 5 | `GetOverlappingReservationsAsync_Should_HandleEdgeCase_StartAtQueryEnd` | Edge: `ScheduledStartTime == queryEnd` → KHÔNG overlap (strict `<`) |
| 6 | `GetOverlappingReservationsAsync_Should_HandleEdgeCase_EndAtQueryStart` | Edge: `ScheduledEndTime == queryStart` → KHÔNG overlap (strict `>`) |
| 7 | `GetOverlappingReservationsAsync_Should_DetectPartialOverlap` | Reservation giao nhau 1 phần với query range |
| 8 | `GetOverlappingReservationsAsync_Should_ReturnEmpty_WhenNoReservations` | Empty result |

### Overlap query semantics

```sql
SELECT * FROM Reservations
WHERE CafeId = @cafeId
  AND ScheduledStartTime < @endTime
  AND ScheduledEndTime > @startTime
```

So sánh strict (`<` và `>`) — không tính biên. Reservation chạm đúng biên `endTime` hoặc `startTime` không overlap.

---

## 3. `CafeRepositoryAvailableSeatsTests.cs`

Test `CafeRepository.GetAvailableSeatsByTimeSlotAsync` — đặc biệt là **fallback path** khi `SeatInventory` row không tồn tại.

### Setup

- `FakeDbContext` + seed `User` (manager) + `Cafe` + `GameTemplate` + `User` (host) cho Reservation FK.

### Test cases

| # | Test | Mục đích |
|---|---|---|
| 1 | `GetAvailableSeatsByTimeSlotAsync_Should_ReturnSeatInventoryValues_WhenExists` | Happy path: dùng `SeatInventory.AvailableSeats` computed |
| 2 | `GetAvailableSeatsByTimeSlotAsync_Should_SubtractReservationHeld_WhenInventoryMissing` | **Gap G3 fix**: fallback trừ Reservation held |
| 3 | `GetAvailableSeatsByTimeSlotAsync_Should_IgnoreCancelledReservation_WhenInventoryMissing` | Fallback không trừ Cancelled/Expired |
| 4 | `GetAvailableSeatsByTimeSlotAsync_Should_OnlySubtractSameTimeSlot_WhenInventoryMissing` | Fallback scope theo `TimeSlot` |
| 5 | `GetAvailableSeatsByTimeSlotAsync_Should_ReturnZero_WhenHeldExceedsTotal` | Edge case held > total → Math.Max(0,...) |
| 6 | `GetAvailableSeatsByTimeSlotAsync_Should_SubtractReservationForSpecificSlot_WhenInventoryExistsForAnother` | Mixed: slot A có inventory, slot B fallback |
| 7 | `GetAvailableSeatsByTimeSlotAsync_Should_ReturnAllFourSlots` | Trả đủ 4 enum `TimeSlot` (morning/afternoon/evening/lateNight) |

### Helper methods mới (private)

| Method | Vai trò |
|---|---|
| `CountHeldSeatsForSlotAsync(cafeId, playDate, timeSlot)` | Đếm Reservation `MaxPlayers` cho (cafe, date, slot) với status `Holding/Confirmed/AwaitingDeposit` |
| `CountInUseSeatsForSlotAsync(cafeId, playDate, timeSlot)` | Đếm `ActiveSessionMember` đang `Playing` cho (cafe, date) — `ActiveSession` chưa có field `TimeSlot` nên chỉ filter theo day |

---

## Lệnh chạy

```bash
# Chỉ chạy 3 file test mới
dotnet test BoardVerse.Tests --no-build \
  --filter "FullyQualifiedName~CafeBookingServiceTests|\
FullyQualifiedName~ReservationRepositoryOverlapTests|\
FullyQualifiedName~CafeRepositoryAvailableSeatsTests"

# Kèm build
dotnet test BoardVerse.Tests \
  --filter "FullyQualifiedName~CafeBookingServiceTests|\
FullyQualifiedName~ReservationRepositoryOverlapTests|\
FullyQualifiedName~CafeRepositoryAvailableSeatsTests"
```

## Lệnh chạy regression (verify không phá test cũ)

```bash
dotnet test BoardVerse.Tests --no-build \
  --filter "FullyQualifiedName~Reservation|\
FullyQualifiedName~WalkIn|\
FullyQualifiedName~Lobby|\
FullyQualifiedName~Concurr|\
FullyQualifiedName~CafeBooking|\
FullyQualifiedName~CafeRepository|\
FullyQualifiedName~CafeAvailability|\
FullyQualifiedName~SeatInventory|\
FullyQualifiedName~ActiveSession|\
FullyQualifiedName~EligibilityValidator|\
FullyQualifiedName~DissolveLobby|\
FullyQualifiedName~DepositCalculator|\
FullyQualifiedName~BugFix"
```

Lệnh này chạy **468 tests** (443 cũ + 25 mới) trong ~1m53s.

---

## Lịch sử thay đổi

| Ngày | Mô tả |
|---|---|
| 2026-08-18 | Tạo 3 file test cover Gap G2 (Reservation overlap), G3 (SeatInventory fallback), G4 (CafeBookingService trừ Reservation + WalkIn). Tổng 25 test, build clean, tất cả pass. |