using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.Helpers;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Regression tests for BR-NEW-11 (Cafe Approval) → Karma Rating membership flow.
///
/// Bug: Khi lobby yêu cầu cafe duyệt (PendingCafeApproval), host KHÔNG được thêm làm
/// LobbyMember trong ConfirmAsync (step 18 skip branch PendingCafeApproval).
/// HandleCafeApprovalAsync trước đó cũng KHÔNG thêm host → host bị 403 khi rate.
///
/// Result: host → GET /api/v1/users/ratings/karma/lobbies/{id}
/// → 403 "Bạn không phải thành viên của phòng này nên không thể đánh giá."
/// vì RequireLobbyMemberContextAsync filter IsActive=true mà không có member record.
///
/// Fix: HandleCafeApprovalAsync nay sẽ thêm host làm LobbyMember khi approve.
/// </summary>
public class ReservationServiceCafeApprovalHostMembershipTests
{
    private readonly BoardVerseDbContext _db;
    private readonly Mock<IWalletService> _mockWalletService;
    private readonly Mock<IReservationRepository> _mockReservationRepository;
    private readonly Mock<ILobbyRepository> _mockLobbyRepository;
    private readonly Mock<IWalletRepository> _mockWalletRepository;
    private readonly Mock<ISeatInventoryRepository> _mockSeatInventoryRepository;
    private readonly Mock<IGameInventoryRepository> _mockGameInventoryRepository;
    private readonly Mock<ICafeInventoryRepository> _mockCafeInventoryRepository;
    private readonly Mock<ICafeConfigRepository> _mockCafeConfigRepository;
    private readonly Mock<ICafeRepository> _mockCafeRepository;
    private readonly Mock<IUserManagementRepository> _mockUserRepository;
    private readonly Mock<IGameTemplateRepository> _mockGameRepository;
    private readonly Mock<IOutboxRepository> _mockOutboxRepository;
    private readonly Mock<IActiveSessionRepository> _mockActiveSessionRepository;
    private readonly DepositCalculator _depositCalculator;
    private readonly Mock<IScheduleResolver> _mockScheduleResolver;
    private readonly Mock<ILogger<ReservationService>> _mockLogger;
    private readonly TimeProvider _timeProvider;
    private readonly Mock<IBookingRatingService> _mockBookingRatingService;
    private readonly Mock<IWalkInService> _mockWalkInService;
    private readonly Mock<IPlayerKarmaService> _mockKarmaService;
    private readonly Mock<ISystemConfigurationProvider> _mockConfigProvider;
    private readonly Mock<IHttpContextAccessor> _mockHttpContextAccessor;
    private readonly Mock<ISettlementService> _mockSettlementService;

    public ReservationServiceCafeApprovalHostMembershipTests()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new BoardVerseDbContext(options);

        _mockWalletService = new Mock<IWalletService>();
        _mockReservationRepository = new Mock<IReservationRepository>();
        _mockLobbyRepository = new Mock<ILobbyRepository>();
        _mockWalletRepository = new Mock<IWalletRepository>();
        _mockSeatInventoryRepository = new Mock<ISeatInventoryRepository>();
        _mockGameInventoryRepository = new Mock<IGameInventoryRepository>();
        _mockCafeInventoryRepository = new Mock<ICafeInventoryRepository>();
        _mockCafeConfigRepository = new Mock<ICafeConfigRepository>();
        _mockCafeRepository = new Mock<ICafeRepository>();
        _mockUserRepository = new Mock<IUserManagementRepository>();
        _mockGameRepository = new Mock<IGameTemplateRepository>();
        _mockOutboxRepository = new Mock<IOutboxRepository>();
        _mockActiveSessionRepository = new Mock<IActiveSessionRepository>();
        _depositCalculator = new DepositCalculator();
        _mockScheduleResolver = new Mock<IScheduleResolver>();
        _mockLogger = new Mock<ILogger<ReservationService>>();
        _timeProvider = TimeProvider.System;
        _mockBookingRatingService = new Mock<IBookingRatingService>();
        _mockWalkInService = new Mock<IWalkInService>();
        _mockKarmaService = new Mock<IPlayerKarmaService>();
        _mockConfigProvider = new Mock<ISystemConfigurationProvider>();
        _mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        _mockSettlementService = new Mock<ISettlementService>();

