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
using BoardVerse.Tests.Fixtures;
using BoardVerse.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NetTopologySuite.Geometries;
using Xunit;


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
public class PlayerCheckInServiceTests : IClassFixture<DatabaseResetFixture>
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
            Token = $"TK{Guid.NewGuid():N}".Substring(0, 16).ToUpper(),
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
        _tokenRepo.Setup(r => r.GetByTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((PosCheckInToken?)null);

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
        _tokenRepo.Setup(r => r.GetByTokenAsync(token.Token, It.IsAny<CancellationToken>())).ReturnsAsync(token);

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
        _tokenRepo.Setup(r => r.GetByTokenAsync(token.Token, It.IsAny<CancellationToken>())).ReturnsAsync(token);

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
        _tokenRepo.Setup(r => r.GetByTokenAsync(token.Token, It.IsAny<CancellationToken>())).ReturnsAsync(token);

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
        _tokenRepo.Setup(r => r.GetByTokenAsync(token.Token, It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var svc = CreateService();
        var result = await svc.CheckInByTokenAsync(playerId,
            new PlayerScanTokenRequestDto { Token = token.Token });

        Assert.Equal(existingSessionId, result.ActiveSessionId);
        Assert.Equal(consumedAt, result.CheckedInAt);
        // Idempotent replay KHÔNG gọi lại POS check-in.
        _posService.Verify(p => p.CheckInByCodeAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CheckInRequestDto>(), It.IsAny<CancellationToken>()),
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

        _tokenRepo.Setup(r => r.GetByTokenAsync("ABCDEFGHJKLMNPQR", It.IsAny<CancellationToken>())).ReturnsAsync(token);

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
        _tokenRepo.Setup(r => r.GetByTokenAsync(token.Token, It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CheckInByTokenAsync(Guid.NewGuid(),
                new PlayerScanTokenRequestDto { Token = token.Token }));
    }

    /// <summary>
    /// BUG FIX 2026-08-27: Service phải gọi <c>CheckInByReservationCodeForPlayerAsync</c>
    /// (bypass <c>EnsurePosAccessAsync</c>) chứ không gọi <c>CheckInByCodeAsync</c> (dành cho POS staff).
    /// Trước fix: service gọi <c>CheckInByCodeAsync</c> với <c>userRole="Player"</c> → repository
    /// <c>CanOperateCafeAsync</c> trả <c>false</c> (Player không phải Manager/CafeStaff) →
    /// service throw <c>ForbiddenException("Từ chối truy cập POS. Bạn không có quyền vận hành quán ...")</c>.
    /// Mobile player nhận 403 với message "Từ chối truy cập POS" dù không liên quan đến vận hành quán.
    ///
    /// Test này mô phỏng bug: mock method CŨ throw ForbiddenException, mock method MỚI return session.
    /// Nếu service vẫn gọi method cũ → test FAIL với ForbiddenException.
    /// Sau fix: service gọi method mới → test PASS.
    /// </summary>
    [Fact]
    public async Task CheckInByToken_HappyPath_CallsPlayerMethod_NotStaffMethod()
    {
        var cafeId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var hostMemberId = Guid.NewGuid();
        var token = BuildValidToken(cafeId, reservationId);
        token.Id = Guid.NewGuid(); // unique id cho DB row
        _tokenRepo.Setup(r => r.GetByTokenAsync(token.Token, It.IsAny<CancellationToken>())).ReturnsAsync(token);

        // Seed cafe manager user trước (FK cho Cafe.ManagerId).
        var cafeManagerId = Guid.NewGuid();
        _db.Users.Add(new User
        {
            Id = cafeManagerId,
            Username = $"cafemanager_test_{cafeManagerId:N}",
            Email = $"manager_{cafeManagerId:N}@test.com"
        });
        _db.Users.Add(new User
        {
            Id = token.CreatedByStaffId,
            Username = $"posstaff_test_{token.CreatedByStaffId:N}",
            Email = $"posstaff_{token.CreatedByStaffId:N}@test.com"
        });
        _db.Users.Add(new User
        {
            Id = playerId,
            Username = $"player_test_{playerId:N}",
            Email = $"player_{playerId:N}@test.com"
        });

        // Save Users trước để FK Cafe → User có hiệu lực.
        await _db.SaveChangesAsync();

        // Insert Cafe bằng raw SQL (bypass Location geography column).
        await _db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""Cafes"" (""Id"", ""Name"", ""Address"", ""ManagerId"", ""IsActive"", ""CreatedAt"", ""UpdatedAt"")
              VALUES ({0}, {1}, {2}, {3}, {4}, NOW(), NOW())
              ON CONFLICT (""Id"") DO NOTHING",
            cafeId, "Test Cafe", "123 Test Street", cafeManagerId, true);

        // Seed reservation (Confirmed, trong check-in window, player là Host).
        var reservation = new Reservation
        {
            Id = reservationId,
            CafeId = cafeId,
            HostId = playerId,
            LobbyId = lobbyId,
            ReservationCode = $"RES{reservationId:N}".Substring(0, 8).ToUpper(),
            GameId = Guid.NewGuid(),
            ScheduledStartTime = DateTime.UtcNow.AddMinutes(-30),
            ScheduledEndTime = DateTime.UtcNow.AddHours(2),
            Status = ReservationStatus.Confirmed,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow,
            IdempotencyKey = $"reservation_test_{reservationId:N}"
        };
        _db.Reservations.Add(reservation);

        // Seed PosCheckInToken row (để ExecuteUpdateAsync chạy được trên InMemory).
        _db.PosCheckInTokens.Add(token);
        _db.Lobbies.Add(new Lobby
        {
            Id = lobbyId,
            ReservationId = reservationId,
            CafeId = cafeId,
            HostUserId = playerId,
            GameTemplateId = reservation.GameId,
            Status = LobbyStatus.Full,
            MinPlayers = 2,
            MaxMembers = 4,
            ShareCode = $"SH{lobbyId:N}".Substring(0, 8).ToUpper(),
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow
        });

        var hostMember = new LobbyMember
        {
            Id = hostMemberId,
            LobbyId = lobbyId,
            UserId = playerId,
            IsHost = true,
            IsActive = true,
            Status = LobbyMemberStatus.Joined,
            JoinedAt = DateTime.UtcNow.AddHours(-1)
        };
        _db.LobbyMembers.Add(hostMember);

        // Seed CafeTable available.
        var table = new CafeTable
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            Name = "T1",
            SortOrder = 0,
            Status = CafeTableStatus.Available,
            IsActive = true
        };
        _db.CafeTables.Add(table);

        // Seed game template + inventory + box available.
        var gameTemplate = new GameTemplate
        {
            Id = reservation.GameId,
            Name = "Catan",
            IsActive = true
        };
        _db.GameTemplates.Add(gameTemplate);

        var inventory = new CafeGameInventory
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            GameTemplateId = reservation.GameId,
            IsActive = true,
            Status = CafeGameInventoryStatus.Available
        };
        _db.CafeGameInventories.Add(inventory);

        var box = new CafeInventoryBox
        {
            Id = Guid.NewGuid(),
            CafeGameInventoryId = inventory.Id,
            Barcode = $"BX{inventory.Id:N}".Substring(0, 8).ToUpper(),
            Status = CafeGameInventoryStatus.Available,
            IsActive = true
        };
        _db.CafeInventoryBoxes.Add(box);

        await _db.SaveChangesAsync();

        // Mock method MỚI (player flow) trả sessionDto thành công.
        var expectedSessionId = Guid.NewGuid();
        _posService.Setup(p => p.CheckInByReservationCodeForPlayerAsync(
                cafeId, playerId, It.IsAny<CheckInRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveSessionDto
            {
                Id = expectedSessionId,
                HostId = playerId,
                Status = GroupSessionStatus.Active
            });

        // Mock method CŨ (staff flow) throw ForbiddenException — mô phỏng bug.
        // Nếu service vẫn gọi method này, test sẽ FAIL với exception này.
        _posService.Setup(p => p.CheckInByCodeAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CheckInRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException(
                "Từ chối truy cập POS. Bạn không có quyền vận hành quán."));

        // Act
        var svc = CreateService();
        var result = await svc.CheckInByTokenAsync(playerId,
            new PlayerScanTokenRequestDto { Token = token.Token });

        // Assert — service đã chọn method mới, KHÔNG gọi method cũ.
        Assert.Equal(expectedSessionId, result.ActiveSessionId);
        Assert.Equal(reservationId, result.ReservationId);
        Assert.Equal(cafeId, result.CafeId);

        _posService.Verify(p => p.CheckInByReservationCodeForPlayerAsync(
            cafeId, playerId, It.IsAny<CheckInRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _posService.Verify(p => p.CheckInByCodeAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CheckInRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verify exception từ <c>CheckInByReservationCodeForPlayerAsync</c> propagate đúng lên caller,
    /// KHÔNG bị nuốt và KHÔNG fallback sang <c>CheckInByCodeAsync</c>.
    /// </summary>
    [Fact]
    public async Task CheckInByToken_PropagatesExceptionFromPlayerMethod()
    {
        var cafeId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var token = BuildValidToken(cafeId, reservationId);
        token.Id = Guid.NewGuid();
        _tokenRepo.Setup(r => r.GetByTokenAsync(token.Token, It.IsAny<CancellationToken>())).ReturnsAsync(token);

        // Seed reservation đủ điều kiện (player là Host).
        var reservation = new Reservation
        {
            Id = reservationId,
            CafeId = cafeId,
            HostId = playerId,
            LobbyId = lobbyId,
            ReservationCode = "RESV5678",
            GameId = Guid.NewGuid(),
            ScheduledStartTime = DateTime.UtcNow.AddMinutes(-30),
            ScheduledEndTime = DateTime.UtcNow.AddHours(2),
            Status = ReservationStatus.Confirmed,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow
        };
        var cafeManagerId2 = Guid.NewGuid();
        _db.Users.Add(new User
        {
            Id = cafeManagerId2,
            Username = $"cafemanager_test2_{cafeManagerId2:N}",
            Email = $"manager2_{cafeManagerId2:N}@test.com"
        });
        _db.Users.Add(new User
        {
            Id = playerId,
            Username = $"player_test2_{playerId:N}",
            Email = $"player2_{playerId:N}@test.com"
        });
        _db.Users.Add(new User
        {
            Id = token.CreatedByStaffId,
            Username = $"posstaff_test2_{token.CreatedByStaffId:N}",
            Email = $"posstaff2_{token.CreatedByStaffId:N}@test.com"
        });

        // Save Users trước để FK Cafe → User có hiệu lực.
        await _db.SaveChangesAsync();

        // Insert Cafe bằng raw SQL (bypass Location geography column).
        await _db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""Cafes"" (""Id"", ""Name"", ""Address"", ""ManagerId"", ""IsActive"", ""CreatedAt"", ""UpdatedAt"")
              VALUES ({0}, {1}, {2}, {3}, {4}, NOW(), NOW())
              ON CONFLICT (""Id"") DO NOTHING",
            cafeId, "Test Cafe", "123 Test Street", cafeManagerId2, true);

        _db.Reservations.Add(reservation);
        _db.PosCheckInTokens.Add(token);

        _db.Lobbies.Add(new Lobby
        {
            Id = lobbyId,
            ReservationId = reservationId,
            CafeId = cafeId,
            HostUserId = playerId,
            GameTemplateId = reservation.GameId,
            Status = LobbyStatus.Full,
            MinPlayers = 2,
            MaxMembers = 4,
            ShareCode = $"SH{lobbyId:N}".Substring(0, 8).ToUpper(),
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow
        });

        _db.LobbyMembers.Add(new LobbyMember
        {
            Id = Guid.NewGuid(),
            LobbyId = lobbyId,
            UserId = playerId,
            IsHost = true,
            IsActive = true,
            Status = LobbyMemberStatus.Joined,
            JoinedAt = DateTime.UtcNow.AddHours(-1)
        });

        _db.CafeTables.Add(new CafeTable
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            Name = "T1",
            SortOrder = 0,
            Status = CafeTableStatus.Available,
            IsActive = true
        });

        var inventory = new CafeGameInventory
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            GameTemplateId = reservation.GameId,
            IsActive = true,
            Status = CafeGameInventoryStatus.Available
        };
        _db.CafeGameInventories.Add(inventory);

        _db.GameTemplates.Add(new GameTemplate
        {
            Id = reservation.GameId,
            Name = "Catan",
            IsActive = true
        });

        _db.CafeInventoryBoxes.Add(new CafeInventoryBox
        {
            Id = Guid.NewGuid(),
            CafeGameInventoryId = inventory.Id,
            Barcode = $"BX{inventory.Id:N}".Substring(0, 8).ToUpper(),
            Status = CafeGameInventoryStatus.Available,
            IsActive = true
        });

        await _db.SaveChangesAsync();

        // Mock method MỚI throw ConflictException (giả lập lỗi từ service).
        _posService.Setup(p => p.CheckInByReservationCodeForPlayerAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CheckInRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Reservation không trong khung giờ check-in."));

        var svc = CreateService();
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CheckInByTokenAsync(playerId,
                new PlayerScanTokenRequestDto { Token = token.Token }));
        Assert.Contains("khung giờ check-in", ex.Message);
    }
}
