using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Phase 3 tests: Extension flow (BR-EXT) + Karma short-play entity validation.
/// </summary>
public class ReservationExtensionServiceTests : IDisposable
{
    private readonly BoardVerseDbContext _db;
    private readonly Mock<IReservationRepository> _resRepoMock;
    private readonly Mock<IWalkInWindowRepository> _windowRepoMock;
    private readonly Mock<IWalletService> _walletServiceMock;
    private readonly Mock<ILogger<ReservationExtensionService>> _loggerMock;
    private readonly ReservationExtensionService _service;

    public ReservationExtensionServiceTests()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new BoardVerseDbContext(options);

        _resRepoMock = new Mock<IReservationRepository>();
        _windowRepoMock = new Mock<IWalkInWindowRepository>();
        _walletServiceMock = new Mock<IWalletService>();
        _loggerMock = new Mock<ILogger<ReservationExtensionService>>();

        _service = new ReservationExtensionService(
            _db,
            _resRepoMock.Object,
            _windowRepoMock.Object,
            _walletServiceMock.Object,
            _loggerMock.Object);
    }

    public void Dispose() => _db.Dispose();

    #region CheckAvailabilityAsync

    [Fact]
    public async Task CheckAvailabilityAsync_Should_ReturnCanExtend_WhenValid()
    {
        // Arrange
        var reservation = CreateConfirmedReservation();
        _resRepoMock.Setup(r => r.GetByIdAsync(reservation.Id, true))
            .ReturnsAsync(reservation);
        _windowRepoMock.Setup(r => r.GetOverlappingAsync(
            reservation.CafeId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), default))
            .ReturnsAsync(new List<WalkInWindow>());

        // Act
        var result = await _service.CheckAvailabilityAsync(reservation.Id, 30);

        // Assert
        Assert.True(result.CanExtend);
        Assert.Equal(120, result.RemainingExtensionMinutes); // Max 120, no extensions yet
    }

    [Fact]
    public async Task CheckAvailabilityAsync_Should_ReturnNotFound_WhenReservationMissing()
    {
        // Arrange
        var fakeId = Guid.NewGuid();
        _resRepoMock.Setup(r => r.GetByIdAsync(fakeId, true))
            .ReturnsAsync((Reservation?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CheckAvailabilityAsync(fakeId, 30));
    }

    [Fact]
    public async Task CheckAvailabilityAsync_Should_ReturnCannotExtend_WhenStatusNotConfirmed()
    {
        // Arrange
        var reservation = CreateConfirmedReservation();
        reservation.Status = ReservationStatus.Holding;
        _resRepoMock.Setup(r => r.GetByIdAsync(reservation.Id, true))
            .ReturnsAsync(reservation);

        // Act
        var result = await _service.CheckAvailabilityAsync(reservation.Id, 30);

        // Assert
        Assert.False(result.CanExtend);
        Assert.Contains("Confirmed", result.Reason);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_Should_ReturnCannotExtend_WhenMaxExtensionsReached()
    {
        // Arrange
        var reservation = CreateConfirmedReservation();
        reservation.ExtensionCount = 2; // Max reached
        _resRepoMock.Setup(r => r.GetByIdAsync(reservation.Id, true))
            .ReturnsAsync(reservation);

        // Act
        var result = await _service.CheckAvailabilityAsync(reservation.Id, 30);

        // Assert
        Assert.False(result.CanExtend);
        Assert.Contains("tối đa 2 lần", result.Reason);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_Should_ReturnCannotExtend_WhenOverlappingWalkInWindow()
    {
        // Arrange
        var reservation = CreateConfirmedReservation();
        _resRepoMock.Setup(r => r.GetByIdAsync(reservation.Id, true))
            .ReturnsAsync(reservation);
        _windowRepoMock.Setup(r => r.GetOverlappingAsync(
            reservation.CafeId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), default))
            .ReturnsAsync(new List<WalkInWindow> { new WalkInWindow() });

        // Act
        var result = await _service.CheckAvailabilityAsync(reservation.Id, 30);

        // Assert
        Assert.False(result.CanExtend);
        Assert.Contains("WalkInWindow", result.Reason);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_Should_ReturnCannotExtend_WhenRemainingMinutesInsufficient()
    {
        // Arrange
        var reservation = CreateConfirmedReservation();
        reservation.ExtensionCount = 1; // Already used 60 min, remaining = 60
        _resRepoMock.Setup(r => r.GetByIdAsync(reservation.Id, true))
            .ReturnsAsync(reservation);
        _windowRepoMock.Setup(r => r.GetOverlappingAsync(
            reservation.CafeId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), default))
            .ReturnsAsync(new List<WalkInWindow>());

        // Act: ask for 90 min but only 60 remaining
        var result = await _service.CheckAvailabilityAsync(reservation.Id, 90);

        // Assert
        Assert.False(result.CanExtend);
        Assert.Contains("còn", result.Reason);
    }

    #endregion

    #region ExtendAsync

    [Fact]
    public async Task ExtendAsync_Should_ExtendSuccessfully_WhenValid()
    {
        // Arrange
        var reservation = CreateConfirmedReservation();
        var userId = reservation.HostId;

        _resRepoMock.Setup(r => r.GetByIdAsync(reservation.Id, true))
            .ReturnsAsync(reservation);
        _windowRepoMock.Setup(r => r.GetOverlappingAsync(
            reservation.CafeId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), default))
            .ReturnsAsync(new List<WalkInWindow>());
        _resRepoMock.Setup(r => r.UpdateAsync(It.IsAny<Reservation>()))
            .Returns(Task.CompletedTask);

        var request = new ExtendReservationRequestDto
        {
            ReservationId = reservation.Id,
            ExtensionMinutes = 30
        };

        // Act
        var result = await _service.ExtendAsync(request, userId);

        // Assert
        Assert.Equal(reservation.Id, result.ReservationId);
        Assert.Equal(1, result.ExtensionCount);
        Assert.Equal(30, result.ExtensionMinutes);
        Assert.Equal(60, result.RemainingExtensionMinutes); // 120 - (1 * 60) = 60
    }

    [Fact]
    public async Task ExtendAsync_Should_Succeed_ForLateNightOvernight_WhenExtensionStaysNextDay()
    {
        // Arrange - BR-EXT-02 LateNight: extension vượt qua playDate.Date là OK
        // vì ScheduledEndTime vốn đã là ngày hôm sau (overnight).
        var playDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            HostId = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            GameId = Guid.NewGuid(),
            PlayDate = playDate,
            TimeSlot = TimeSlot.LateNight,
            ScheduledStartTime = playDate.ToDateTime(new TimeOnly(23, 0)),
            ScheduledEndTime = playDate.AddDays(1).ToDateTime(new TimeOnly(6, 0)),
            Status = ReservationStatus.Confirmed,
            DepositAmount = 100,
            CurrentPlayers = 4,
            ExtensionCount = 0
        };
        var userId = reservation.HostId;

        _resRepoMock.Setup(r => r.GetByIdAsync(reservation.Id, true))
            .ReturnsAsync(reservation);
        _windowRepoMock.Setup(r => r.GetOverlappingAsync(
            reservation.CafeId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), default))
            .ReturnsAsync(new List<WalkInWindow>());
        _resRepoMock.Setup(r => r.UpdateAsync(It.IsAny<Reservation>()))
            .Returns(Task.CompletedTask);

        var request = new ExtendReservationRequestDto
        {
            ReservationId = reservation.Id,
            ExtensionMinutes = 30
        };

        // Act - extension sẽ push end từ 06:00 next day → 06:30 next day (vẫn next day)
        var result = await _service.ExtendAsync(request, userId);

        // Assert
        Assert.Equal(reservation.Id, result.ReservationId);
        Assert.Equal(1, result.ExtensionCount);
        Assert.Equal(30, result.ExtensionMinutes);
    }

    [Fact]
    public async Task ExtendAsync_Should_ThrowForbidden_WhenUserNotHost()
    {
        // Arrange
        var reservation = CreateConfirmedReservation();
        var nonHostId = Guid.NewGuid();

        _resRepoMock.Setup(r => r.GetByIdAsync(reservation.Id, true))
            .ReturnsAsync(reservation);

        var request = new ExtendReservationRequestDto
        {
            ReservationId = reservation.Id,
            ExtensionMinutes = 30
        };

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.ExtendAsync(request, nonHostId));
    }

    [Fact]
    public async Task ExtendAsync_Should_ThrowConflict_WhenMaxExtensionsReached()
    {
        // Arrange
        var reservation = CreateConfirmedReservation();
        reservation.ExtensionCount = 2;

        _resRepoMock.Setup(r => r.GetByIdAsync(reservation.Id, true))
            .ReturnsAsync(reservation);

        var request = new ExtendReservationRequestDto
        {
            ReservationId = reservation.Id,
            ExtensionMinutes = 30
        };

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.ExtendAsync(request, reservation.HostId));
    }

    #endregion

    #region Karma Short-Play Entity Tests (Db validation)

    [Fact]
    public async Task KarmaShortPlayRecord_Should_PersistWithAllNewFields()
    {
        // Arrange: Verify KarmaShortPlayRecord entity with new fields persists correctly
        var reservationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var record = new KarmaShortPlayRecord
        {
            Id = Guid.NewGuid(),
            ReservationId = reservationId,
            UserId = userId,
            PlayedMinutes = 60,
            ScheduledMinutes = 300,
            PlayedRatio = 0.2m,
            KarmaDelta = -5,
            KarmaPointsAdded = -5m,
            TotalKarmaScore = 95,
            Status = KarmaRecordStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        _db.KarmaShortPlayRecords.Add(record);
        await _db.SaveChangesAsync();

        // Assert
        var loaded = await _db.KarmaShortPlayRecords.FirstAsync(r => r.Id == record.Id);
        Assert.Equal(-5, loaded.KarmaDelta);
        Assert.Equal(-5m, loaded.KarmaPointsAdded);
        Assert.Equal(95, loaded.TotalKarmaScore);
        Assert.Equal(KarmaRecordStatus.Active, loaded.Status);
    }

    [Fact]
    public async Task KarmaShortPlayRecord_Should_PersistReservationNavigation()
    {
        // Arrange
        var reservationId = Guid.NewGuid();
        var reservation = CreateConfirmedReservation();
        reservation.Id = reservationId;

        var record = new KarmaShortPlayRecord
        {
            Id = Guid.NewGuid(),
            ReservationId = reservationId,
            UserId = Guid.NewGuid(),
            PlayedMinutes = 60,
            ScheduledMinutes = 300,
            PlayedRatio = 0.2m,
            KarmaDelta = -5,
            KarmaPointsAdded = -5m,
            TotalKarmaScore = 95,
            Status = KarmaRecordStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        _db.Reservations.Add(reservation);
        _db.KarmaShortPlayRecords.Add(record);
        await _db.SaveChangesAsync();

        // Assert
        var loaded = await _db.KarmaShortPlayRecords
            .Include(r => r.Reservation)
            .FirstAsync(r => r.Id == record.Id);

        Assert.NotNull(loaded.Reservation);
        Assert.Equal(reservationId, loaded.Reservation.Id);
    }

    [Fact]
    public async Task Reservation_Should_PersistExtensionCountAndExtendedEndTime()
    {
        // Arrange
        var reservation = CreateConfirmedReservation();
        reservation.ExtensionCount = 1;
        reservation.ExtendedEndTime = reservation.ScheduledEndTime.AddMinutes(60);

        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync();

        // Assert
        var loaded = await _db.Reservations.FirstAsync(r => r.Id == reservation.Id);
        Assert.Equal(1, loaded.ExtensionCount);
        Assert.NotNull(loaded.ExtendedEndTime);
        Assert.True(loaded.ExtendedEndTime > loaded.ScheduledEndTime);
    }

    #endregion

    #region Helpers

    private static Reservation CreateConfirmedReservation()
    {
        var playDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        return new Reservation
        {
            Id = Guid.NewGuid(),
            HostId = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            GameId = Guid.NewGuid(),
            PlayDate = playDate,
            TimeSlot = TimeSlot.Afternoon,
            ScheduledStartTime = playDate.ToDateTime(new TimeOnly(12, 0)),
            ScheduledEndTime = playDate.ToDateTime(new TimeOnly(17, 0)),
            Status = ReservationStatus.Confirmed,
            DepositAmount = 100,
            CurrentPlayers = 4,
            ExtensionCount = 0,
            ExtendedEndTime = null
        };
    }

    #endregion
}
