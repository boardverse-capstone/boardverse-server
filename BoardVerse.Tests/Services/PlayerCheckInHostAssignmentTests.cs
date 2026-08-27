using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using BoardVerse.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit test cho CafePosService.CheckInByReservationCodeForPlayerAsync.
/// BR §21A.7 — Player quét QR POS → check-in vào reservation của mình.
///
/// Trọng tâm: xác nhận ActiveSession.HostId = reservation.HostId (host gốc),
/// KHÔNG phải player.UserId (kể cả khi player là member, không phải host).
///
/// Đây là regression test cho bug "check-in cho người chơi thì ko gán host".
/// </summary>
public class PlayerCheckInHostAssignmentTests
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

    private static readonly MemoryCache MemoryCache = new(new MemoryCacheOptions());

    public PlayerCheckInHostAssignmentTests()
    {
        // Force InMemory provider — không hit Neon DB (test đã mock toàn bộ repo).
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase($"PlayerCheckInHostAssignment-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new BoardVerseDbContext(options);
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
        MemoryCache,
        _logger.Object,
        _db);

    /// <summary>
    /// BUG 2026-08-27: Khi member (không phải host) thực hiện player check-in,
    /// session.HostId phải = reservation.HostId, KHÔNG phải member.UserId.
    ///
    /// Setup:
    /// - Reservation có HostId = hostUserId
    /// - Player gọi check-in với playerUserId (≠ hostUserId)
    /// - Lobby có 2 members: host (IsHost=true) + player (IsHost=false)
    ///
    /// Expected:
    /// - session.HostId = hostUserId (reservation host)
    /// - 2 ActiveSessionMember: host (IsHost=true) + player (IsHost=false)
    /// </summary>
    [Fact]
    public async Task CheckInByReservationCodeForPlayer_AsMember_AssignsReservationHost_NotPlayer()
    {
        var cafeId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var hostUserId = Guid.NewGuid();
        var playerUserId = Guid.NewGuid(); // member, không phải host
        var gameTemplateId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var boxId = Guid.NewGuid();
        var reservationCode = "ABC23456"; // 8-char [A-Z2-9]{8} ReservationCode

        var managerId = Guid.NewGuid();

        // Mock: ReservationRepository trả về reservation (bypass DB seed).
        var mockReservation = new Reservation
        {
            Id = reservationId,
            CafeId = cafeId,
            HostId = hostUserId,
            LobbyId = lobbyId,
            GameId = gameTemplateId,
            ReservationCode = reservationCode,
            Status = ReservationStatus.Confirmed,
            ScheduledStartTime = DateTime.UtcNow.AddMinutes(-30),
            ScheduledEndTime = DateTime.UtcNow.AddHours(2),
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow,
            IdempotencyKey = $"test-host-assign-{reservationId:N}"
        };
        _reservationRepo.Setup(r => r.GetByReservationCodeAsync(reservationCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockReservation);

        // Mock: ReservationService.CheckInAsync trả response thành công.
        _reservationService.Setup(s => s.CheckInAsync(It.IsAny<Guid>(), It.IsAny<ReservationCheckInRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid userId, ReservationCheckInRequestDto req, CancellationToken _) =>
                new ReservationCheckInResponseDto
                {
                    ReservationId = reservationId,
                    LobbyId = lobbyId,
                    ActiveSessionId = req.ActiveSessionId,
                    ReservationStatus = ReservationStatus.CheckedIn.ToString(),
                    LobbyStatus = LobbyStatus.InProgress.ToString(),
                    CheckedInAt = DateTime.UtcNow
                });

        // Mock: LobbyRepository trả về lobby với 2 members.
        _lobbyRepo.Setup(r => r.GetByIdWithMembersAsync(lobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Lobby
            {
                Id = lobbyId,
                ReservationId = reservationId,
                CafeId = cafeId,
                HostUserId = hostUserId,
                GameTemplateId = gameTemplateId,
                Status = LobbyStatus.Full,
                ShareCode = $"SH{lobbyId:N}".Substring(0, 8).ToUpper(),
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                UpdatedAt = DateTime.UtcNow,
                Members = new List<LobbyMember>
                {
                    new LobbyMember
                    {
                        Id = Guid.NewGuid(),
                        LobbyId = lobbyId,
                        UserId = hostUserId,
                        IsHost = true,
                        IsActive = true,
                        Status = LobbyMemberStatus.Joined,
                        JoinedAt = DateTime.UtcNow.AddHours(-1)
                    },
                    new LobbyMember
                    {
                        Id = Guid.NewGuid(),
                        LobbyId = lobbyId,
                        UserId = playerUserId,
                        IsHost = false,
                        IsActive = true,
                        Status = LobbyMemberStatus.Joined,
                        JoinedAt = DateTime.UtcNow.AddMinutes(-30)
                    }
                }
            });

        // Mock: table + box.
        _posRepo.Setup(r => r.GetTableAsync(cafeId, tableId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CafeTable
            {
                Id = tableId,
                CafeId = cafeId,
                Name = "T1",
                SortOrder = 0,
                Status = CafeTableStatus.Available,
                IsActive = true
            });

        _posRepo.Setup(r => r.GetBoxByBarcodeAsync(cafeId, "BX001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CafeInventoryBox
            {
                Id = boxId,
                Barcode = "BX001",
                Status = CafeGameInventoryStatus.Available,
                IsActive = true,
                CafeGameInventory = new CafeGameInventory
                {
                    Id = Guid.NewGuid(),
                    CafeId = cafeId,
                    GameTemplateId = gameTemplateId,
                    IsActive = true,
                    Status = CafeGameInventoryStatus.Available
                }
            });

        _posRepo.Setup(r => r.GetActiveSessionByBoxIdAsync(boxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveSession?)null);

        // Capture session + members đã persist.
        ActiveSession? capturedSession = null;
        var capturedMembers = new List<ActiveSessionMember>();

        _posRepo.Setup(r => r.AddSessionAsync(It.IsAny<ActiveSession>(), It.IsAny<CancellationToken>()))
            .Callback<ActiveSession, CancellationToken>((s, _) => capturedSession = s)
            .Returns(Task.CompletedTask);

        _posRepo.Setup(r => r.AddSessionMemberAsync(It.IsAny<ActiveSessionMember>(), It.IsAny<CancellationToken>()))
            .Callback<ActiveSessionMember, CancellationToken>((m, _) => capturedMembers.Add(m))
            .Returns(Task.CompletedTask);

        _posRepo.Setup(r => r.AddSessionGameAsync(It.IsAny<ActiveSessionGame>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _posRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test Cafe", Address = "123 Test St", ManagerId = managerId, IsActive = true });

        _activeSessionRepo.Setup(r => r.GetByIdWithMembersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new ActiveSession
            {
                Id = id,
                CafeId = cafeId,
                HostId = capturedSession?.HostId ?? hostUserId,
                LobbyId = lobbyId,
                Status = GroupSessionStatus.Active,
                StartedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                DepositAppliedAmount = 0,
                Subtotal = 0,
                TotalAmount = 0,
                Members = capturedMembers.Select(m => new ActiveSessionMember
                {
                    Id = m.Id,
                    ActiveSessionId = id,
                    UserId = m.UserId,
                    IsHost = m.IsHost,
                    IsGuestSlot = m.IsGuestSlot,
                    JoinedAt = m.JoinedAt,
                    Status = m.Status
                }).ToList()
            });

        _posHubService.Setup(s => s.NotifySessionActivatedAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>()))
            .Returns(Task.CompletedTask);

        // Act: Member (playerUserId) gọi player check-in.
        var svc = CreateService();
        var request = new CheckInRequestDto
        {
            Code = reservationCode,
            CafeTableId = tableId,
            Barcode = "BX001",
            IdempotencyKey = $"test-{Guid.NewGuid():N}"
        };

        var result = await svc.CheckInByReservationCodeForPlayerAsync(
            cafeId, playerUserId, request);

        // Assert 1: session được persist với HostId = reservation.HostId (hostUserId),
        // KHÔNG phải playerUserId (member).
        Assert.NotNull(capturedSession);
        Assert.Equal(hostUserId, capturedSession!.HostId);
        Assert.NotEqual(playerUserId, capturedSession.HostId);

        // Assert 2: Có 2 members được persist — host + player (member).
        Assert.Equal(2, capturedMembers.Count);

        var hostMember = capturedMembers.FirstOrDefault(m => m.IsHost);
        Assert.NotNull(hostMember);
        Assert.Equal(hostUserId, hostMember!.UserId);

        var playerMember = capturedMembers.FirstOrDefault(m => !m.IsHost);
        Assert.NotNull(playerMember);
        Assert.Equal(playerUserId, playerMember!.UserId);
        Assert.False(playerMember.IsHost);
    }
}
