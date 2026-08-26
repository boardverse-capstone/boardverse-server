using BoardVerse.Core.DTOs.Booking;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Core.Exceptions;
using BoardVerse.Services.Services;
using Moq;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho CafeBookingService — Gap G4 (GetAvailabilityAsync trừ Reservation + WalkIn held seats).
/// BR §17.3 — capacity phải phản ánh đúng tất cả flow giữ ghế:
///   - Flow B: Booking overlap
///   - Flow B: ActiveSession đang chơi
///   - Flow A: Reservation Holding/Confirmed/AwaitingDeposit
///   - Flow B: WalkInWindow.Available/Full
/// </summary>
public class CafeBookingServiceTests
{
    private readonly Mock<ICafeRepository> _cafeRepo;
    private readonly Mock<ICafeTableRepository> _tableRepo;
    private readonly Mock<IBookingRepository> _bookingRepo;
    private readonly Mock<IActiveSessionRepository> _sessionRepo;
    private readonly Mock<ICafePosRepository> _posRepo;
    private readonly Mock<IReservationRepository> _reservationRepo;
    private readonly Mock<IWalkInWindowRepository> _walkInRepo;
    private readonly CafeBookingService _service;

    public CafeBookingServiceTests()
    {
        _cafeRepo = new Mock<ICafeRepository>();
        _tableRepo = new Mock<ICafeTableRepository>();
        _bookingRepo = new Mock<IBookingRepository>();
        _sessionRepo = new Mock<IActiveSessionRepository>();
        _posRepo = new Mock<ICafePosRepository>();
        _reservationRepo = new Mock<IReservationRepository>();
        _walkInRepo = new Mock<IWalkInWindowRepository>();

        // Default MockBehavior.Loose → trả default value cho các call chưa setup
        // (null cho reference, 0 cho int). Đảm bảo Sum() không crash vì null IEnumerable.
        _bookingRepo.Setup(r => r.GetOverlappingBookingsAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Booking>());
        _sessionRepo.Setup(r => r.GetActiveSessionsInRangeAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveSession>());
        _reservationRepo.Setup(r => r.GetOverlappingReservationsAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Reservation>());
        _walkInRepo.Setup(r => r.GetOverlappingAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WalkInWindow>());

        _service = new CafeBookingService(
            _cafeRepo.Object,
            _tableRepo.Object,
            _bookingRepo.Object,
            _sessionRepo.Object,
            _posRepo.Object,
            _reservationRepo.Object,
            _walkInRepo.Object);
    }

    private static Cafe CreateCafe(Guid cafeId, int totalSeats = 30)
    {
        return new Cafe
        {
            Id = cafeId,
            Name = "Test Cafe",
            Address = "123 Test Street",
            TotalSeats = totalSeats,
            IsActive = true,
            PartnerOperationalStatus = CafePartnerOperationalStatus.Active,
            BasePrice = 50000m
        };
    }