        // Enable demo mode for EligibilityValidator (avoids dependency on real User/Eligibility lookups)
        _mockConfigProvider.Setup(x => x.GetBoolAsync("demo_loosen_lobby_constraints", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    /// <summary>
    /// Regression: GAP fix. Khi lobby đang ở PendingCafeApproval (host chưa là member),
    /// sau khi cafe approve, host PHẢI được thêm làm LobbyMember IsActive=true.
    /// Trước fix: host bị 403 khi gọi /karma/lobbies/{id}.
    /// </summary>
    [Fact]
    public async Task HandleCafeApprovalAsync_WhenLobbyPendingCafeApproval_AddsHostAsMember()
    {
        // Arrange
        var lobbyId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        var lobby = new Lobby
        {
            Id = lobbyId,
            HostUserId = hostId,
            CafeId = cafeId,
            Status = LobbyStatus.PendingCafeApproval,
            Members = new List<LobbyMember>(), // host chưa là member — đây là bug
            CafeApprovalDeadline = DateTime.UtcNow.AddHours(24)
        };

        var reservation = new Reservation
        {
            Id = reservationId,
            HostId = hostId,
            CafeId = cafeId,
            LobbyId = lobbyId,
            Status = ReservationStatus.Holding,
            DepositAmount = 50_000,
            MaxPlayers = 4,
            GameId = Guid.NewGuid(),
            MinPlayers = 2,
            PlayDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            TimeSlot = TimeSlot.Morning,
            ScheduledStartTime = DateTime.UtcNow.AddDays(2),
            ScheduledEndTime = DateTime.UtcNow.AddDays(2).AddHours(4),
            RecruitmentDeadline = DateTime.UtcNow.AddDays(1),
            CurrentPlayers = 1,
            DepositConfigSnapshot = new DepositSnapshot(),
            RiskMultiplier = 1.0m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var cafe = new Cafe
        {
            Id = cafeId,
            ManagerId = managerId,
            Name = "Test Cafe",
            Address = "123 Test St"
        };

        // Trick: lưu entity vào in-memory DB trước, sau đó attach lại vào lobby
        // để service reference cùng object instance.
        _db.Lobbies.Add(lobby);
        _db.Reservations.Add(reservation);
        _db.Cafes.Add(cafe);
        await _db.SaveChangesAsync();

        // EF Core tracking sẽ đính instance mới sau SaveChanges.
        // Để service thấy đúng instance lobby + reservation + cafe, mock các lookup.

        // Service dùng _reservationRepository.GetByIdAsync(...) để load reservation.
        _mockReservationRepository.Setup(r => r.GetByIdAsync(reservationId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);
        _mockReservationRepository.Setup(r => r.UpdateAsync(It.IsAny<Reservation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockReservationRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Service gọi trực tiếp _db.Cafes.FirstOrDefaultAsync.
        // In-memory DB đã có cafe nên EF sẽ tìm thấy → OK.

        // Service gọi _lobbyRepository.UpdateAsync + SaveChangesAsync (nhưng lobby
        // đã tracked bởi _db — SaveChangesAsync qua repository chỉ confirm EF save).
        _mockLobbyRepository.Setup(r => r.UpdateAsync(It.IsAny<Lobby>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockLobbyRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        var request = new CafeApprovalRequestDto
        {
            ReservationId = reservationId,
            Approve = true
        };

        // Act
        var result = await service.HandleCafeApprovalAsync(managerId, request, CancellationToken.None);

        // Assert: lobby đã chuyển sang Open
        Assert.Equal(LobbyStatus.Open, lobby.Status);
        Assert.True(result.Approved);
        Assert.Equal("Open", result.LobbyStatus);

        // Assert: host đã được thêm làm LobbyMember (GAP fix)
        var hostMember = lobby.Members.FirstOrDefault(m => m.UserId == hostId);
        Assert.NotNull(hostMember);
        Assert.True(hostMember.IsHost);
        Assert.True(hostMember.IsActive);
        Assert.Equal(LobbyMemberStatus.Joined, hostMember.Status);
    }

    /// <summary>
    /// Regression: Nếu host đã là member rồi (re-approval), KHÔNG tạo thêm duplicate.
    /// </summary>
    [Fact]
    public async Task HandleCafeApprovalAsync_WhenHostAlreadyAMember_DoesNotDuplicateMember()
    {
        // Arrange
        var lobbyId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        var existingMember = new LobbyMember
        {
            Id = Guid.NewGuid(),
            LobbyId = lobbyId,
            UserId = hostId,
            IsHost = true,
            IsActive = true,
            Status = LobbyMemberStatus.Joined,
            JoinedAt = DateTime.UtcNow.AddHours(-1)
        };

        var lobby = new Lobby
        {
            Id = lobbyId,
            HostUserId = hostId,
            CafeId = cafeId,
            Status = LobbyStatus.PendingCafeApproval,
            Members = new List<LobbyMember> { existingMember },
            CafeApprovalDeadline = DateTime.UtcNow.AddHours(24)
        };

        var reservation = new Reservation
        {
            Id = reservationId,
            HostId = hostId,
            CafeId = cafeId,
            LobbyId = lobbyId,
            Status = ReservationStatus.Holding,
            DepositAmount = 50_000,
            MaxPlayers = 4,
            GameId = Guid.NewGuid(),
            MinPlayers = 2,
            PlayDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
            TimeSlot = TimeSlot.Morning,
            ScheduledStartTime = DateTime.UtcNow.AddDays(2),
            ScheduledEndTime = DateTime.UtcNow.AddDays(2).AddHours(4),
            RecruitmentDeadline = DateTime.UtcNow.AddDays(1),
            CurrentPlayers = 1,
            DepositConfigSnapshot = new DepositSnapshot(),
            RiskMultiplier = 1.0m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var cafe = new Cafe
        {
            Id = cafeId,
            ManagerId = managerId,
            Name = "Test Cafe",
            Address = "123 Test St"
        };

        _db.Lobbies.Add(lobby);
        _db.Reservations.Add(reservation);
        _db.Cafes.Add(cafe);
        await _db.SaveChangesAsync();

        _mockReservationRepository.Setup(r => r.GetByIdAsync(reservationId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);
        _mockReservationRepository.Setup(r => r.UpdateAsync(It.IsAny<Reservation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockReservationRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockLobbyRepository.Setup(r => r.UpdateAsync(It.IsAny<Lobby>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockLobbyRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        var request = new CafeApprovalRequestDto
        {
            ReservationId = reservationId,
            Approve = true
        };

        // Act
        var result = await service.HandleCafeApprovalAsync(managerId, request, CancellationToken.None);

        // Assert
        Assert.Equal(LobbyStatus.Open, lobby.Status);
        Assert.True(result.Approved);

        // Assert: chỉ có DUY NHẤT 1 host member record (không duplicate)
        var hostMembers = lobby.Members.Where(m => m.UserId == hostId).ToList();
        Assert.Single(hostMembers);
    }

    // ===== Helpers =====

    private ReservationService CreateService()
    {
        var realEligibility = new EligibilityValidator();
        var realRefundCalc = new RefundCalculationService();

        return new ReservationService(
            _db,
            _mockWalletService.Object,
            _mockWalletRepository.Object,
            _mockReservationRepository.Object,
            _mockLobbyRepository.Object,
            _mockSeatInventoryRepository.Object,
            _mockGameInventoryRepository.Object,
            _mockCafeInventoryRepository.Object,
            _mockCafeConfigRepository.Object,
            _mockCafeRepository.Object,
            _mockUserRepository.Object,
            _mockGameRepository.Object,
            _mockOutboxRepository.Object,
            _mockActiveSessionRepository.Object,
            _depositCalculator,
            realEligibility,
            _mockScheduleResolver.Object,
            _mockLogger.Object,
            _timeProvider,
            _mockBookingRatingService.Object,
            realRefundCalc,
            _mockWalkInService.Object,
            _mockKarmaService.Object,
            _mockConfigProvider.Object,
            _mockHttpContextAccessor.Object,
            _mockSettlementService.Object
        );
    }
}
