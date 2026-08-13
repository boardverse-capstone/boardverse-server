using BoardVerse.Core.DTOs.WalkIn;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Phase 2 unit tests: WalkInService + WalkInWindow OCC logic.
/// </summary>
public class WalkInServiceTests : IDisposable
{
    private readonly Mock<IWalkInWindowRepository> _windowRepo;
    private readonly Mock<IWalkInBookingRepository> _bookingRepo;
    private readonly Mock<IActiveSessionRepository> _sessionRepo;
    private readonly Mock<ICafeRepository> _cafeRepo;
    private readonly Mock<ICafeInventoryRepository> _cafeInventoryRepo;
    private readonly Mock<ILogger<WalkInService>> _logger;
    private readonly WalkInService _service;

    public WalkInServiceTests()
    {
        _windowRepo = new Mock<IWalkInWindowRepository>();
        _bookingRepo = new Mock<IWalkInBookingRepository>();
        _sessionRepo = new Mock<IActiveSessionRepository>();
        _cafeRepo = new Mock<ICafeRepository>();
        _cafeInventoryRepo = new Mock<ICafeInventoryRepository>();
        _logger = new Mock<ILogger<WalkInService>>();

        _service = new WalkInService(
            _windowRepo.Object,
            _bookingRepo.Object,
            _sessionRepo.Object,
            _cafeRepo.Object,
            _cafeInventoryRepo.Object,
            _logger.Object);
    }

    public void Dispose() { }

    // ===== GetWalkInWindowsAsync =====

    [Fact]
    public async Task GetWalkInWindowsAsync_Should_ReturnWindows()
    {
        // Arrange
        var cafeId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var windows = new List<WalkInWindow>
        {
            CreateWindow(cafeId, WalkInWindowStatus.Available),
            CreateWindow(cafeId, WalkInWindowStatus.Partial)
        };

        _windowRepo.Setup(r => r.GetActiveByCafeAsync(cafeId, date, default))
            .ReturnsAsync(windows);

        // Act
        var result = await _service.GetWalkInWindowsAsync(cafeId, date);

        // Assert
        Assert.Equal(2, result.Items.Count);
    }

    // ===== CreateWalkInBookingAsync =====

