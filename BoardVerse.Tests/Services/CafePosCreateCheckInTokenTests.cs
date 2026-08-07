using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using BoardVerse.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit test cho CafePosService.CreateCheckInTokenAsync (POS QR 2-chiều).
/// BR §21A.7 — POS sinh token 16-char alphanumeric, lưu DB, player scan để check-in.
/// </summary>
public class CafePosCreateCheckInTokenTests
{
    private readonly Mock<ICafePosRepository> _posRepo = new();
    private readonly Mock<ICafeRepository> _cafeRepo = new();
    private readonly Mock<IBookingDepositRepository> _depositRepo = new();
    private readonly Mock<IBookingRepository> _bookingRepo = new();
    private readonly Mock<IActiveSessionRepository> _activeSessionRepo = new();
    private readonly Mock<IActiveSessionService> _activeSessionService = new();
    private readonly Mock<IPosHubService> _posHubService = new();
    private readonly Mock<ILobbyRepository> _lobbyRepo = new();
    private readonly Mock<IUserProfileRepository> _userProfileRepo = new();
    private readonly Mock<IReservationService> _reservationService = new();
    private readonly Mock<IReservationRepository> _reservationRepo = new();
    private readonly Mock<IPosCheckInTokenRepository> _tokenRepo = new();
    private readonly Mock<ILogger<CafePosService>> _logger = new();
    private readonly BoardVerseDbContext _db;