    private static CafeTable CreateTable(Guid cafeId, int seatCount, CafeTableStatus status = CafeTableStatus.Available)
    {
        return new CafeTable
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            Name = $"Table {seatCount}",
            SeatCount = seatCount,
            IsActive = true,
            Status = status,
            SortOrder = 1
        };
    }

    private static Booking CreateBooking(Guid cafeId, int playerQuantity, DateTime start, DateTime end)
    {
        return new Booking
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            CafeTableId = Guid.NewGuid(),
            PlayerQuantity = playerQuantity,
            ScheduledStartTime = start,
            ScheduleEndTime = end,
            Status = BookingStatus.Confirmed
        };
    }

    private static ActiveSession CreateActiveSession(Guid cafeId, Guid? tableId, GroupSessionStatus status = GroupSessionStatus.Active)
    {
        return new ActiveSession
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            CafeTableId = tableId,
            Status = status,
            StartedAt = DateTime.UtcNow.AddHours(-1),
            EndedAt = null,
            Members = new List<ActiveSessionMember>()
        };
    }

    private static Reservation CreateReservation(
        Guid cafeId, int maxPlayers, DateTime start, DateTime end, ReservationStatus status)
    {
        return new Reservation
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            HostId = Guid.NewGuid(),
            GameId = Guid.NewGuid(),
            MaxPlayers = maxPlayers,
            ScheduledStartTime = start,
            ScheduledEndTime = end,
            Status = status,
            PlayDate = DateOnly.FromDateTime(start),
            TimeSlot = TimeSlot.Afternoon
        };
    }

    private static WalkInWindow CreateWalkInWindow(
        Guid cafeId, int heldSeats, DateTime start, DateTime end, WalkInWindowStatus status)
    {
        return new WalkInWindow
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            WindowStart = start,
            WindowEnd = end,
            TotalSeats = 10,
            AvailableSeats = 10 - heldSeats,
            HeldSeats = heldSeats,
            InUseSeats = 0,
            Status = status
        };
    }

    // ============================================================================
    // GetAvailabilityAsync — Gap G4
    // ============================================================================

    [Fact]
    public async Task GetAvailabilityAsync_Should_IncludeReservationHeldSeats_WhenHolding()
    {
        // BR §17.3: Reservation Holding phải trừ vào available capacity.
        // Gap G4 fix: CafeBookingService cũ BỎ QUÊN Reservation → luôn trả capacity sai.
        var cafeId = Guid.NewGuid();
        var start = new DateTime(2026, 8, 20, 13, 0, 0);
        var end = new DateTime(2026, 8, 20, 15, 0, 0);

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCafe(cafeId, totalSeats: 20));
        _tableRepo.Setup(r => r.GetByCafeIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeTable> { CreateTable(cafeId, 20) });
        _bookingRepo.Setup(r => r.GetOverlappingBookingsAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Booking>());
        _sessionRepo.Setup(r => r.GetActiveSessionsInRangeAsync(cafeId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveSession>());
        _reservationRepo.Setup(r => r.GetOverlappingReservationsAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Reservation>
            {
                CreateReservation(cafeId, 4, start, end, ReservationStatus.Holding)
            });
        _walkInRepo.Setup(r => r.GetOverlappingAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WalkInWindow>());

        // Act
        var result = await _service.GetAvailabilityAsync(cafeId, start, end, 1, null);

        // Assert: 20 - 0 - 0 - 4 - 0 = 16
        Assert.Equal(16, result.AvailableSeats);
        Assert.True(result.HasCapacity); // capacity for 1 person
    }

    [Fact]
    public async Task GetAvailabilityAsync_Should_IncludeReservationHeldSeats_WhenConfirmed()
    {
        var cafeId = Guid.NewGuid();
        var start = new DateTime(2026, 8, 20, 13, 0, 0);
        var end = new DateTime(2026, 8, 20, 15, 0, 0);

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCafe(cafeId, totalSeats: 20));
        _tableRepo.Setup(r => r.GetByCafeIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeTable> { CreateTable(cafeId, 20) });
        _bookingRepo.Setup(r => r.GetOverlappingBookingsAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Booking>());
        _sessionRepo.Setup(r => r.GetActiveSessionsInRangeAsync(cafeId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveSession>());
        _reservationRepo.Setup(r => r.GetOverlappingReservationsAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Reservation>
            {
                CreateReservation(cafeId, 6, start, end, ReservationStatus.Confirmed)
            });
        _walkInRepo.Setup(r => r.GetOverlappingAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WalkInWindow>());

        var result = await _service.GetAvailabilityAsync(cafeId, start, end, 1, null);

        Assert.Equal(14, result.AvailableSeats);
    }

    [Fact]
    public async Task GetAvailabilityAsync_Should_IncludeWalkInWindowHeldSeats()
    {
        // BR §17.3: WalkInWindow cũng giữ ghế, phải trừ vào capacity.
        var cafeId = Guid.NewGuid();
        var start = new DateTime(2026, 8, 20, 13, 0, 0);
        var end = new DateTime(2026, 8, 20, 15, 0, 0);

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCafe(cafeId, totalSeats: 20));
        _tableRepo.Setup(r => r.GetByCafeIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeTable> { CreateTable(cafeId, 20) });
        _bookingRepo.Setup(r => r.GetOverlappingBookingsAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Booking>());
        _sessionRepo.Setup(r => r.GetActiveSessionsInRangeAsync(cafeId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveSession>());
        _reservationRepo.Setup(r => r.GetOverlappingReservationsAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Reservation>());
        _walkInRepo.Setup(r => r.GetOverlappingAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WalkInWindow>
            {
                CreateWalkInWindow(cafeId, heldSeats: 3, start: start, end: end, status: WalkInWindowStatus.Available)
            });

        var result = await _service.GetAvailabilityAsync(cafeId, start, end, 1, null);

        Assert.Equal(17, result.AvailableSeats);
    }

    [Fact]
    public async Task GetAvailabilityAsync_Should_CombineAllFlowsCorrectly()
    {
        // All flows together: Booking + Session + Reservation + WalkIn
        var cafeId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var start = new DateTime(2026, 8, 20, 13, 0, 0);
        var end = new DateTime(2026, 8, 20, 15, 0, 0);

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCafe(cafeId, totalSeats: 30));
        var table = CreateTable(cafeId, 10);
        table.Id = tableId;
        _tableRepo.Setup(r => r.GetByCafeIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeTable> { table, CreateTable(cafeId, 20) });
        // Booking overlap = 4, Session inUse = 10 (1 table fully occupied)
        _bookingRepo.Setup(r => r.GetOverlappingBookingsAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Booking>
            {
                CreateBooking(cafeId, 4, start, end)
            });
        _sessionRepo.Setup(r => r.GetActiveSessionsInRangeAsync(cafeId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveSession>
            {
                new ActiveSession
                {
                    Id = Guid.NewGuid(),
                    CafeId = cafeId,
                    CafeTableId = tableId,
                    Status = GroupSessionStatus.Active,
                    StartedAt = start.AddMinutes(-30),
                    EndedAt = null,
                    Members = new List<ActiveSessionMember>()
                }
            });
        // Reservation Held = 5
        _reservationRepo.Setup(r => r.GetOverlappingReservationsAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Reservation>
            {
                CreateReservation(cafeId, 5, start, end, ReservationStatus.Holding)
            });
        // WalkIn Held = 2
        _walkInRepo.Setup(r => r.GetOverlappingAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WalkInWindow>
            {
                CreateWalkInWindow(cafeId, heldSeats: 2, start: start, end: end, status: WalkInWindowStatus.Full)
            });

        var result = await _service.GetAvailabilityAsync(cafeId, start, end, 1, null);

        // Total = 30 (10 + 20)
        // Booked = 4, Session = 10, Reservation = 5, WalkIn = 2
        // Available = 30 - 4 - 10 - 5 - 2 = 9
        Assert.Equal(30, result.TotalSeats);
        Assert.Equal(9, result.AvailableSeats);
        Assert.True(result.HasCapacity);
    }

    [Fact]
    public async Task GetAvailabilityAsync_Should_IgnoreCancelledReservation()
    {
        // Reservation bị Cancelled/Expired → không còn giữ ghế → KHÔNG trừ capacity.
        var cafeId = Guid.NewGuid();
        var start = new DateTime(2026, 8, 20, 13, 0, 0);
        var end = new DateTime(2026, 8, 20, 15, 0, 0);

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCafe(cafeId, totalSeats: 20));
        _tableRepo.Setup(r => r.GetByCafeIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeTable> { CreateTable(cafeId, 20) });
        _bookingRepo.Setup(r => r.GetOverlappingBookingsAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Booking>());
        _sessionRepo.Setup(r => r.GetActiveSessionsInRangeAsync(cafeId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveSession>());
        _reservationRepo.Setup(r => r.GetOverlappingReservationsAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Reservation>
            {
                CreateReservation(cafeId, 5, start, end, ReservationStatus.CancelledByPlayer),
                CreateReservation(cafeId, 3, start, end, ReservationStatus.Expired),
                CreateReservation(cafeId, 2, start, end, ReservationStatus.Holding) // active
            });
        _walkInRepo.Setup(r => r.GetOverlappingAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WalkInWindow>());

        var result = await _service.GetAvailabilityAsync(cafeId, start, end, 1, null);

        // Chỉ trừ 2 (Holding). Cancelled/Expired bị ignore.
        Assert.Equal(18, result.AvailableSeats);
    }

    [Fact]
    public async Task GetAvailabilityAsync_Should_IgnoreClosedWalkInWindow()
    {
        // WalkInWindow đã Closed → HeldSeats không còn giữ ghế.
        var cafeId = Guid.NewGuid();
        var start = new DateTime(2026, 8, 20, 13, 0, 0);
        var end = new DateTime(2026, 8, 20, 15, 0, 0);

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCafe(cafeId, totalSeats: 20));
        _tableRepo.Setup(r => r.GetByCafeIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeTable> { CreateTable(cafeId, 20) });
        _bookingRepo.Setup(r => r.GetOverlappingBookingsAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Booking>());
        _sessionRepo.Setup(r => r.GetActiveSessionsInRangeAsync(cafeId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveSession>());
        _reservationRepo.Setup(r => r.GetOverlappingReservationsAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Reservation>());
        _walkInRepo.Setup(r => r.GetOverlappingAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WalkInWindow>
            {
                CreateWalkInWindow(cafeId, heldSeats: 4, start: start, end: end, status: WalkInWindowStatus.Closed)
            });

        var result = await _service.GetAvailabilityAsync(cafeId, start, end, 1, null);

        Assert.Equal(20, result.AvailableSeats); // Closed window không trừ
    }

    [Fact]
    public async Task GetAvailabilityAsync_Should_NotReturnNegative_WhenFullyBooked()
    {
        // Edge case: total < occupied → Math.Max(0, ...) bảo vệ UI khỏi hiển thị âm.
        var cafeId = Guid.NewGuid();
        var start = new DateTime(2026, 8, 20, 13, 0, 0);
        var end = new DateTime(2026, 8, 20, 15, 0, 0);

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCafe(cafeId, totalSeats: 5));
        _tableRepo.Setup(r => r.GetByCafeIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeTable> { CreateTable(cafeId, 5) });
        _bookingRepo.Setup(r => r.GetOverlappingBookingsAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Booking>());
        _sessionRepo.Setup(r => r.GetActiveSessionsInRangeAsync(cafeId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveSession>());
        _reservationRepo.Setup(r => r.GetOverlappingReservationsAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Reservation>
            {
                CreateReservation(cafeId, 10, start, end, ReservationStatus.Holding)
            });
        _walkInRepo.Setup(r => r.GetOverlappingAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WalkInWindow>());

        var result = await _service.GetAvailabilityAsync(cafeId, start, end, 1, null);

        Assert.Equal(0, result.AvailableSeats);
        Assert.False(result.HasCapacity);
    }

    [Fact]
    public async Task GetAvailabilityAsync_Should_FindAlternativeSlots_ExcludingReservation()
    {
        // Alt-slot loop cũng phải query Reservation overlap.
        // Tại t=+30 phút: 1 reservation overlap → capacity giảm.
        var cafeId = Guid.NewGuid();
        var start = new DateTime(2026, 8, 20, 13, 0, 0);
        var end = new DateTime(2026, 8, 20, 15, 0, 0);
        var altStart = start.AddMinutes(30);
        var altEnd = altStart.Add(end - start);

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCafe(cafeId, totalSeats: 10));
        _tableRepo.Setup(r => r.GetByCafeIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeTable> { CreateTable(cafeId, 10) });

        // Main slot: trống
        _bookingRepo.Setup(r => r.GetOverlappingBookingsAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Booking>());
        _reservationRepo.Setup(r => r.GetOverlappingReservationsAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Reservation>());
        _walkInRepo.Setup(r => r.GetOverlappingAsync(cafeId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WalkInWindow>());

        // Alt slot +30p: có 1 reservation holding 4 ghế
        _bookingRepo.Setup(r => r.GetOverlappingBookingsAsync(cafeId, altStart, altEnd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Booking>());
        _reservationRepo.Setup(r => r.GetOverlappingReservationsAsync(cafeId, altStart, altEnd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Reservation>
            {
                CreateReservation(cafeId, 4, altStart, altEnd, ReservationStatus.Holding)
            });
        _walkInRepo.Setup(r => r.GetOverlappingAsync(cafeId, altStart, altEnd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WalkInWindow>());

        _sessionRepo.Setup(r => r.GetActiveSessionsInRangeAsync(cafeId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveSession>());

        var result = await _service.GetAvailabilityAsync(cafeId, start, end, 3, null);

        Assert.True(result.HasCapacity);
        Assert.NotEmpty(result.AlternativeSlots);

        // Alt slot phải có AvailableSeats = 10 - 0 - 0 - 4 - 0 = 6
        var firstAlt = result.AlternativeSlots.First();
        Assert.Equal(altStart, firstAlt.StartTime);
        Assert.Equal(6, firstAlt.AvailableSeats);
    }

    [Fact]
    public async Task GetAvailabilityAsync_Should_ThrowBadRequest_WhenEndBeforeStart()
    {
        var cafeId = Guid.NewGuid();
        var start = new DateTime(2026, 8, 20, 15, 0, 0);
        var end = new DateTime(2026, 8, 20, 13, 0, 0);

        await Assert.ThrowsAsync<BadRequestException>(async () =>
            await _service.GetAvailabilityAsync(cafeId, start, end, 1, null));
    }

    [Fact]
    public async Task GetAvailabilityAsync_Should_ThrowNotFound_WhenCafeMissing()
    {
        var cafeId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cafe?)null);

        await Assert.ThrowsAsync<NotFoundException>(async () =>
            await _service.GetAvailabilityAsync(cafeId, DateTime.UtcNow, DateTime.UtcNow.AddHours(2), 1, null));
    }
}