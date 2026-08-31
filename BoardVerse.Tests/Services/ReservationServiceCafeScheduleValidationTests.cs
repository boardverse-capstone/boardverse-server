using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
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
/// Unit tests cho ReservationService.ValidatePreferredTimesWithCafeScheduleAsync
/// với focus vào overnight sessions + CafeScheduleOverride validation.
///
/// Test coverage:
/// - Same-day session (preferredEnd > preferredStart)
/// - Overnight session (preferredEnd < preferredStart)
/// - Edge cases: next day closed, next day early close, boundary times
/// </summary>
public class ReservationServiceCafeScheduleValidationTests
{
    private readonly Mock<BoardVerseDbContext> _mockDb;
    private readonly Mock<IWalletService> _mockWalletService;
    private readonly Mock<IWalletRepository> _mockWalletRepository;
    private readonly Mock<IReservationRepository> _mockReservationRepository;
    private readonly Mock<ILobbyRepository> _mockLobbyRepository;
    private readonly Mock<ISeatInventoryRepository> _mockSeatInventoryRepository;
    private readonly Mock<IGameInventoryRepository> _mockGameInventoryRepository;
    private readonly Mock<ICafeInventoryRepository> _mockCafeInventoryRepository;
    private readonly Mock<ICafeConfigRepository> _mockCafeConfigRepository;
    private readonly Mock<ICafeRepository> _mockCafeRepository;
    private readonly Mock<IUserManagementRepository> _mockUserRepository;
    private readonly Mock<IGameTemplateRepository> _mockGameRepository;
    private readonly Mock<IOutboxRepository> _mockOutboxRepository;
    private readonly Mock<IActiveSessionRepository> _mockActiveSessionRepository;
    private readonly DepositCalculator _depositCalculator; // Real instance
    private readonly Mock<EligibilityValidator> _mockEligibilityValidator;
    private readonly Mock<IScheduleResolver> _mockScheduleResolver;
    private readonly Mock<ILogger<ReservationService>> _mockLogger;
    private readonly TimeProvider _timeProvider;
    private readonly Mock<IBookingRatingService> _mockBookingRatingService;
    private readonly Mock<RefundCalculationService> _mockRefundCalc;
    private readonly Mock<IWalkInService> _mockWalkInService;
    private readonly Mock<IPlayerKarmaService> _mockKarmaService;
    private readonly Mock<ISystemConfigurationProvider> _mockConfigProvider;
    private readonly Mock<IHttpContextAccessor> _mockHttpContextAccessor;
    private readonly Mock<ISettlementService> _mockSettlementService;

    private readonly ReservationService _service;

    private readonly Guid _testCafeId = Guid.NewGuid();
    private readonly Guid _testHostId = Guid.NewGuid();
    private readonly Guid _testGameId = Guid.NewGuid();