    public CafePosCreateCheckInTokenTests()
    {
        _db = new FakeDbContext();

        // Default: staff có quyền POS.
        _posRepo.Setup(r => r.CanOperateCafeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        // Default: token unique ngay lần thử đầu.
        _tokenRepo.Setup(r => r.TokenExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);
    }

    private void SetupActiveCafe(Cafe cafe)
    {
        // EnsurePosAccessAsync gọi GetActiveByIdAsync (filter IsActive = true).
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafe.Id)).ReturnsAsync(cafe);
        // CreateCheckInTokenAsync cũng gọi GetByIdAsync để validate cafe tồn tại.
        _cafeRepo.Setup(r => r.GetByIdAsync(cafe.Id)).ReturnsAsync(cafe);
    }

    private CafePosService CreateService() => new(
        _posRepo.Object,
        _cafeRepo.Object,
        _depositRepo.Object,
        _bookingRepo.Object,
        _activeSessionRepo.Object,
        _activeSessionService.Object,
        _posHubService.Object,
        _lobbyRepo.Object,
        _userProfileRepo.Object,
        _reservationService.Object,
        _reservationRepo.Object,
        _tokenRepo.Object,
        _logger.Object,
        _db);

    private static Cafe BuildCafe(Guid id, Guid managerId, bool isActive = true) => new()
    {
        Id = id,
        ManagerId = managerId,
        Name = "Test Cafe",
        Address = "123 Test Street",
        IsActive = isActive
    };

    [Fact]
    public async Task CreateCheckInToken_StaffNotAuthorized_ThrowsForbidden()
    {
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        _posRepo.Setup(r => r.CanOperateCafeAsync(cafeId, staffId, "CafeStaff"))
            .ReturnsAsync(false);
        // Đảm bảo EnsurePosAccessAsync không throw NotFoundException trước khi tới check quyền.
        var cafe = BuildCafe(cafeId, Guid.NewGuid());
        SetupActiveCafe(cafe);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.CreateCheckInTokenAsync(cafeId, staffId, "CafeStaff", new CreatePosCheckInTokenRequestDto()));
    }

    [Fact]
    public async Task CreateCheckInToken_CafeNotFound_ThrowsNotFound()
    {
        var cafeId = Guid.NewGuid();
        // Đảm bảo cả GetActiveByIdAsync và GetByIdAsync đều trả null.
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync((Cafe?)null);
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync((Cafe?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.CreateCheckInTokenAsync(cafeId, Guid.NewGuid(), "Manager", new CreatePosCheckInTokenRequestDto()));
    }

    [Fact]
    public async Task CreateCheckInToken_NoReservation_PersistsTokenWithDefaultTtl()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var cafe = BuildCafe(cafeId, managerId);
        SetupActiveCafe(cafe);

        PosCheckInToken? captured = null;
        _tokenRepo.Setup(r => r.AddAsync(It.IsAny<PosCheckInToken>()))
            .Callback<PosCheckInToken>(t => captured = t)
            .Returns(Task.CompletedTask);

        var beforeCall = DateTime.UtcNow;
        var svc = CreateService();
        var result = await svc.CreateCheckInTokenAsync(
            cafeId, staffId, "Manager", new CreatePosCheckInTokenRequestDto());

        Assert.NotNull(result);
        Assert.NotNull(captured);
        Assert.Equal(cafeId, captured!.CafeId);
        Assert.Equal(staffId, captured.CreatedByStaffId);
        Assert.Null(captured.ReservationId);
        Assert.False(captured.IsRevoked);
        Assert.Null(captured.ConsumedAt);

        // Token: 16-char alphanumeric uppercase, exclude 0/1/I/O.
        Assert.Equal(16, result.Token.Length);
        Assert.Matches("^[A-Z2-9]{16}$", result.Token);
        Assert.Equal(result.Token, captured.Token);

        // TTL mặc định 30 phút.
        var ttlSpan = result.ExpiresAt - result.CreatedAt;
        Assert.InRange(ttlSpan.TotalMinutes, 29.5, 30.5);

        // QR payload deep-link.
        Assert.StartsWith("boardverse://check-in?token=", result.QrPayload);
        Assert.Contains(Uri.EscapeDataString(result.Token), result.QrPayload);

        Assert.True(result.CreatedAt >= beforeCall);
    }

    [Fact]
    public async Task CreateCheckInToken_WithCustomTtl_UsesProvidedTtl()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var cafe = BuildCafe(cafeId, managerId);
        SetupActiveCafe(cafe);

        var svc = CreateService();
        var result = await svc.CreateCheckInTokenAsync(
            cafeId, Guid.NewGuid(), "Manager",
            new CreatePosCheckInTokenRequestDto { TtlMinutes = 120 });

        var ttlSpan = result.ExpiresAt - result.CreatedAt;
        Assert.InRange(ttlSpan.TotalMinutes, 119.5, 120.5);
    }

    [Fact]
    public async Task CreateCheckInToken_WithValidReservation_LinksReservation()
    {
        var cafeId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var cafe = BuildCafe(cafeId, Guid.NewGuid());
        var reservation = new Reservation { Id = reservationId, CafeId = cafeId };

        SetupActiveCafe(cafe);
        _reservationRepo.Setup(r => r.GetByIdAsync(reservationId, false)).ReturnsAsync(reservation);

        PosCheckInToken? captured = null;
        _tokenRepo.Setup(r => r.AddAsync(It.IsAny<PosCheckInToken>()))
            .Callback<PosCheckInToken>(t => captured = t)
            .Returns(Task.CompletedTask);

        var svc = CreateService();
        var result = await svc.CreateCheckInTokenAsync(
            cafeId, Guid.NewGuid(), "Manager",
            new CreatePosCheckInTokenRequestDto { ReservationId = reservationId });

        Assert.Equal(reservationId, result.ReservationId);
        Assert.Equal(reservationId, captured!.ReservationId);
    }

    [Fact]
    public async Task CreateCheckInToken_ReservationNotFound_ThrowsNotFound()
    {
        var cafeId = Guid.NewGuid();
        var cafe = BuildCafe(cafeId, Guid.NewGuid());
        SetupActiveCafe(cafe);
        _reservationRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), false))
            .ReturnsAsync((Reservation?)null);

        var svc = CreateService();
        var request = new CreatePosCheckInTokenRequestDto { ReservationId = Guid.NewGuid() };

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.CreateCheckInTokenAsync(cafeId, Guid.NewGuid(), "Manager", request));
    }

    [Fact]
    public async Task CreateCheckInToken_ReservationCafeMismatch_ThrowsConflict()
    {
        var cafeId = Guid.NewGuid();
        var otherCafeId = Guid.NewGuid();
        var cafe = BuildCafe(cafeId, Guid.NewGuid());
        var reservation = new Reservation { Id = Guid.NewGuid(), CafeId = otherCafeId };

        SetupActiveCafe(cafe);
        _reservationRepo.Setup(r => r.GetByIdAsync(reservation.Id, false)).ReturnsAsync(reservation);

        var svc = CreateService();
        var request = new CreatePosCheckInTokenRequestDto { ReservationId = reservation.Id };

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateCheckInTokenAsync(cafeId, Guid.NewGuid(), "Manager", request));
    }

    [Fact]
    public async Task CreateCheckInToken_TokenAlreadyExists_RetriesUntilUnique()
    {
        var cafeId = Guid.NewGuid();
        var cafe = BuildCafe(cafeId, Guid.NewGuid());
        SetupActiveCafe(cafe);

        // Lần 1 và 2: đã tồn tại. Lần 3: unique.
        var callCount = 0;
        _tokenRepo.Setup(r => r.TokenExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(() => ++callCount <= 2);

        var svc = CreateService();
        var result = await svc.CreateCheckInTokenAsync(
            cafeId, Guid.NewGuid(), "Manager", new CreatePosCheckInTokenRequestDto());

        Assert.NotNull(result);
        Assert.Equal(16, result.Token.Length);
        _tokenRepo.Verify(r => r.AddAsync(It.IsAny<PosCheckInToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateCheckInToken_AllAttemptsCollide_ThrowsInvalidOperation()
    {
        var cafeId = Guid.NewGuid();
        var cafe = BuildCafe(cafeId, Guid.NewGuid());
        SetupActiveCafe(cafe);

        // Luôn trả về đã tồn tại → retry 5 lần đều fail.
        _tokenRepo.Setup(r => r.TokenExistsAsync(It.IsAny<string>())).ReturnsAsync(true);

        var svc = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateCheckInTokenAsync(cafeId, Guid.NewGuid(), "Manager",
                new CreatePosCheckInTokenRequestDto()));
    }
}