    [Fact]
    public async Task CreateWalkInBookingAsync_Should_ThrowNotFound_WhenWindowNotExist()
    {
        // Arrange
        var request = new CreateWalkInBookingRequestDto
        {
            WalkInWindowId = Guid.NewGuid(),
            GuestName = "Test Guest",
            Seats = 2
        };

        _windowRepo.Setup(r => r.GetByIdAsync(request.WalkInWindowId, default))
            .ReturnsAsync((WalkInWindow?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateWalkInBookingAsync(request, Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateWalkInBookingAsync_Should_ThrowConflict_WhenWindowFull()
    {
        // Arrange
        var windowId = Guid.NewGuid();
        var request = new CreateWalkInBookingRequestDto
        {
            WalkInWindowId = windowId,
            GuestName = "Test Guest",
            Seats = 2
        };

        var window = CreateWindow(windowId, WalkInWindowStatus.Full);
        window.AvailableSeats = 0;

        _windowRepo.Setup(r => r.GetByIdAsync(windowId, default))
            .ReturnsAsync(window);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateWalkInBookingAsync(request, Guid.NewGuid()));

        Assert.Contains("không khả dụng", ex.Message);
    }

    [Fact]
    public async Task CreateWalkInBookingAsync_Should_ThrowConflict_WhenNotEnoughSeats()
    {
        // Arrange
        var windowId = Guid.NewGuid();
        var request = new CreateWalkInBookingRequestDto
        {
            WalkInWindowId = windowId,
            GuestName = "Test Guest",
            Seats = 10
        };

        var window = CreateWindow(windowId, WalkInWindowStatus.Available);
        window.AvailableSeats = 5;

        _windowRepo.Setup(r => r.GetByIdAsync(windowId, default))
            .ReturnsAsync(window);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateWalkInBookingAsync(request, Guid.NewGuid()));

        Assert.Contains("Không đủ chỗ trống", ex.Message);
    }

    [Fact]
    public async Task CreateWalkInBookingAsync_Should_ThrowConflict_WhenOccFails()
    {
        // Arrange
        var windowId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var request = new CreateWalkInBookingRequestDto
        {
            WalkInWindowId = windowId,
            GuestName = "Test Guest",
            Seats = 2
        };

        var window = CreateWindow(windowId, WalkInWindowStatus.Available);
        window.AvailableSeats = 10;
        window.Version = 1;
        window.CafeId = cafeId;

        _windowRepo.Setup(r => r.GetByIdAsync(windowId, default))
            .ReturnsAsync(window);

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test Cafe", IsActive = true, Address = "Test Address" });

        _windowRepo.Setup(r => r.TryHoldSeatsAsync(windowId, 2, 1, default))
            .ReturnsAsync(false); // OCC fails

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateWalkInBookingAsync(request, Guid.NewGuid()));

        Assert.Contains("nhân viên khác", ex.Message);
    }

    [Fact]
    public async Task CreateWalkInBookingAsync_Should_Succeed_WhenValid()
    {
        // Arrange
        var windowId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var request = new CreateWalkInBookingRequestDto
        {
            WalkInWindowId = windowId,
            GuestName = "Test Guest",
            GuestPhone = "0912345678",
            Seats = 2
        };

        var window = CreateWindow(windowId, WalkInWindowStatus.Available);
        window.AvailableSeats = 10;
        window.Version = 1;
        window.CafeId = cafeId;

        _windowRepo.Setup(r => r.GetByIdAsync(windowId, default))
            .ReturnsAsync(window);

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test Cafe", IsActive = true, Address = "Test Address" });

        _windowRepo.Setup(r => r.TryHoldSeatsAsync(windowId, 2, 1, default))
            .ReturnsAsync(true);

        _bookingRepo.Setup(r => r.AddAsync(It.IsAny<WalkInBooking>(), default))
            .ReturnsAsync((WalkInBooking b, CancellationToken _) => b);

        // Act
        var result = await _service.CreateWalkInBookingAsync(request, Guid.NewGuid());

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test Guest", result.GuestName);
        Assert.Equal("0912345678", result.GuestPhone);
        Assert.Equal(2, result.Seats);
        Assert.Equal("Unpaid", result.PaymentStatus);
        Assert.Equal("Active", result.Status);
    }

    // ===== CleanupExpiredWindowsAsync =====

    [Fact]
    public async Task CleanupExpiredWindowsAsync_Should_CloseExpiredWindows()
    {
        // Arrange
        var window1 = CreateWindow(Guid.NewGuid(), WalkInWindowStatus.Available);
        var window2 = CreateWindow(Guid.NewGuid(), WalkInWindowStatus.Partial);

        _windowRepo.Setup(r => r.GetExpiredAsync(default))
            .ReturnsAsync(new List<WalkInWindow> { window1, window2 });

        _windowRepo.Setup(r => r.CloseAsync(It.IsAny<Guid>(), default))
            .Returns(Task.CompletedTask);

        // Act
        await _service.CleanupExpiredWindowsAsync();

        // Assert
        _windowRepo.Verify(r => r.CloseAsync(window1.Id, default), Times.Once);
        _windowRepo.Verify(r => r.CloseAsync(window2.Id, default), Times.Once);
    }

    // ===== CreateWindowFromReservationAsync =====

    [Fact]
    public async Task CreateWindowFromReservationAsync_Should_CreateWindow()
    {
        // Arrange
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            ScheduledEndTime = DateTime.UtcNow.AddHours(2)
        };

        _windowRepo.Setup(r => r.AddAsync(It.IsAny<WalkInWindow>(), default))
            .ReturnsAsync((WalkInWindow w, CancellationToken _) => w);

        // Act
        var window = await _service.CreateWindowFromReservationAsync(
            reservation, 4, DateTime.UtcNow);

        // Assert
        Assert.NotNull(window);
        Assert.Equal(reservation.Id, window.SourceReservationId);
        Assert.Equal(reservation.CafeId, window.CafeId);
        Assert.Equal(4, window.TotalSeats);
        Assert.Equal(4, window.AvailableSeats);
        Assert.Equal(0, window.HeldSeats);
        Assert.Equal(reservation.ScheduledEndTime, window.WindowEnd);
        Assert.Equal(WalkInWindowStatus.Available, window.Status);
    }

    [Fact]
    public async Task CreateWindowFromReservationAsync_Should_ReturnNull_WhenNoSeats()
    {
        // Arrange
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            CafeId = Guid.NewGuid()
        };