    public ReservationServiceCafeScheduleValidationTests()
    {
        _mockDb = new Mock<BoardVerseDbContext>(new DbContextOptions<BoardVerseDbContext>());
        _mockWalletService = new Mock<IWalletService>();
        _mockWalletRepository = new Mock<IWalletRepository>();
        _mockReservationRepository = new Mock<IReservationRepository>();
        _mockLobbyRepository = new Mock<ILobbyRepository>();
        _mockSeatInventoryRepository = new Mock<ISeatInventoryRepository>();
        _mockGameInventoryRepository = new Mock<IGameInventoryRepository>();
        _mockCafeInventoryRepository = new Mock<ICafeInventoryRepository>();
        _mockCafeConfigRepository = new Mock<ICafeConfigRepository>();
        _mockCafeRepository = new Mock<ICafeRepository>();
        _mockUserRepository = new Mock<IUserManagementRepository>();
        _mockGameRepository = new Mock<IGameTemplateRepository>();
        _mockOutboxRepository = new Mock<IOutboxRepository>();
        _mockActiveSessionRepository = new Mock<IActiveSessionRepository>();
        _mockScheduleResolver = new Mock<IScheduleResolver>();
        _mockLogger = new Mock<ILogger<ReservationService>>();
        _depositCalculator = new DepositCalculator();
        _mockEligibilityValidator = new Mock<EligibilityValidator>(MockBehavior.Loose, null!, null!, null!, null!, null!);
        _timeProvider = TimeProvider.System;
        _mockBookingRatingService = new Mock<IBookingRatingService>();
        _mockRefundCalc = new Mock<RefundCalculationService>(MockBehavior.Loose, null!);
        _mockWalkInService = new Mock<IWalkInService>();
        _mockKarmaService = new Mock<IPlayerKarmaService>();
        _mockConfigProvider = new Mock<ISystemConfigurationProvider>();
        _mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        _mockSettlementService = new Mock<ISettlementService>();

        _service = new ReservationService(
            _mockDb.Object,
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
            _mockEligibilityValidator.Object,
            _mockScheduleResolver.Object,
            _mockLogger.Object,
            _timeProvider,
            _mockBookingRatingService.Object,
            _mockRefundCalc.Object,
            _mockWalkInService.Object,
            _mockKarmaService.Object,
            _mockConfigProvider.Object,
            _mockHttpContextAccessor.Object,
            _mockSettlementService.Object
        );
    }

    #region Same-day sessions (preferredEnd > preferredStart)

