using BoardVerse.Core.DTOs.Lobby;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho LobbyMergeService.
/// Bao phủ: Create, Approve, Reject, Cancel, Get, ExpireOverdue.
/// G8 (seat availability), G9 (cross-cafe), G18 (deposit status), G22 (idempotency).
/// 
/// Lưu ý: ApproveMergeAsync dùng FromSqlRaw (FOR UPDATE) không hoạt động với
/// InMemory DB. Các test ApproveMergeAsync chỉ test validation logic ở tầng service
/// (permission check, request lookup) bằng cách giả lập các bước pre-Approve.
/// Các test happy path cho ApproveMergeAsync cần chạy trên integration test thực.
/// </summary>
public class LobbyMergeServiceTests : IDisposable
{
    private readonly Mock<ILobbyRepository> _lobbyRepo = new();
    private readonly Mock<ILobbyMemberRepository> _memberRepo = new();
    private readonly Mock<ICafeRepository> _cafeRepo = new();
    private readonly Mock<IUserManagementRepository> _userRepo = new();
    private readonly Mock<IWalletRepository> _walletRepo = new();
    private readonly Mock<IBookingDepositRepository> _depositRepo = new();
    private readonly Mock<ISeatInventoryRepository> _seatRepo = new();
    private readonly Mock<IHttpContextAccessor> _httpCtx = new();
    private readonly Mock<ISystemConfigurationProvider> _configProvider = new();
    private readonly Mock<ILogger<LobbyMergeService>> _logger = new();
    private readonly Mock<ILobbyHubService> _hubService = new();
    private readonly Mock<ILobbyInviteRepository> _inviteRepo = new();

    // In-memory DB cho các test dùng DB persistence (G22 idempotency)
    private BoardVerseDbContext _db;