        // Act
        var window = await _service.CreateWindowFromReservationAsync(
            reservation, 0, DateTime.UtcNow);

        // Assert
        Assert.Null(window);
        _windowRepo.Verify(r => r.AddAsync(It.IsAny<WalkInWindow>(), default), Times.Never);
    }

    // ===== CloseWindowAsync =====

    [Fact]
    public async Task CloseWindowAsync_Should_ThrowNotFound_WhenWindowNotExist()
    {
        // Arrange
        var windowId = Guid.NewGuid();
        _windowRepo.Setup(r => r.GetByIdAsync(windowId, default))
            .ReturnsAsync((WalkInWindow?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CloseWindowAsync(windowId));
    }

    [Fact]
    public async Task CloseWindowAsync_Should_Close_WhenExists()
    {
        // Arrange
        var windowId = Guid.NewGuid();
        var window = CreateWindow(windowId, WalkInWindowStatus.Available);

        _windowRepo.Setup(r => r.GetByIdAsync(windowId, default))
            .ReturnsAsync(window);
        _windowRepo.Setup(r => r.CloseAsync(windowId, default))
            .Returns(Task.CompletedTask);

        // Act
        await _service.CloseWindowAsync(windowId, "Manual close");

        // Assert
        _windowRepo.Verify(r => r.CloseAsync(windowId, default), Times.Once);
    }

    // ===== CancelWalkInBookingAsync =====

    [Fact]
    public async Task CancelWalkInBookingAsync_Should_ThrowNotFound_WhenBookingNotExist()
    {
        // Arrange
        var bookingId = Guid.NewGuid();
        _bookingRepo.Setup(r => r.GetByIdAsync(bookingId, default))
            .ReturnsAsync((WalkInBooking?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CancelWalkInBookingAsync(bookingId));
    }

    [Fact]
    public async Task CancelWalkInBookingAsync_Should_ThrowConflict_WhenBookingAlreadyCompleted()
    {
        // BR: chỉ cho phép hủy khi Status = Active (đã check-in thành công nhưng chưa thanh toán).
        // Nếu status = Completed (đã thanh toán) → ConflictException.
        // Arrange
        var bookingId = Guid.NewGuid();
        var booking = new WalkInBooking
        {
            Id = bookingId,
            WalkInWindowId = Guid.NewGuid(),
            GuestName = "Test Guest",
            GuestPhone = "0901234567",
            Seats = 2,
            Status = WalkInBookingStatus.Completed, // đã thanh toán
            StartTime = DateTime.UtcNow
        };
        _bookingRepo.Setup(r => r.GetByIdAsync(bookingId, default))
            .ReturnsAsync(booking);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CancelWalkInBookingAsync(bookingId));
        _bookingRepo.Verify(r => r.UpdateAsync(It.IsAny<WalkInBooking>(), default), Times.Never);
    }

    [Fact]
    public async Task CancelWalkInBookingAsync_Should_ThrowConflict_WhenBookingAlreadyCancelled()
    {
        var bookingId = Guid.NewGuid();
        var booking = new WalkInBooking
        {
            Id = bookingId,
            WalkInWindowId = Guid.NewGuid(),
            GuestName = "Test Guest",
            GuestPhone = "0901234567",
            Seats = 2,
            Status = WalkInBookingStatus.Cancelled,
            StartTime = DateTime.UtcNow
        };
        _bookingRepo.Setup(r => r.GetByIdAsync(bookingId, default))
            .ReturnsAsync(booking);

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CancelWalkInBookingAsync(bookingId));
    }

    [Fact]
    public async Task CancelWalkInBookingAsync_Should_ReleaseSeatsAndUpdateStatus_WhenActive()
    {
        // BR: Active → Cancelled, trả ghế về WalkInWindow.
        var bookingId = Guid.NewGuid();
        var windowId = Guid.NewGuid();
        var booking = new WalkInBooking
        {
            Id = bookingId,
            WalkInWindowId = windowId,
            GuestName = "Test Guest",
            GuestPhone = "0901234567",
            Seats = 2,
            Status = WalkInBookingStatus.Active,
            StartTime = DateTime.UtcNow
        };
        var window = CreateWindow(windowId, WalkInWindowStatus.Available);
        window.AvailableSeats = 0; // đã bị booking giữ hết
        window.HeldSeats = 2;

        _bookingRepo.Setup(r => r.GetByIdAsync(bookingId, default))
            .ReturnsAsync(booking);
        _windowRepo.Setup(r => r.GetByIdAsync(windowId, default))
            .ReturnsAsync(window);
        _windowRepo.Setup(r => r.TryReleaseSeatsAsync(windowId, 2, window.Version, default))
            .ReturnsAsync(true);
        _bookingRepo.Setup(r => r.UpdateAsync(booking, default))
            .Returns(Task.CompletedTask);

        // Act
        await _service.CancelWalkInBookingAsync(bookingId);

        // Assert
        Assert.Equal(WalkInBookingStatus.Cancelled, booking.Status);
        _windowRepo.Verify(r => r.TryReleaseSeatsAsync(windowId, 2, window.Version, default), Times.Once);
        _bookingRepo.Verify(r => r.UpdateAsync(booking, default), Times.Once);
    }

    [Fact]
    public async Task CancelWalkInBookingAsync_Should_StillCancel_WhenWindowMissing()
    {
        // Edge case: WalkInWindow đã bị xóa (cascade) → vẫn cancel được booking,
        // nhưng không release seats (log warning).
        var bookingId = Guid.NewGuid();
        var windowId = Guid.NewGuid();
        var booking = new WalkInBooking
        {
            Id = bookingId,
            WalkInWindowId = windowId,
            GuestName = "Test Guest",
            GuestPhone = "0901234567",
            Seats = 2,
            Status = WalkInBookingStatus.Active,
            StartTime = DateTime.UtcNow
        };

        _bookingRepo.Setup(r => r.GetByIdAsync(bookingId, default))
            .ReturnsAsync(booking);
        _windowRepo.Setup(r => r.GetByIdAsync(windowId, default))
            .ReturnsAsync((WalkInWindow?)null);
        _bookingRepo.Setup(r => r.UpdateAsync(booking, default))
            .Returns(Task.CompletedTask);

        // Act
        await _service.CancelWalkInBookingAsync(bookingId);

        // Assert
        Assert.Equal(WalkInBookingStatus.Cancelled, booking.Status);
        _windowRepo.Verify(r => r.TryReleaseSeatsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<uint>(), default), Times.Never);
        _bookingRepo.Verify(r => r.UpdateAsync(booking, default), Times.Once);
    }

    [Fact]
    public async Task CancelWalkInBookingAsync_Should_StillCancel_WhenSeatReleaseConflicts()
    {
        // Edge case: OCC version conflict khi release seats → vẫn cancel booking,
        // chỉ log warning về seats không được trả.
        var bookingId = Guid.NewGuid();
        var windowId = Guid.NewGuid();
        var booking = new WalkInBooking
        {
            Id = bookingId,
            WalkInWindowId = windowId,
            GuestName = "Test Guest",
            GuestPhone = "0901234567",
            Seats = 2,
            Status = WalkInBookingStatus.Active,
            StartTime = DateTime.UtcNow
        };
        var window = CreateWindow(windowId, WalkInWindowStatus.Available);

        _bookingRepo.Setup(r => r.GetByIdAsync(bookingId, default))
            .ReturnsAsync(booking);
        _windowRepo.Setup(r => r.GetByIdAsync(windowId, default))
            .ReturnsAsync(window);
        _windowRepo.Setup(r => r.TryReleaseSeatsAsync(windowId, 2, window.Version, default))
            .ReturnsAsync(false); // OCC conflict
        _bookingRepo.Setup(r => r.UpdateAsync(booking, default))
            .Returns(Task.CompletedTask);

        // Act
        await _service.CancelWalkInBookingAsync(bookingId);

        // Assert
        Assert.Equal(WalkInBookingStatus.Cancelled, booking.Status);
        _bookingRepo.Verify(r => r.UpdateAsync(booking, default), Times.Once);
    }

    // ===== Helper =====

    private static WalkInWindow CreateWindow(Guid? id = null, WalkInWindowStatus status = WalkInWindowStatus.Available)
    {
        return new WalkInWindow
        {
            Id = id ?? Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            WindowStart = DateTime.UtcNow,
            WindowEnd = DateTime.UtcNow.AddHours(3),
            TotalSeats = 4,
            AvailableSeats = 4,
            HeldSeats = 0,
            InUseSeats = 0,
            Version = 1,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };
    }
}
