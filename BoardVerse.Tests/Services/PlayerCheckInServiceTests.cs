using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
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
/// Unit test cho PlayerCheckInService.CheckInByTokenAsync (POS QR 2-chiều).
/// BR §21A.7 — Player scan QR POS → check-in vào reservation của mình.
///
/// Phạm vi test: chỉ cover các guard logic trước khi chạm DB query (token lookup,
/// IsRevoked / ExpiresAt / ConsumedAt checks). Vì PlayerCheckInService dùng
/// BoardVerseDbContext trực tiếp (không qua repo) cho table/box picking, ta skip
/// những test path đó — chúng sẽ được cover bởi integration test.
/// </summary>
public class PlayerCheckInServiceTests
{
    private readonly Mock<IPosCheckInTokenRepository> _tokenRepo = new();
    private readonly Mock<IReservationRepository> _reservationRepo = new();
    private readonly Mock<ILobbyRepository> _lobbyRepo = new();
    private readonly Mock<ICafePosService> _posService = new();
    private readonly Mock<ILogger<PlayerCheckInService>> _logger = new();
    private readonly BoardVerseDbContext _db;

    public PlayerCheckInServiceTests()
    {
        _db = new FakeDbContext();
    }

    private PlayerCheckInService CreateService() => new(
        _tokenRepo.Object,
        _reservationRepo.Object,
        _lobbyRepo.Object,
        _posService.Object,
        _db,
        _logger.Object,
        new Mock<ISystemConfigurationProvider>().Object);

    private static PosCheckInToken BuildValidToken(
        Guid cafeId,
        Guid reservationId,
        DateTime? expiresAt = null,
        bool isRevoked = false,
        DateTime? consumedAt = null,
        Guid? consumedByUserId = null,
        Guid? resultSessionId = null)
    {
        return new PosCheckInToken
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            ReservationId = reservationId,
            Token = "ABCDEFGHJKLMNPQR",
            CreatedByStaffId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddMinutes(25),
            IsRevoked = isRevoked,
            ConsumedAt = consumedAt,
            ConsumedByUserId = consumedByUserId,
            ResultActiveSessionId = resultSessionId
        };
    }

    [Fact]
    public async Task CheckInByToken_TokenNotFound_ThrowsNotFound()
    {
        _tokenRepo.Setup(r => r.GetByTokenAsync(It.IsAny<string>())).ReturnsAsync((PosCheckInToken?)null);

        var svc = CreateService();
        var playerId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.CheckInByTokenAsync(playerId, new PlayerScanTokenRequestDto { Token = "ZZZZZZZZZZZZZZZZ" }));
        Assert.Contains("ZZZZZZZZZZZZZZZZ", ex.Message);
    }

    [Fact]
    public async Task CheckInByToken_TokenRevoked_ThrowsConflict()
    {
        var token = BuildValidToken(Guid.NewGuid(), Guid.NewGuid(), isRevoked: true);
        _tokenRepo.Setup(r => r.GetByTokenAsync(token.Token)).ReturnsAsync(token);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CheckInByTokenAsync(Guid.NewGuid(),
                new PlayerScanTokenRequestDto { Token = token.Token }));
    }

    [Fact]
    public async Task CheckInByToken_TokenExpired_ThrowsConflict()
    {
        var token = BuildValidToken(Guid.NewGuid(), Guid.NewGuid(),
            expiresAt: DateTime.UtcNow.AddMinutes(-1));
        _tokenRepo.Setup(r => r.GetByTokenAsync(token.Token)).ReturnsAsync(token);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CheckInByTokenAsync(Guid.NewGuid(),
                new PlayerScanTokenRequestDto { Token = token.Token }));
    }

    [Fact]
    public async Task CheckInByToken_TokenConsumedByOtherPlayer_ThrowsConflict()
    {
        var token = BuildValidToken(Guid.NewGuid(), Guid.NewGuid(),
            consumedAt: DateTime.UtcNow.AddSeconds(-30),
            consumedByUserId: Guid.NewGuid(),
            resultSessionId: Guid.NewGuid());
        _tokenRepo.Setup(r => r.GetByTokenAsync(token.Token)).ReturnsAsync(token);

        var svc = CreateService();
        var anotherPlayer = Guid.NewGuid();

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CheckInByTokenAsync(anotherPlayer,
                new PlayerScanTokenRequestDto { Token = token.Token }));
    }

    [Fact]
    public async Task CheckInByToken_TokenConsumedBySamePlayer_ReturnsExistingSession()
    {
        var playerId = Guid.NewGuid();
        var existingSessionId = Guid.NewGuid();
        var consumedAt = DateTime.UtcNow.AddSeconds(-15);
        var token = BuildValidToken(Guid.NewGuid(), Guid.NewGuid(),
            consumedAt: consumedAt,
            consumedByUserId: playerId,
            resultSessionId: existingSessionId);
        _tokenRepo.Setup(r => r.GetByTokenAsync(token.Token)).ReturnsAsync(token);

        var svc = CreateService();
        var result = await svc.CheckInByTokenAsync(playerId,
            new PlayerScanTokenRequestDto { Token = token.Token });

        Assert.Equal(existingSessionId, result.ActiveSessionId);
        Assert.Equal(consumedAt, result.CheckedInAt);
        // Idempotent replay KHÔNG gọi lại POS check-in.
        _posService.Verify(p => p.CheckInByCodeAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CheckInRequestDto>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckInByToken_NormalizesTokenToUpperBeforeLookup()
    {
        var playerId = Guid.NewGuid();
        var existingSessionId = Guid.NewGuid();
        var consumedAt = DateTime.UtcNow.AddSeconds(-5);
        var token = BuildValidToken(Guid.NewGuid(), Guid.NewGuid(),
            consumedAt: consumedAt,
            consumedByUserId: playerId,
            resultSessionId: existingSessionId);

        _tokenRepo.Setup(r => r.GetByTokenAsync("ABCDEFGHJKLMNPQR")).ReturnsAsync(token);

        var svc = CreateService();
        var result = await svc.CheckInByTokenAsync(playerId,
            new PlayerScanTokenRequestDto { Token = "abcdefghjklmnpqr" });

        Assert.Equal(existingSessionId, result.ActiveSessionId);
    }

    [Fact]
    public async Task CheckInByToken_TokenWithoutReservation_ThrowsConflict()
    {
        var token = BuildValidToken(Guid.NewGuid(), Guid.NewGuid());
        token.ReservationId = null;
        _tokenRepo.Setup(r => r.GetByTokenAsync(token.Token)).ReturnsAsync(token);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CheckInByTokenAsync(Guid.NewGuid(),
                new PlayerScanTokenRequestDto { Token = token.Token }));
    }
}