    [Fact]
    public async Task CreateQuoteAsync_SameDay_ValidTimes_ShouldPass()
    {
        // Arrange
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var preferredStart = new TimeOnly(14, 0); // 14:00
        var preferredEnd = new TimeOnly(18, 0);   // 18:00

        var request = new ReservationQuoteRequestDto
        {
            CafeId = _testCafeId,
            GameId = _testGameId,
            PlayDate = playDate,
            PreferredStartTime = preferredStart,
            PreferredEndTime = preferredEnd,
            MaxPlayers = 4,
            MinPlayers = 2,
            IsPrivate = false,
            IdempotencyKey = $"test-{Guid.NewGuid():N}"
        };

        // Mock schedule resolver: cafe mở 08:00-22:00
        _mockScheduleResolver
            .Setup(x => x.ResolveAsync(_testCafeId, playDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule
            {
                IsClosed = false,
                OpenTime = new TimeOnly(8, 0),
                CloseTime = new TimeOnly(22, 0)
            });

        // Mock các dependencies khác
        SetupCommonMocks(request);

        // Act
        var result = await _service.CreateQuoteAsync(_testHostId, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(preferredStart, result.PreferredStartTime);
        Assert.Equal(preferredEnd, result.PreferredEndTime);
    }

    [Fact]
    public async Task CreateQuoteAsync_SameDay_StartBeforeOpen_ShouldThrow()
    {
        // Arrange
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var preferredStart = new TimeOnly(7, 0);  // 07:00 - trước giờ mở cửa
        var preferredEnd = new TimeOnly(10, 0);

        var request = new ReservationQuoteRequestDto
        {
            CafeId = _testCafeId,
            GameId = _testGameId,
            PlayDate = playDate,
            PreferredStartTime = preferredStart,
            PreferredEndTime = preferredEnd,
            MaxPlayers = 4,
            MinPlayers = 2,
            IsPrivate = false,
            IdempotencyKey = $"test-{Guid.NewGuid():N}"
        };

        // Mock schedule: cafe mở 08:00-22:00
        _mockScheduleResolver
            .Setup(x => x.ResolveAsync(_testCafeId, playDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule
            {
                IsClosed = false,
                OpenTime = new TimeOnly(8, 0),
                CloseTime = new TimeOnly(22, 0)
            });

        SetupCommonMocks(request);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateQuoteAsync(_testHostId, request));

        Assert.Contains("trước giờ mở cửa", ex.Message);
    }

    [Fact]
    public async Task CreateQuoteAsync_SameDay_EndAfterClose_ShouldThrow()
    {
        // Arrange
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var preferredStart = new TimeOnly(20, 0);
        var preferredEnd = new TimeOnly(23, 0);   // 23:00 - sau giờ đóng cửa

        var request = new ReservationQuoteRequestDto
        {
            CafeId = _testCafeId,
            GameId = _testGameId,
            PlayDate = playDate,
            PreferredStartTime = preferredStart,
            PreferredEndTime = preferredEnd,
            MaxPlayers = 4,
            MinPlayers = 2,
            IsPrivate = false,
            IdempotencyKey = $"test-{Guid.NewGuid():N}"
        };

        // Mock schedule: cafe mở 08:00-22:00
        _mockScheduleResolver
            .Setup(x => x.ResolveAsync(_testCafeId, playDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule
            {
                IsClosed = false,
                OpenTime = new TimeOnly(8, 0),
                CloseTime = new TimeOnly(22, 0)
            });

        SetupCommonMocks(request);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateQuoteAsync(_testHostId, request));

        Assert.Contains("sau giờ đóng cửa", ex.Message);
    }

    #endregion

    #region Overnight sessions (preferredEnd < preferredStart)

    [Fact]
    public async Task CreateQuoteAsync_Overnight_ValidTimes_ShouldPass()
    {
        // Arrange
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var nextDay = playDate.AddDays(1);
        var preferredStart = new TimeOnly(22, 0); // 22:00 ngày playDate
        var preferredEnd = new TimeOnly(2, 0);    // 02:00 ngày kế tiếp (overnight)

        var request = new ReservationQuoteRequestDto
        {
            CafeId = _testCafeId,
            GameId = _testGameId,
            PlayDate = playDate,
            PreferredStartTime = preferredStart,
            PreferredEndTime = preferredEnd,
            MaxPlayers = 4,
            MinPlayers = 2,
            IsPrivate = false,
            IdempotencyKey = $"test-{Guid.NewGuid():N}"
        };

        // Mock schedule ngày playDate: 08:00-23:59
        _mockScheduleResolver
            .Setup(x => x.ResolveAsync(_testCafeId, playDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule
            {
                IsClosed = false,
                OpenTime = new TimeOnly(8, 0),
                CloseTime = new TimeOnly(23, 59)
            });

        // Mock schedule ngày kế tiếp: 00:00-06:00
        _mockScheduleResolver
            .Setup(x => x.ResolveAsync(_testCafeId, nextDay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule
            {
                IsClosed = false,
                OpenTime = new TimeOnly(0, 0),
                CloseTime = new TimeOnly(6, 0)
            });

        SetupCommonMocks(request);

        // Act
        var result = await _service.CreateQuoteAsync(_testHostId, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(preferredStart, result.PreferredStartTime);
        Assert.Equal(preferredEnd, result.PreferredEndTime);
    }

    [Fact]
    public async Task CreateQuoteAsync_Overnight_NextDayClosed_ShouldThrow()
    {
        // Arrange
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var nextDay = playDate.AddDays(1);
        var preferredStart = new TimeOnly(22, 0);
        var preferredEnd = new TimeOnly(2, 0);

        var request = new ReservationQuoteRequestDto
        {
            CafeId = _testCafeId,
            GameId = _testGameId,
            PlayDate = playDate,
            PreferredStartTime = preferredStart,
            PreferredEndTime = preferredEnd,
            MaxPlayers = 4,
            MinPlayers = 2,
            IsPrivate = false,
            IdempotencyKey = $"test-{Guid.NewGuid():N}"
        };

        // Mock schedule ngày playDate: mở cửa
        _mockScheduleResolver
            .Setup(x => x.ResolveAsync(_testCafeId, playDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule
            {
                IsClosed = false,
                OpenTime = new TimeOnly(8, 0),
                CloseTime = new TimeOnly(23, 59)
            });

        // Mock schedule ngày kế tiếp: ĐÓNG CỬA
        _mockScheduleResolver
            .Setup(x => x.ResolveAsync(_testCafeId, nextDay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule
            {
                IsClosed = true,
                OpenTime = TimeOnly.MinValue,
                CloseTime = TimeOnly.MinValue
            });

        SetupCommonMocks(request);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateQuoteAsync(_testHostId, request));

        Assert.Contains("đóng cửa", ex.Message);
        Assert.Contains(nextDay.ToString("dd/MM/yyyy"), ex.Message);
    }

    [Fact]
    public async Task CreateQuoteAsync_Overnight_EndAfterNextDayClose_ShouldThrow()
    {
        // Arrange
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var nextDay = playDate.AddDays(1);
        var preferredStart = new TimeOnly(22, 0);
        var preferredEnd = new TimeOnly(7, 0);    // 07:00 - SAU giờ đóng cửa ngày kế tiếp

        var request = new ReservationQuoteRequestDto
        {
            CafeId = _testCafeId,
            GameId = _testGameId,
            PlayDate = playDate,
            PreferredStartTime = preferredStart,
            PreferredEndTime = preferredEnd,
            MaxPlayers = 4,
            MinPlayers = 2,
            IsPrivate = false,
            IdempotencyKey = $"test-{Guid.NewGuid():N}"
        };

        // Mock schedule ngày playDate: mở cửa
        _mockScheduleResolver
            .Setup(x => x.ResolveAsync(_testCafeId, playDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule
            {
                IsClosed = false,
                OpenTime = new TimeOnly(8, 0),
                CloseTime = new TimeOnly(23, 59)
            });

        // Mock schedule ngày kế tiếp: mở 00:00-06:00 (đóng cửa lúc 06:00)
        _mockScheduleResolver
            .Setup(x => x.ResolveAsync(_testCafeId, nextDay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule
            {
                IsClosed = false,
                OpenTime = new TimeOnly(0, 0),
                CloseTime = new TimeOnly(6, 0)
            });

        SetupCommonMocks(request);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateQuoteAsync(_testHostId, request));

        Assert.Contains("sau giờ đóng cửa", ex.Message);
    }

    #endregion

    #region Edge cases - Boundary times

    [Fact]
    public async Task CreateQuoteAsync_StartExactlyAtOpen_ShouldPass()
    {
        // Arrange
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var preferredStart = new TimeOnly(8, 0);  // Đúng giờ mở cửa
        var preferredEnd = new TimeOnly(12, 0);

        var request = new ReservationQuoteRequestDto
        {
            CafeId = _testCafeId,
            GameId = _testGameId,
            PlayDate = playDate,
            PreferredStartTime = preferredStart,
            PreferredEndTime = preferredEnd,
            MaxPlayers = 4,
            MinPlayers = 2,
            IsPrivate = false,
            IdempotencyKey = $"test-{Guid.NewGuid():N}"
        };

        _mockScheduleResolver
            .Setup(x => x.ResolveAsync(_testCafeId, playDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule
            {
                IsClosed = false,
                OpenTime = new TimeOnly(8, 0),
                CloseTime = new TimeOnly(22, 0)
            });

        SetupCommonMocks(request);

        // Act
        var result = await _service.CreateQuoteAsync(_testHostId, request);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateQuoteAsync_EndExactlyAtClose_ShouldPass()
    {
        // Arrange
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var preferredStart = new TimeOnly(18, 0);
        var preferredEnd = new TimeOnly(22, 0);   // Đúng giờ đóng cửa

        var request = new ReservationQuoteRequestDto
        {
            CafeId = _testCafeId,
            GameId = _testGameId,
            PlayDate = playDate,
            PreferredStartTime = preferredStart,
            PreferredEndTime = preferredEnd,
            MaxPlayers = 4,
            MinPlayers = 2,
            IsPrivate = false,
            IdempotencyKey = $"test-{Guid.NewGuid():N}"
        };

        _mockScheduleResolver
            .Setup(x => x.ResolveAsync(_testCafeId, playDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule
            {
                IsClosed = false,
                OpenTime = new TimeOnly(8, 0),
                CloseTime = new TimeOnly(22, 0)
            });

        SetupCommonMocks(request);

        // Act
        var result = await _service.CreateQuoteAsync(_testHostId, request);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateQuoteAsync_Overnight_EndExactlyAtNextDayClose_ShouldPass()
    {
        // Arrange
        var playDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var nextDay = playDate.AddDays(1);
        var preferredStart = new TimeOnly(22, 0);
        var preferredEnd = new TimeOnly(6, 0);    // Đúng giờ đóng cửa ngày kế tiếp

        var request = new ReservationQuoteRequestDto
        {
            CafeId = _testCafeId,
            GameId = _testGameId,
            PlayDate = playDate,
            PreferredStartTime = preferredStart,
            PreferredEndTime = preferredEnd,
            MaxPlayers = 4,
            MinPlayers = 2,
            IsPrivate = false,
            IdempotencyKey = $"test-{Guid.NewGuid():N}"
        };

        _mockScheduleResolver
            .Setup(x => x.ResolveAsync(_testCafeId, playDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule
            {
                IsClosed = false,
                OpenTime = new TimeOnly(8, 0),
                CloseTime = new TimeOnly(23, 59)
            });

        _mockScheduleResolver
            .Setup(x => x.ResolveAsync(_testCafeId, nextDay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSchedule
            {
                IsClosed = false,
                OpenTime = new TimeOnly(0, 0),
                CloseTime = new TimeOnly(6, 0)
            });

        SetupCommonMocks(request);

        // Act
        var result = await _service.CreateQuoteAsync(_testHostId, request);

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region Helper methods

    private void SetupCommonMocks(ReservationQuoteRequestDto request)
    {
        // Mock cafe exists and active
        _mockCafeRepository
            .Setup(x => x.GetActiveByIdAsync(request.CafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe
            {
                Id = request.CafeId,
                Name = "Test Cafe",
                Address = "Test Address",
                BasePrice = 50000
            });

        // Mock game exists
        _mockGameRepository
            .Setup(x => x.GetByIdAsync(request.GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameTemplate
            {
                Id = request.GameId,
                Name = "Test Game",
                MinPlayers = 2,
                MaxPlayers = 6
            });

        // Mock cafe config
        _mockCafeConfigRepository
            .Setup(x => x.GetOrCreateDefaultAsync(request.CafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CafeConfig
            {
                CafeId = request.CafeId,
                DepositRatePerPerson = 10,
                Capacity = 50,
                RecruitmentDeadlineBufferMinutes = 120
            });

        // Mock wallet
        _mockWalletRepository
            .Setup(x => x.GetByUserIdForUpdateAsync(_testHostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Wallet
            {
                UserId = _testHostId,
                AvailableBalance = 100000,
                HeldBalance = 0,
                RiskMultiplier = 1.0m,
                IsCoolingOff = false,
                AccountStatus = AccountStatus.Active
            });

        // Mock cafe inventory
        _mockCafeInventoryRepository
            .Setup(x => x.GetByCafeAndGameTemplateAsync(request.CafeId, request.GameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CafeGameInventory
            {
                CafeId = request.CafeId,
                GameTemplateId = request.GameId,
                BoxQuantity = 3
            });

        // Mock eligibility validator (always pass for quote)
        _mockEligibilityValidator
            .Setup(x => x.ValidateHostCanCreateAsync(
                It.IsAny<HostReservationContext>(),
                It.IsAny<IHttpContextAccessor>(),
                It.IsAny<ISystemConfigurationProvider>(),
                It.IsAny<ILogger>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Note: DepositCalculator is a real instance, not mocked.
        // It will use the mocked IScheduleResolver internally.
    }

    #endregion
}