    public LobbyMergeServiceTests()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new BoardVerseDbContext(options);
    }

    // MỖI TEST tự tạo _db riêng để tránh cross-test pollution khi tests chạy song song
    private BoardVerseDbContext CreateDb()
    {
        // Dispose old _db first so its in-memory store is fully released
        _db?.Dispose();
        return new BoardVerseDbContext(
            new DbContextOptionsBuilder<BoardVerseDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
    }

    public void Dispose()
    {
        _db?.Dispose();
    }

    private LobbyMergeService CreateService() => new(
        _db,
        _lobbyRepo.Object,
        _memberRepo.Object,
        _cafeRepo.Object,
        _userRepo.Object,
        _walletRepo.Object,
        _depositRepo.Object,
        _seatRepo.Object,
        _httpCtx.Object,
        _configProvider.Object,
        _logger.Object,
        _hubService.Object,
        _inviteRepo.Object);

    /// <summary>
    /// Tạo service mới với DbContext ĐÃ CLEAR (đã có data từ test setup).
    /// Dùng cho tests cần query data vừa add vào _db.
    /// </summary>
    private LobbyMergeService CreateFreshService()
    {
        _db.ChangeTracker.Clear();
        return CreateService();
    }

    private static Cafe BuildCafe(Guid id) => new()
    {
        Id = id,
        Name = "Test Cafe",
        Address = "123 Test Street",
        IsActive = true
    };

    private static Lobby BuildLobby(
        Guid id,
        LobbyStatus status,
        Guid? cafeId = null,
        Guid? reservationId = null)
    {
        var hostUserId = Guid.NewGuid();
        var gameTemplateId = Guid.NewGuid();
        var lobby = new Lobby
        {
            Id = id,
            Status = status,
            CafeId = cafeId ?? Guid.NewGuid(),
            ReservationId = reservationId ?? Guid.NewGuid(), // non-null: CreateMergeRequestAsync accesses this
            HostUserId = hostUserId,
            GameTemplateId = gameTemplateId,
            // Required navigation properties (must be non-null for in-memory DB SaveChanges)
            HostUser = new User { Id = hostUserId, Username = "host", Email = "host@test.com", PasswordHash = "x" },
            GameTemplate = new GameTemplate { Id = gameTemplateId, Name = "Test Game" },
            Members = new List<LobbyMember>()
        };
        return lobby;
    }

    private static LobbyMember BuildMember(
        Guid userId,
        bool isActive = true,
        LobbyMemberStatus status = LobbyMemberStatus.Ready,
        Guid lobbyId = default)
    {
        return new LobbyMember
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            LobbyId = lobbyId,
            IsActive = isActive,
            Status = status,
            IsHost = false
        };
    }

    // =====================================================================
    // CreateMergeRequestAsync
    // =====================================================================

    [Fact]
    public async Task CreateMergeRequestAsync_WhenCafeNotFound_ThrowsNotFoundException()
    {
        _db = CreateDb();
        var svc = CreateService();
        var cafeId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cafe?)null);

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = Guid.NewGuid(),
            TargetLobbyId = Guid.NewGuid()
        };

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.CreateMergeRequestAsync(cafeId, Guid.NewGuid(), dto));
    }

    [Fact]
    public async Task CreateMergeRequestAsync_WhenStaffNotAuthorized_ThrowsForbiddenException()
    {
        _db = CreateDb();
        var svc = CreateService();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = Guid.NewGuid(),
            TargetLobbyId = Guid.NewGuid()
        };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.CreateMergeRequestAsync(cafeId, staffId, dto));
    }

    [Fact]
    public async Task CreateMergeRequestAsync_WhenSourceLobbyNotFound_ThrowsNotFoundException()
    {
        _db = CreateDb();
        var svc = CreateService();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _lobbyRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Lobby?)null);

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = Guid.NewGuid(),
            TargetLobbyId = Guid.NewGuid()
        };

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.CreateMergeRequestAsync(cafeId, staffId, dto));
    }

    [Fact]
    public async Task CreateMergeRequestAsync_WhenTargetLobbyNotInProgress_ThrowsConflictException()
    {
        _db = CreateDb();
        var svc = CreateService();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(sourceId, LobbyStatus.Open));
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(targetId, LobbyStatus.Open)); // Not InProgress

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId
        };

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateMergeRequestAsync(cafeId, staffId, dto));
    }

    // =====================================================================
    // Gap #1: Không cho gộp chính mình
    // =====================================================================

    [Fact]
    public async Task CreateMergeRequestAsync_WhenSameLobbyId_ThrowsConflictException()
    {
        _db = CreateDb();
        var svc = CreateService();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var lobby = BuildLobby(lobbyId, LobbyStatus.InProgress, cafeId);
        _lobbyRepo.Setup(r => r.GetByIdAsync(lobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lobby);

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = lobbyId,
            TargetLobbyId = lobbyId   // Same ID — Gap #1
        };

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateMergeRequestAsync(cafeId, staffId, dto));
        Assert.Contains("chính nó", ex.Message);
    }

    // =====================================================================
    // Gap #2: Lobby nguồn phải ở trạng thái hợp lệ để merge
    // =====================================================================

    [Theory]
    [InlineData(LobbyStatus.Closed)]
    [InlineData(LobbyStatus.TimeoutFailed)]
    [InlineData(LobbyStatus.HostCancelled)]
    [InlineData(LobbyStatus.RejectedByCafe)]
    [InlineData(LobbyStatus.ExpiredByCafe)]
    public async Task CreateMergeRequestAsync_WhenSourceLobbyInvalidStatus_ThrowsConflictException(
        LobbyStatus invalidStatus)
    {
        _db = CreateDb();
        var svc = CreateService();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(sourceId, invalidStatus, cafeId));   // Gap #2: invalid source status
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(targetId, LobbyStatus.InProgress, cafeId));

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId
        };

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateMergeRequestAsync(cafeId, staffId, dto));
        Assert.Contains("không ở trạng thái hợp lệ", ex.Message);
    }

    [Fact]
    public async Task CreateMergeRequestAsync_WhenExistingPendingRequest_ThrowsConflictException()
    {
        _db = CreateDb();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(sourceId, LobbyStatus.InProgress, cafeId));
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(targetId, LobbyStatus.InProgress, cafeId));

        // Simulate existing pending request in DB
        _db.LobbyMergeRequests.Add(new LobbyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId,
            RequestedByUserId = Guid.NewGuid(),
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        var svc = CreateService();

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId
        };

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateMergeRequestAsync(cafeId, staffId, dto));
    }

    [Fact]
    public async Task CreateMergeRequestAsync_WithIdempotencyKey_ReturnsExistingRequest()
    {
        _db = CreateDb();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var idempotencyKey = "MERGE-TEST-KEY-001";
        var existingId = Guid.NewGuid();

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(sourceId, LobbyStatus.InProgress, cafeId));
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(targetId, LobbyStatus.InProgress, cafeId));

        // Simulate existing request with same idempotency key
        _db.LobbyMergeRequests.Add(new LobbyMergeRequest
        {
            Id = existingId,
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId,
            RequestedByUserId = staffId,
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        var svc = CreateService();

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId,
            IdempotencyKey = idempotencyKey
        };

        var result = await svc.CreateMergeRequestAsync(cafeId, staffId, dto);

        Assert.Equal(existingId, result.Id);
        Assert.Equal(idempotencyKey, result.IdempotencyKey);
    }

    // =====================================================================
    // Cross-game merge (BR Exception 4 — boardverse-business-context.mdc)
    // Gap 4 fix 2026-09-29:
    //   - Chỉ BLOCK khi source lobby còn box game InUse (game đang chơi trên bàn).
    //   - Cho phép cross-game merge khi game Nguồn đã trả về quán (box Available/Maintenance).
    // =====================================================================

    /// <summary>
    /// BLOCK: source đang chơi game khác (box InUse) — staff phải EndGame + ComponentCheck trước.
    /// </summary>
    [Fact]
    public async Task CreateMergeRequestAsync_CrossGame_WithInUseBox_ThrowsMergeDifferentGames()
    {
        _db = CreateDb();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var sourceGameId = Guid.NewGuid();
        var targetGameId = Guid.NewGuid();
        var sourceLobby = BuildLobby(sourceId, LobbyStatus.InProgress, cafeId);
        sourceLobby.GameTemplateId = sourceGameId;
        var targetLobby = BuildLobby(targetId, LobbyStatus.InProgress, cafeId);
        targetLobby.GameTemplateId = targetGameId;

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceLobby);
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetLobby);

        // Seed: source lobby có ActiveSession với 1 game box InUse (chưa trả)
        var sourceSession = new ActiveSession
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            HostId = Guid.NewGuid(),
            LobbyId = sourceId,
            GameTemplateId = sourceGameId,
            Status = GroupSessionStatus.Active,
            StartedAt = DateTime.UtcNow.AddHours(-1)
        };
        var inUseBox = new CafeInventoryBox
        {
            Id = Guid.NewGuid(),
            CafeGameInventoryId = Guid.NewGuid(),
            Barcode = "TEST-INUSE-001",
            Status = CafeGameInventoryStatus.InUse
        };
        var inUseGame = new ActiveSessionGame
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = sourceSession.Id,
            ActiveSession = sourceSession,
            CafeInventoryBoxId = inUseBox.Id,
            CafeInventoryBox = inUseBox,
            GameTemplateId = sourceGameId,
            AttachedAt = DateTime.UtcNow.AddHours(-1)
        };
        sourceSession.Games = new List<ActiveSessionGame> { inUseGame };
        _db.ActiveSessions.Add(sourceSession);
        _db.CafeInventoryBoxes.Add(inUseBox);
        _db.ActiveSessionGames.Add(inUseGame);
        await _db.SaveChangesAsync();

        var svc = CreateService();

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId
        };

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.CreateMergeRequestAsync(cafeId, staffId, dto));
        Assert.Contains("game khác nhau", ex.Message);
    }

    /// <summary>
    /// ALLOW (BR Exception 4): source đã trả game về quán (box Available) → cross-game merge OK.
    /// A3 rời Nhóm A (Catan đã trả) → merge Nhóm B (Splendor).
    /// </summary>
    [Fact]
    public async Task CreateMergeRequestAsync_CrossGame_NoInUseBox_AllowsMerge()
    {
        _db = CreateDb();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var sourceGameId = Guid.NewGuid();
        var targetGameId = Guid.NewGuid();
        var sourceLobby = BuildLobby(sourceId, LobbyStatus.InProgress, cafeId);
        sourceLobby.GameTemplateId = sourceGameId;
        var targetLobby = BuildLobby(targetId, LobbyStatus.InProgress, cafeId);
        targetLobby.GameTemplateId = targetGameId;

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceLobby);
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetLobby);
        _memberRepo.Setup(r => r.GetByLobbyAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember> { BuildMember(Guid.NewGuid(), lobbyId: sourceId) });
        _memberRepo.Setup(r => r.GetByLobbyAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember>());

        // Seed: source lobby có ActiveSession với game box Available (đã trả về quán)
        var sourceSession = new ActiveSession
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            HostId = Guid.NewGuid(),
            LobbyId = sourceId,
            GameTemplateId = sourceGameId,
            Status = GroupSessionStatus.Checking, // hoặc Active — quan trọng là box đã trả
            StartedAt = DateTime.UtcNow.AddHours(-1)
        };
        var returnedBox = new CafeInventoryBox
        {
            Id = Guid.NewGuid(),
            CafeGameInventoryId = Guid.NewGuid(),
            Barcode = "TEST-RETURNED-001",
            Status = CafeGameInventoryStatus.Available // game đã trả về quán
        };
        var returnedGame = new ActiveSessionGame
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = sourceSession.Id,
            ActiveSession = sourceSession,
            CafeInventoryBoxId = returnedBox.Id,
            CafeInventoryBox = returnedBox,
            GameTemplateId = sourceGameId,
            AttachedAt = DateTime.UtcNow.AddHours(-1),
            CheckStatus = ComponentCheckStatus.Verified
        };
        sourceSession.Games = new List<ActiveSessionGame> { returnedGame };
        _db.ActiveSessions.Add(sourceSession);
        _db.CafeInventoryBoxes.Add(returnedBox);
        _db.ActiveSessionGames.Add(returnedGame);
        await _db.SaveChangesAsync();

        var svc = CreateService();

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId,
            Reason = "A3 đã trả game Catan, muốn nhập nhóm B chơi Splendor"
        };

        // Không throw — cross-game merge vẫn thành công vì box đã trả.
        var result = await svc.CreateMergeRequestAsync(cafeId, staffId, dto);

        Assert.Equal(LobbyMergeRequestStatus.Pending, result.Status);
        Assert.Equal(sourceId, result.SourceLobbyId);
        Assert.Equal(targetId, result.TargetLobbyId);
        Assert.NotEqual(sourceGameId, targetGameId); // sanity: games thực sự khác nhau
    }

    /// <summary>
    /// ALLOW: cùng game — happy path cũ, regression test.
    /// </summary>
    [Fact]
    public async Task CreateMergeRequestAsync_SameGame_AllowsMerge()
    {
        _db = CreateDb();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var sameGameId = Guid.NewGuid();
        var sourceLobby = BuildLobby(sourceId, LobbyStatus.InProgress, cafeId);
        sourceLobby.GameTemplateId = sameGameId;
        var targetLobby = BuildLobby(targetId, LobbyStatus.InProgress, cafeId);
        targetLobby.GameTemplateId = sameGameId;

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceLobby);
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetLobby);
        _memberRepo.Setup(r => r.GetByLobbyAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember> { BuildMember(Guid.NewGuid(), lobbyId: sourceId) });
        _memberRepo.Setup(r => r.GetByLobbyAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember>());

        var svc = CreateService();

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId
        };

        var result = await svc.CreateMergeRequestAsync(cafeId, staffId, dto);

        Assert.Equal(LobbyMergeRequestStatus.Pending, result.Status);
    }

    [Fact]
    public async Task CreateMergeRequestAsync_HappyPath_CreatesRequestWithExpiry()
    {
        _db = CreateDb();
        var svc = CreateService();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var sourceLobby = BuildLobby(sourceId, LobbyStatus.InProgress, cafeId);
        var targetLobby = BuildLobby(targetId, LobbyStatus.InProgress, cafeId);
        sourceLobby.Members.Add(BuildMember(Guid.NewGuid(), lobbyId: sourceId));

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceLobby);
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetLobby);
        _memberRepo.Setup(r => r.GetByLobbyAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember> { BuildMember(Guid.NewGuid(), lobbyId: sourceId) });
        _memberRepo.Setup(r => r.GetByLobbyAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember>());   // target lobby empty for happy path

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId,
            Reason = "Player wants to join another group"
        };

        var result = await svc.CreateMergeRequestAsync(cafeId, staffId, dto);

        Assert.Equal(LobbyMergeRequestStatus.Pending, result.Status);
        Assert.Equal(sourceId, result.SourceLobbyId);
        Assert.Equal(targetId, result.TargetLobbyId);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
        Assert.True(result.ExpiresAt <= DateTime.UtcNow.AddMinutes(16));
        Assert.Equal(1, result.SourceMembersCount);

        // Verify audit log was created with reservation IDs (Gap #3)
        var audit = await _db.LobbyMergeAuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Equal("MergeRequested", audit.Action);
        Assert.Equal(sourceLobby.ReservationId, audit.SourceReservationId);  // Gap #3
        Assert.Equal(targetLobby.ReservationId, audit.TargetReservationId); // Gap #3
    }

    // =====================================================================
    // Gap #5 Fix (2026-09-29): Walk-in lobby merge — đếm member từ ActiveSessionMembers
    // chứ không phải LobbyMembers (vì walk-in lobbies không có LobbyMembers rows).
    // =====================================================================

    [Fact]
    public async Task CreateMergeRequestAsync_WhenWalkInSource_CountsMembersFromActiveSession()
    {
        // Setup: walk-in source lobby (ReservationId = null) với 2 ActiveSessionMembers:
        //   - 1 Playing (active, transferable)
        //   - 1 Finished (không transferable, không hiển thị trong active count)
        _db = CreateDb();
        var svc = CreateService();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var sourceSessionId = Guid.NewGuid();
        var targetSessionId = Guid.NewGuid();

        // Walk-in source lobby (không có ReservationId) — BuildLobby mặc định sinh random GUID
        // cho ReservationId (vì nhiều test khác cần non-null), nên phải force null ở đây.
        var sourceLobby = BuildLobby(sourceId, LobbyStatus.InProgress, cafeId, reservationId: null);
        sourceLobby.ReservationId = null;
        sourceLobby.ActiveSessionId = sourceSessionId;
        var sourceSession = new ActiveSession
        {
            Id = sourceSessionId,
            LobbyId = sourceId,
            CafeId = cafeId,
            HostId = staffId,
            Status = GroupSessionStatus.Active,
            StartedAt = DateTime.UtcNow.AddMinutes(-30),
            GameTemplateId = sourceLobby.GameTemplateId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        _db.ActiveSessions.Add(sourceSession);
        _db.ActiveSessionMembers.Add(new ActiveSessionMember
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = sourceSessionId,
            UserId = null, // Walk-in guest: UserId = null
            IsGuestSlot = true,
            GuestDisplayName = "trinh",
            Status = IndividualSessionStatus.Playing,
            JoinedAt = DateTime.UtcNow.AddMinutes(-30)
        });
        _db.ActiveSessionMembers.Add(new ActiveSessionMember
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = sourceSessionId,
            UserId = null,
            IsGuestSlot = true,
            GuestDisplayName = "trinh2-finished",
            Status = IndividualSessionStatus.Finished,    // Đã về — KHÔNG transferable
            JoinedAt = DateTime.UtcNow.AddMinutes(-30),
            LeftAt = DateTime.UtcNow.AddMinutes(-5)
        });
        await _db.SaveChangesAsync();

        // Walk-in target lobby (không có ReservationId) — BuildLobby mặc định sinh random GUID,
        // nên force null ở đây để test đúng walk-in scenario.
        var targetLobby = BuildLobby(targetId, LobbyStatus.InProgress, cafeId, reservationId: null);
        targetLobby.ReservationId = null;
        targetLobby.ActiveSessionId = targetSessionId;
        _db.ActiveSessions.Add(new ActiveSession
        {
            Id = targetSessionId,
            LobbyId = targetId,
            CafeId = cafeId,
            HostId = staffId,
            Status = GroupSessionStatus.Active,
            StartedAt = DateTime.UtcNow.AddMinutes(-15),
            GameTemplateId = targetLobby.GameTemplateId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-15)
        });
        await _db.SaveChangesAsync();

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceLobby);
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetLobby);

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId,
            Reason = "walk-in test"
        };

        var result = await svc.CreateMergeRequestAsync(cafeId, staffId, dto);

        // Assert: sourceMembersCount = 2 (cả Playing + Finished đều đếm),
        //         sourceActiveMembersAtRequest = 1 (chỉ Playing mới transferable)
        Assert.Equal(LobbyMergeRequestStatus.Pending, result.Status);
        Assert.Equal(2, result.SourceMembersCount);          // Tổng trong source session
        Assert.Equal(1, result.SourceActiveMembersAtRequest); // Playing only
        Assert.Equal(1, result.CombinedCount);                // target=0 + source=1
    }

    [Fact]
    public async Task CreateMergeRequestAsync_WhenWalkInSourceNoPlayingMembers_StillCreatesRequest()
    {
        // Edge case: walk-in source lobby không có Playing member nào
        // → SourceActiveMembersAtRequest = 0 nhưng vẫn cho tạo request (staff có thể
        //   cố tình merge để dissolve empty source).
        // Approve sẽ throw ConflictException để staff biết.
        _db = CreateDb();
        var svc = CreateService();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var sourceSessionId = Guid.NewGuid();

        // BuildLobby mặc định sinh random GUID cho ReservationId (vì nhiều test khác cần non-null)
        // — phải force null cho walk-in scenario.
        var sourceLobby = BuildLobby(sourceId, LobbyStatus.InProgress, cafeId, reservationId: null);
        sourceLobby.ReservationId = null;
        sourceLobby.ActiveSessionId = sourceSessionId;
        _db.ActiveSessions.Add(new ActiveSession
        {
            Id = sourceSessionId,
            LobbyId = sourceId,
            CafeId = cafeId,
            HostId = staffId,
            Status = GroupSessionStatus.Active,
            StartedAt = DateTime.UtcNow.AddMinutes(-30),
            GameTemplateId = sourceLobby.GameTemplateId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        });
        // Không thêm ActiveSessionMembers → 0 transferable
        await _db.SaveChangesAsync();

        var targetLobby = BuildLobby(targetId, LobbyStatus.InProgress, cafeId, reservationId: null);
        targetLobby.ReservationId = null;
        _db.ActiveSessions.Add(new ActiveSession
        {
            Id = Guid.NewGuid(),
            LobbyId = targetId,
            CafeId = cafeId,
            HostId = staffId,
            Status = GroupSessionStatus.Active,
            StartedAt = DateTime.UtcNow.AddMinutes(-15),
            GameTemplateId = targetLobby.GameTemplateId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-15)
        });
        await _db.SaveChangesAsync();

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceLobby);
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetLobby);

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId
        };

        var result = await svc.CreateMergeRequestAsync(cafeId, staffId, dto);

        Assert.Equal(LobbyMergeRequestStatus.Pending, result.Status);
        Assert.Equal(0, result.SourceActiveMembersAtRequest);
    }

    // =====================================================================
    // Bug fix (2026-09-29): Walk-in source lobby có `lobby.ActiveSessionId == null`
    // nhưng `ActiveSession.LobbyId == sourceLobby.Id` (set bởi
    // ReservationService.CheckInAsync cho online flow qua POS — KHÔNG set
    // lobby.ActiveSessionId). Trước fix này service query qua lobby.ActiveSessionId
    // → trả 0 → merge "thành công" nhưng membersTransferred = 0, members vẫn ở bàn cũ.
    // Sau fix: query reverse-FK ActiveSession.LobbyId == sourceLobby.Id (đáng tin cậy).
    // =====================================================================

    [Fact]
    public async Task CreateMergeRequestAsync_WhenWalkInSourceActiveSessionIdIsNull_StillFindsMembersViaReverseFK()
    {
        // Reproduce user bug (2026-09-29):
        // - Source lobby (walk-in hoặc online check-in qua POS) có `ActiveSessionId = null`.
        // - ActiveSession có `LobbyId = sourceLobby.Id` (FK đáng tin cậy — set khi session tạo).
        // - ActiveSession có members Playing.
        // Expected: sourceMembersCount > 0, sourceActiveMembersAtRequest > 0.
        _db = CreateDb();
        var svc = CreateService();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var sourceSessionId = Guid.NewGuid();
        var targetSessionId = Guid.NewGuid();

        // Source lobby — walk-in (ReservationId = null) AND ActiveSessionId = null.
        // Đây là data shape xảy ra khi online lobby check-in qua POS:
        // - CafePosService.StartSessionFromReservationAsync tạo ActiveSession với `LobbyId = lobby.Id`
        // - NHƯNG KHÔNG set `lobby.ActiveSessionId = session.Id`
        // - ReservationService.CheckInAsync chỉ set `lobby.Status = InProgress`.
        var sourceLobby = BuildLobby(sourceId, LobbyStatus.InProgress, cafeId, reservationId: null);
        sourceLobby.ReservationId = null;
        sourceLobby.ActiveSessionId = null;  // ← KEY: null như online flow qua POS
        _db.ActiveSessions.Add(new ActiveSession
        {
            Id = sourceSessionId,
            LobbyId = sourceId,                // ← KEY: reverse-FK được set
            CafeId = cafeId,
            HostId = staffId,
            Status = GroupSessionStatus.Active,
            StartedAt = DateTime.UtcNow.AddMinutes(-30),
            GameTemplateId = sourceLobby.GameTemplateId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        });
        // 3 Playing members
        _db.ActiveSessionMembers.Add(new ActiveSessionMember
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = sourceSessionId,
            UserId = Guid.NewGuid(),
            IsGuestSlot = false,
            Status = IndividualSessionStatus.Playing,
            JoinedAt = DateTime.UtcNow.AddMinutes(-30)
        });
        _db.ActiveSessionMembers.Add(new ActiveSessionMember
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = sourceSessionId,
            UserId = Guid.NewGuid(),
            IsGuestSlot = false,
            Status = IndividualSessionStatus.Playing,
            JoinedAt = DateTime.UtcNow.AddMinutes(-28)
        });
        _db.ActiveSessionMembers.Add(new ActiveSessionMember
        {
            Id = Guid.NewGuid(),
            ActiveSessionId = sourceSessionId,
            UserId = null,                     // Walk-in guest slot
            IsGuestSlot = true,
            GuestDisplayName = "Guest-1",
            Status = IndividualSessionStatus.Playing,
            JoinedAt = DateTime.UtcNow.AddMinutes(-25)
        });
        await _db.SaveChangesAsync();

        // Target lobby (walk-in, có session Active để merge vào)
        var targetLobby = BuildLobby(targetId, LobbyStatus.InProgress, cafeId, reservationId: null);
        targetLobby.ReservationId = null;
        targetLobby.ActiveSessionId = targetSessionId;
        _db.ActiveSessions.Add(new ActiveSession
        {
            Id = targetSessionId,
            LobbyId = targetId,
            CafeId = cafeId,
            HostId = staffId,
            Status = GroupSessionStatus.Active,
            StartedAt = DateTime.UtcNow.AddMinutes(-15),
            GameTemplateId = targetLobby.GameTemplateId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-15)
        });
        await _db.SaveChangesAsync();

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceLobby);
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetLobby);

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId,
            Reason = "online check-in merge test"
        };

        var result = await svc.CreateMergeRequestAsync(cafeId, staffId, dto);

        // Assert: KHÔNG bị "0 members" do lobby.ActiveSessionId = null
        Assert.Equal(LobbyMergeRequestStatus.Pending, result.Status);
        Assert.Equal(3, result.SourceMembersCount);
        Assert.Equal(3, result.SourceActiveMembersAtRequest);  // Cả 3 đều Playing
    }

    [Fact]
    public async Task CreateMergeRequestAsync_WhenOnlineSourceCheckedInViaPos_StillCountsReadyMembers()
    {
        // Test parallel với walk-in case: online lobby (có ReservationId) check-in qua POS.
        // LobbyMember rows có Status = Ready → count bình thường qua LobbyMember table.
        // (Bug này không ảnh hưởng online flow vì code dùng LobbyMember trước.)
        // Test này đảm bảo fix không break online happy path.
        _db = CreateDb();
        var svc = CreateService();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var sourceSessionId = Guid.NewGuid();
        var targetSessionId = Guid.NewGuid();

        // Online source lobby (có ReservationId) — ActiveSessionId = null (POS check-in)
        var sourceLobby = BuildLobby(sourceId, LobbyStatus.InProgress, cafeId);
        sourceLobby.ActiveSessionId = null;
        // Online lobby có ActiveSession liên kết nhưng lobby.ActiveSessionId chưa được update.
        _db.ActiveSessions.Add(new ActiveSession
        {
            Id = sourceSessionId,
            LobbyId = sourceId,
            CafeId = cafeId,
            HostId = staffId,
            Status = GroupSessionStatus.Active,
            StartedAt = DateTime.UtcNow.AddMinutes(-30),
            GameTemplateId = sourceLobby.GameTemplateId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        });
        await _db.SaveChangesAsync();

        var targetLobby = BuildLobby(targetId, LobbyStatus.InProgress, cafeId);
        targetLobby.ActiveSessionId = targetSessionId;
        _db.ActiveSessions.Add(new ActiveSession
        {
            Id = targetSessionId,
            LobbyId = targetId,
            CafeId = cafeId,
            HostId = staffId,
            Status = GroupSessionStatus.Active,
            StartedAt = DateTime.UtcNow.AddMinutes(-15),
            GameTemplateId = targetLobby.GameTemplateId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-15)
        });
        await _db.SaveChangesAsync();

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceLobby);
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetLobby);
        // Source có 2 LobbyMember sẵn sàng (mimic check-in ready)
        _memberRepo.Setup(r => r.GetByLobbyAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember>
            {
                BuildMember(Guid.NewGuid(), status: LobbyMemberStatus.Ready, lobbyId: sourceId),
                BuildMember(Guid.NewGuid(), status: LobbyMemberStatus.Ready, lobbyId: sourceId)
            });
        _memberRepo.Setup(r => r.GetByLobbyAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember>());

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId
        };

        var result = await svc.CreateMergeRequestAsync(cafeId, staffId, dto);

        Assert.Equal(LobbyMergeRequestStatus.Pending, result.Status);
        Assert.Equal(2, result.SourceMembersCount);
        Assert.Equal(2, result.SourceActiveMembersAtRequest);
    }


    // =====================================================================
    // ApproveMergeAsync — pre-approval validation tests
    // (FROM SQL không hoạt động với InMemory DB nên test ở tầng permission + lookup)
    // =====================================================================

    [Fact]
    public async Task ApproveMergeAsync_WhenStaffNotAuthorized_ThrowsForbiddenException()
    {
        _db = CreateDb();
        var svc = CreateService();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.ApproveMergeAsync(cafeId, staffId, Guid.NewGuid()));
    }

    [Fact]
    public async Task ApproveMergeAsync_WhenRequestNotFound_ThrowsNotFoundException()
    {
        _db = CreateDb();
        // Dùng TestableLobbyMergeService để bypass FromSqlRaw (không hoạt động với InMemory DB)
        var svc = CreateTestableService();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.ApproveMergeAsync(cafeId, staffId, Guid.NewGuid()));
    }

    [Fact]
    public async Task ApproveMergeAsync_WhenRequestNotPending_ThrowsConflictException()
    {
        _db = CreateDb();
        // Dùng TestableLobbyMergeService để bypass FromSqlRaw (không hoạt động với InMemory DB)
        var svc = CreateTestableService();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sourceLobby = BuildLobby(sourceId, LobbyStatus.InProgress, cafeId);
        var targetLobby = BuildLobby(targetId, LobbyStatus.InProgress, cafeId);
        _db.Lobbies.AddRange(sourceLobby, targetLobby);

        _db.LobbyMergeRequests.Add(new LobbyMergeRequest
        {
            Id = requestId,
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId,
            SourceLobby = sourceLobby,
            TargetLobby = targetLobby,
            RequestedByUserId = Guid.NewGuid(),
            Status = LobbyMergeRequestStatus.Approved, // Not Pending
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.ApproveMergeAsync(cafeId, staffId, requestId));
    }

    // =====================================================================
    // RejectMergeAsync
    // =====================================================================

    [Fact]
    public async Task RejectMergeAsync_WhenStaffNotAuthorized_ThrowsForbiddenException()
    {
        _db = CreateDb();
        var svc = CreateService();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.RejectMergeAsync(cafeId, staffId, Guid.NewGuid(),
                new ReviewLobbyMergeRequestDto()));
    }

    [Fact]
    public async Task RejectMergeAsync_WhenRequestNotFound_ThrowsNotFoundException()
    {
        _db = CreateDb();
        var svc = CreateService();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.RejectMergeAsync(cafeId, staffId, Guid.NewGuid(),
                new ReviewLobbyMergeRequestDto()));
    }

    [Fact]
    public async Task RejectMergeAsync_WhenRequestNotPending_ThrowsConflictException()
    {
        // Use constructor's _db (never reassign) so service captures same context as test data
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();

        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sourceLobby = BuildLobby(sourceId, LobbyStatus.InProgress, cafeId);
        var targetLobby = BuildLobby(targetId, LobbyStatus.InProgress, cafeId);
        _db.Lobbies.AddRange(sourceLobby, targetLobby);

        var requestedByUser = new User { Id = requestedByUserId, Username = "staff", Email = "staff@test.com" };

        _db.LobbyMergeRequests.Add(new LobbyMergeRequest
        {
            Id = requestId,
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId,
            SourceLobby = sourceLobby,
            TargetLobby = targetLobby,
            RequestedByUserId = requestedByUserId,
            RequestedByUser = requestedByUser,
            Status = LobbyMergeRequestStatus.Approved, // Not Pending
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.RejectMergeAsync(cafeId, staffId, requestId,
                new ReviewLobbyMergeRequestDto { ReviewNote = "Too busy" }));
    }

    [Fact]
    public async Task RejectMergeAsync_HappyPath_UpdatesStatusToRejected()
    {
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();

        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sourceLobby = BuildLobby(sourceId, LobbyStatus.InProgress, cafeId);
        var targetLobby = BuildLobby(targetId, LobbyStatus.InProgress, cafeId);
        _db.Lobbies.AddRange(sourceLobby, targetLobby);

        var requestedByUser = new User { Id = requestedByUserId, Username = "staff", Email = "staff@test.com" };

        _db.LobbyMergeRequests.Add(new LobbyMergeRequest
        {
            Id = requestId,
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId,
            SourceLobby = sourceLobby,
            TargetLobby = targetLobby,
            RequestedByUserId = requestedByUserId,
            RequestedByUser = requestedByUser,
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var svc = CreateService();

        var result = await svc.RejectMergeAsync(cafeId, staffId, requestId,
            new ReviewLobbyMergeRequestDto { ReviewNote = "Player changed mind" });

        Assert.Equal(LobbyMergeRequestStatus.Rejected, result.Status);
        Assert.Equal("Player changed mind", result.ReviewNote);
        Assert.NotNull(result.ReviewedAt);
        Assert.Equal(staffId, result.ReviewedByUserId);

        // Verify audit log
        var audit = await _db.LobbyMergeAuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Equal("MergeRejected", audit.Action);
    }

    // =====================================================================
    // CancelMergeRequestAsync
    // =====================================================================

    [Fact]
    public async Task CancelMergeRequestAsync_WhenRequestNotFound_ThrowsNotFoundException()
    {
        var svc = CreateService();
        var requestId = Guid.NewGuid();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.CancelMergeRequestAsync(Guid.NewGuid(), Guid.NewGuid(), requestId));
    }

    [Fact]
    public async Task CancelMergeRequestAsync_WhenRequestNotPending_ThrowsConflictException()
    {
        var requestId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();

        var sourceLobby = BuildLobby(sourceId, LobbyStatus.InProgress, cafeId);
        var targetLobby = BuildLobby(targetId, LobbyStatus.InProgress, cafeId);
        _db.Lobbies.AddRange(sourceLobby, targetLobby);

        var requestedByUser = new User { Id = requestedByUserId, Username = "staff", Email = "staff@test.com" };

        _db.LobbyMergeRequests.Add(new LobbyMergeRequest
        {
            Id = requestId,
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId,
            SourceLobby = sourceLobby,
            TargetLobby = targetLobby,
            RequestedByUserId = requestedByUserId,
            RequestedByUser = requestedByUser,
            Status = LobbyMergeRequestStatus.Approved, // Not Pending
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CancelMergeRequestAsync(Guid.NewGuid(), Guid.NewGuid(), requestId));
    }

    [Fact]
    public async Task CancelMergeRequestAsync_HappyPath_UpdatesStatusToCancelled()
    {
        var requestId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();

        var sourceLobby = BuildLobby(sourceId, LobbyStatus.InProgress, cafeId);
        var targetLobby = BuildLobby(targetId, LobbyStatus.InProgress, cafeId);
        _db.Lobbies.AddRange(sourceLobby, targetLobby);

        var requestedByUser = new User { Id = requestedByUserId, Username = "staff", Email = "staff@test.com" };

        _db.LobbyMergeRequests.Add(new LobbyMergeRequest
        {
            Id = requestId,
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId,
            SourceLobby = sourceLobby,
            TargetLobby = targetLobby,
            RequestedByUserId = requestedByUserId,
            RequestedByUser = requestedByUser,
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var svc = CreateService();

        var result = await svc.CancelMergeRequestAsync(Guid.NewGuid(), Guid.NewGuid(), requestId);

        Assert.Equal(LobbyMergeRequestStatus.Cancelled, result.Status);

        // Verify audit log
        var audit = await _db.LobbyMergeAuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Equal("MergeCancelled", audit.Action);
    }

    // =====================================================================
    // GetMergeRequestAsync
    // =====================================================================

    [Fact]
    public async Task GetMergeRequestAsync_WhenNotFound_ReturnsNull()
    {
        var svc = CreateService();
        var result = await svc.GetMergeRequestAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetMergeRequestAsync_WhenFound_ReturnsDto()
    {
        var requestId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();

        var sourceLobby = BuildLobby(sourceId, LobbyStatus.InProgress, cafeId);
        var targetLobby = BuildLobby(targetId, LobbyStatus.InProgress, cafeId);
        _db.Lobbies.AddRange(sourceLobby, targetLobby);

        var requestedByUser = new User { Id = requestedByUserId, Username = "staff", Email = "staff@test.com" };

        _db.LobbyMergeRequests.Add(new LobbyMergeRequest
        {
            Id = requestId,
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId,
            SourceLobby = sourceLobby,
            TargetLobby = targetLobby,
            RequestedByUserId = requestedByUserId,
            RequestedByUser = requestedByUser,
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var svc = CreateService();

        var result = await svc.GetMergeRequestAsync(requestId);

        Assert.NotNull(result);
        Assert.Equal(requestId, result.Id);
        Assert.Equal(sourceId, result.SourceLobbyId);
        Assert.Equal(targetId, result.TargetLobbyId);
        Assert.Equal(LobbyMergeRequestStatus.Pending, result.Status);
    }

    // =====================================================================
    // GetPendingRequestsAsync
    // =====================================================================

    [Fact]
    public async Task GetPendingRequestsAsync_ReturnsOnlyPendingForCafe()
    {
        var cafeId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();

        var sourceLobby = BuildLobby(sourceId, LobbyStatus.InProgress, cafeId);
        var targetLobby = BuildLobby(targetId, LobbyStatus.InProgress, cafeId);
        var requestedByUser = new User { Id = requestedByUserId, Username = "staff", Email = "staff@test.com" };

        _db.LobbyMergeRequests.AddRange(
            new LobbyMergeRequest
            {
                Id = Guid.NewGuid(),
                SourceLobbyId = sourceId,
                TargetLobbyId = targetId,
                RequestedByUserId = requestedByUserId,
                RequestedByUser = requestedByUser,
                Status = LobbyMergeRequestStatus.Pending,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                CreatedAt = DateTime.UtcNow,
                SourceLobby = sourceLobby,
                TargetLobby = targetLobby
            },
            new LobbyMergeRequest
            {
                Id = Guid.NewGuid(),
                SourceLobbyId = Guid.NewGuid(),
                TargetLobbyId = Guid.NewGuid(),
                RequestedByUserId = requestedByUserId,
                RequestedByUser = requestedByUser,
                Status = LobbyMergeRequestStatus.Approved, // Not pending
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                CreatedAt = DateTime.UtcNow,
                SourceLobby = BuildLobby(Guid.NewGuid(), LobbyStatus.InProgress, cafeId),
                TargetLobby = BuildLobby(Guid.NewGuid(), LobbyStatus.InProgress, cafeId)
            });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var svc = CreateService();

        var result = await svc.GetPendingRequestsAsync(cafeId);

        Assert.Single(result);
        Assert.Equal(LobbyMergeRequestStatus.Pending, result[0].Status);
    }

    // =====================================================================
    // GetLobbyMergeHistoryAsync
    // =====================================================================

    [Fact]
    public async Task GetLobbyMergeHistoryAsync_ReturnsAuditLogs()
    {
        var lobbyId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var performedByUserId = Guid.NewGuid();
        var performedByUser = new User { Id = performedByUserId, Username = "staff", Email = "staff@test.com" };

        _db.LobbyMergeAuditLogs.AddRange(
            new LobbyMergeAuditLog
            {
                Id = Guid.NewGuid(),
                MergeRequestId = requestId,
                SourceLobbyId = lobbyId,
                TargetLobbyId = Guid.NewGuid(),
                PerformedByUserId = performedByUserId,
                PerformedByUser = performedByUser,
                Action = "MergeRequested",
                Success = true,
                CreatedAt = DateTime.UtcNow
            },
            new LobbyMergeAuditLog
            {
                Id = Guid.NewGuid(),
                MergeRequestId = requestId,
                SourceLobbyId = lobbyId,
                TargetLobbyId = Guid.NewGuid(),
                PerformedByUserId = performedByUserId,
                PerformedByUser = performedByUser,
                Action = "MergeApproved",
                Success = true,
                CreatedAt = DateTime.UtcNow.AddMinutes(1)
            });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var svc = CreateService();

        var result = await svc.GetLobbyMergeHistoryAsync(lobbyId);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Action == "MergeRequested");
        Assert.Contains(result, r => r.Action == "MergeApproved");
    }

    // =====================================================================
    // ExpireOverdueRequestsAsync
    // =====================================================================

    [Fact]
    public async Task ExpireOverdueRequestsAsync_WhenNoOverdue_ReturnsZero()
    {
        var svc = CreateService();
        var result = await svc.ExpireOverdueRequestsAsync();
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ExpireOverdueRequestsAsync_ExpiresOverdueRequests()
    {
        // Create lobby entities so Include navigation properties resolve in in-memory DB
        var sourceLobby1 = BuildLobby(Guid.NewGuid(), LobbyStatus.InProgress);
        var targetLobby1 = BuildLobby(Guid.NewGuid(), LobbyStatus.InProgress);
        var sourceLobby2 = BuildLobby(Guid.NewGuid(), LobbyStatus.InProgress);
        var targetLobby2 = BuildLobby(Guid.NewGuid(), LobbyStatus.InProgress);
        var sourceLobby3 = BuildLobby(Guid.NewGuid(), LobbyStatus.InProgress);
        var targetLobby3 = BuildLobby(Guid.NewGuid(), LobbyStatus.InProgress);
        _db.Lobbies.AddRange(sourceLobby1, targetLobby1, sourceLobby2, targetLobby2, sourceLobby3, targetLobby3);

        var requestedByUserId = Guid.NewGuid();
        var requestedByUser = new User { Id = requestedByUserId, Username = "staff", Email = "staff@test.com" };

        _db.LobbyMergeRequests.AddRange(
            new LobbyMergeRequest
            {
                Id = Guid.NewGuid(),
                SourceLobbyId = sourceLobby1.Id,
                TargetLobbyId = targetLobby1.Id,
                SourceLobby = sourceLobby1,
                TargetLobby = targetLobby1,
                RequestedByUserId = requestedByUserId,
                RequestedByUser = requestedByUser,
                Status = LobbyMergeRequestStatus.Pending,
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5), // Overdue
                CreatedAt = DateTime.UtcNow.AddMinutes(-20)
            },
            new LobbyMergeRequest
            {
                Id = Guid.NewGuid(),
                SourceLobbyId = sourceLobby2.Id,
                TargetLobbyId = targetLobby2.Id,
                SourceLobby = sourceLobby2,
                TargetLobby = targetLobby2,
                RequestedByUserId = requestedByUserId,
                RequestedByUser = requestedByUser,
                Status = LobbyMergeRequestStatus.Pending,
                ExpiresAt = DateTime.UtcNow.AddMinutes(-2), // Also overdue
                CreatedAt = DateTime.UtcNow.AddMinutes(-17)
            },
            new LobbyMergeRequest
            {
                Id = Guid.NewGuid(),
                SourceLobbyId = sourceLobby3.Id,
                TargetLobbyId = targetLobby3.Id,
                SourceLobby = sourceLobby3,
                TargetLobby = targetLobby3,
                RequestedByUserId = requestedByUserId,
                RequestedByUser = requestedByUser,
                Status = LobbyMergeRequestStatus.Pending,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10), // Not yet expired
                CreatedAt = DateTime.UtcNow
            });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var svc = CreateService();

        var result = await svc.ExpireOverdueRequestsAsync();

        Assert.Equal(2, result);

        var remaining = await _db.LobbyMergeRequests
            .Include(r => r.SourceLobby)
            .Include(r => r.TargetLobby)
            .Where(r => r.Status == LobbyMergeRequestStatus.Pending)
            .ToListAsync();
        Assert.Single(remaining); // Only the non-expired one remains Pending

        var expired = await _db.LobbyMergeRequests
            .Include(r => r.SourceLobby)
            .Include(r => r.TargetLobby)
            .Where(r => r.Status == LobbyMergeRequestStatus.Expired)
            .ToListAsync();
        Assert.Equal(2, expired.Count);

        // Verify audit logs created
        var audits = await _db.LobbyMergeAuditLogs.ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => Assert.Equal("MergeExpired", a.Action));
    }

    // =====================================================================
    // TestableLobbyMergeService — overrides FromSqlRaw (không hỗ trợ InMemory DB)
    // =====================================================================

    /// <summary>
    /// Test subclass của LobbyMergeService.
    /// Override LoadMergeRequestWithLockAsync để trả về test data từ InMemory DB
    /// thay vì dùng FromSqlRaw (không hoạt động với InMemory DbContext).
    /// </summary>
    private class TestableLobbyMergeService : LobbyMergeService
    {
        private readonly BoardVerseDbContext _testDb;

        public TestableLobbyMergeService(
            BoardVerseDbContext db,
            ILobbyRepository lobbyRepository,
            ILobbyMemberRepository lobbyMemberRepository,
            ICafeRepository cafeRepository,
            IUserManagementRepository userRepository,
            IWalletRepository walletRepository,
            IBookingDepositRepository depositRepository,
            ISeatInventoryRepository seatInventoryRepository,
            IHttpContextAccessor httpContextAccessor,
            ISystemConfigurationProvider configProvider,
            ILogger<LobbyMergeService> logger,
            ILobbyHubService lobbyHubService,
            ILobbyInviteRepository lobbyInviteRepository)
            : base(
                db, lobbyRepository, lobbyMemberRepository, cafeRepository,
                userRepository, walletRepository, depositRepository, seatInventoryRepository,
                httpContextAccessor, configProvider, logger, lobbyHubService, lobbyInviteRepository)
        {
            _testDb = db;
        }

        protected override async Task<LobbyMergeRequest?> LoadMergeRequestWithLockAsync(
            Guid requestId,
            CancellationToken cancellationToken)
        {
            // Bypass FromSqlRaw: dùng LINQ thuần để load từ InMemory DB
            return await _testDb.LobbyMergeRequests
                .Include(r => r.SourceLobby)
                    .ThenInclude(l => l.Members)
                .Include(r => r.TargetLobby)
                .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        }
    }

    private TestableLobbyMergeService CreateTestableService() => new(
        _db,
        _lobbyRepo.Object,
        _memberRepo.Object,
        _cafeRepo.Object,
        _userRepo.Object,
        _walletRepo.Object,
        _depositRepo.Object,
        _seatRepo.Object,
        _httpCtx.Object,
        _configProvider.Object,
        _logger.Object,
        _hubService.Object,
        _inviteRepo.Object);
}
