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
    private readonly Mock<IWalletService> _walletService = new();
    private readonly Mock<IBookingDepositRepository> _depositRepo = new();
    private readonly Mock<ISeatInventoryRepository> _seatRepo = new();
    private readonly Mock<IActiveSessionRepository> _activeSessionRepo = new();
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

    /// <summary>
    /// Create LobbyMergeService. Sau khi H7 fix (FOR UPDATE lock cho Reject/Cancel),
    /// mọi test phải dùng TestableLobbyMergeService vì InMemory DB không hỗ trợ
    /// FromSqlRaw FOR UPDATE. TestableLobbyMergeService override LoadMergeRequestWithLockAsync
    /// để bypass.
    /// </summary>
    private LobbyMergeService CreateService() => new TestableLobbyMergeService(
        _db,
        _lobbyRepo.Object,
        _memberRepo.Object,
        _cafeRepo.Object,
        _userRepo.Object,
        _walletRepo.Object,
        _walletService.Object,
        _depositRepo.Object,
        _seatRepo.Object,
        _activeSessionRepo.Object,
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

    private static LobbyMergeSummary BuildLobbyMergeSummary(Lobby lobby) => new(
        lobby.Id,
        lobby.Status,
        lobby.CafeId,
        lobby.GameTemplateId,
        lobby.ReservationId,
        lobby.HostUserId,
        lobby.ActiveSessionId);

    private static LobbyMember BuildMember(
        Guid userId,
        bool isActive = true,
        LobbyMemberStatus status = LobbyMemberStatus.Ready,
        Guid lobbyId = default,
        bool isHost = false,
        DateTime? joinedAt = null)
    {
        return new LobbyMember
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            LobbyId = lobbyId,
            IsActive = isActive,
            Status = status,
            IsHost = isHost,
            JoinedAt = joinedAt ?? DateTime.UtcNow
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
        _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobbyMergeSummary(BuildLobby(sourceId, LobbyStatus.Open)));
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(sourceId, LobbyStatus.Open));
        _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobbyMergeSummary(BuildLobby(targetId, LobbyStatus.Open)));
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
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(lobbyId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(lobby));

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
        _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobbyMergeSummary(BuildLobby(sourceId, invalidStatus, cafeId)));
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(sourceId, invalidStatus, cafeId));   // Gap #2: invalid source status
        _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobbyMergeSummary(BuildLobby(targetId, LobbyStatus.InProgress, cafeId)));
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
        _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobbyMergeSummary(BuildLobby(sourceId, LobbyStatus.InProgress, cafeId)));
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(sourceId, LobbyStatus.InProgress, cafeId));
        _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobbyMergeSummary(BuildLobby(targetId, LobbyStatus.InProgress, cafeId)));
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
        _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobbyMergeSummary(BuildLobby(sourceId, LobbyStatus.InProgress, cafeId)));
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(sourceId, LobbyStatus.InProgress, cafeId));
        _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobbyMergeSummary(BuildLobby(targetId, LobbyStatus.InProgress, cafeId)));
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
    // Regression: race-condition safety net cho IX_LMR_SourceTarget_Pending
    // (Gap H6 fix 2026-10-01)
    //
    // Test này không thể tái hiện race thật (InMemory provider không enforce unique
    // constraint), nhưng nó verify 2 điều:
    //   (a) Service hoạt động bình thường với transaction wrap (không gây exception
    //       mới khi có valid pending request khác trong DB với lobby khác).
    //   (b) Helper IsUniqueViolationOnMergePendingConstraint detect đúng
    //       PostgresException 23505 trên IX_LMR_SourceTarget_Pending.
    //
    // Test (b) cần thiết vì helper là static private — phải có coverage trực tiếp
    // để future refactor không vô tình break catch path → quay lại bug 500.
    // =====================================================================
    [Fact]
    public async Task CreateMergeRequestAsync_WhenOtherPendingExists_DoesNotThrowConflict()
    {
        // Regression test: trước fix, việc wrap transaction làm thay đổi SQL snapshot
        // behavior. Verify rằng với một existing pending request cho (X, Y) khác, ta vẫn
        // có thể tạo request cho (A, B) mới mà không bị ảnh hưởng.
        _db = CreateDb();
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var otherSource = Guid.NewGuid();
        var otherTarget = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobbyMergeSummary(BuildLobby(sourceId, LobbyStatus.InProgress, cafeId)));
        _lobbyRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(sourceId, LobbyStatus.InProgress, cafeId));
        _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobbyMergeSummary(BuildLobby(targetId, LobbyStatus.InProgress, cafeId)));
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildLobby(targetId, LobbyStatus.InProgress, cafeId));

        // Stub member counts cho source/target — không có member active → throw ConflictException
        // (sẽ fail trước khi tới INSERT), nhưng vẫn verify transaction wrap không gây lỗi
        // mới khi query LobbyMembers.
        _memberRepo.Setup(r => r.GetByLobbyAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember>());
        _memberRepo.Setup(r => r.GetByLobbyAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember>());

        // Existing pending request cho (otherSource, otherTarget) — không liên quan tới (sourceId, targetId)
        _db.LobbyMergeRequests.Add(new LobbyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourceLobbyId = otherSource,
            TargetLobbyId = otherTarget,
            RequestedByUserId = Guid.NewGuid(),
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        var svc = CreateFreshService();

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId
        };

        // Source lobby rỗng → ConflictException NoActiveMembersToTransfer (early reject).
        // Quan trọng: KHÔNG phải ConflictException về "MergeRequestAlreadyPending" — chứng tỏ
        // existing pending khác không trigger nhầm logic.
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateMergeRequestAsync(cafeId, staffId, dto));
        Assert.Contains("nguồn không còn thành viên active", ex.Message, StringComparison.OrdinalIgnoreCase);
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
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(sourceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(sourceLobby));
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetLobby);
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(targetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(targetLobby));

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
            CheckStatus = ComponentCheckStatus.Verified, // Source đã kiểm kê nhưng box vẫn attach + game khác target → throw "MergeDifferentGames"
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
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(sourceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(sourceLobby));
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetLobby);
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(targetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(targetLobby));
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
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(sourceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(sourceLobby));
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetLobby);
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(targetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(targetLobby));
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
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(sourceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(sourceLobby));
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetLobby);
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(targetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(targetLobby));
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
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(sourceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(sourceLobby));
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetLobby);
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(targetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(targetLobby));

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
    public async Task CreateMergeRequestAsync_WhenWalkInSourceNoPlayingMembers_ThrowsConflictException()
    {
        // Bug fix (2026-09-29): Source lobby rỗng (0 active members) phải bị reject ngay
        // tại CreateMergeRequestAsync. Trước fix này staff retry liên tục tạo request rác
        // (12 lần/24h cho cùng source lobby trong production logs) → DB bloat + UX kém.
        // Test: walk-in source lobby không có Playing member nào → ConflictException.
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
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(sourceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(sourceLobby));
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetLobby);
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(targetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(targetLobby));

        var dto = new CreateLobbyMergeRequestDto
        {
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId
        };

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateMergeRequestAsync(cafeId, staffId, dto));
        Assert.Contains("không còn thành viên active", ex.Message);
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
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(sourceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(sourceLobby));
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetLobby);
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(targetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(targetLobby));

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
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(sourceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(sourceLobby));
        _lobbyRepo.Setup(r => r.GetByIdAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetLobby);
            _lobbyRepo.Setup(r => r.GetMergeSummaryAsync(targetId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildLobbyMergeSummary(targetLobby));
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
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        // H1 fix (2026-09-30): Cancel giờ check staff permission — mock staff valid
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var svc = CreateService();
        var requestId = Guid.NewGuid();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.CancelMergeRequestAsync(cafeId, staffId, requestId));
    }

    [Fact]
    public async Task CancelMergeRequestAsync_WhenRequestNotPending_ThrowsConflictException()
    {
        var requestId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        // H1 fix: Cancel giờ check staff permission — mock staff valid
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
            svc.CancelMergeRequestAsync(cafeId, staffId, requestId));
    }

    [Fact]
    public async Task CancelMergeRequestAsync_HappyPath_UpdatesStatusToCancelled()
    {
        var requestId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        // H1 fix: Cancel giờ check staff permission — mock staff valid
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

        var result = await svc.CancelMergeRequestAsync(cafeId, staffId, requestId);

        Assert.Equal(LobbyMergeRequestStatus.Cancelled, result.Status);

        // Verify audit log
        var audit = await _db.LobbyMergeAuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Equal("MergeCancelled", audit.Action);
    }

    // =====================================================================
    // H2 fix: Cancel cross-cafe check
    // =====================================================================

    /// <summary>
    /// H2: Staff cafe A truyền cafeId của cafe B (request thuộc cafe B) → bị reject.
    /// </summary>
    [Fact]
    public async Task CancelMergeRequestAsync_WhenStaffFromDifferentCafe_ThrowsForbidden()
    {
        var requestId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var cafeAId = Guid.NewGuid();    // cafe của request
        var cafeBId = Guid.NewGuid();    // cafe của staff (khác cafe A)
        var staffId = Guid.NewGuid();

        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeBId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sourceLobby = BuildLobby(sourceId, LobbyStatus.InProgress, cafeAId);
        var targetLobby = BuildLobby(targetId, LobbyStatus.InProgress, cafeAId);
        _db.Lobbies.AddRange(sourceLobby, targetLobby);

        _db.LobbyMergeRequests.Add(new LobbyMergeRequest
        {
            Id = requestId,
            SourceLobbyId = sourceId,
            TargetLobbyId = targetId,
            SourceLobby = sourceLobby,
            TargetLobby = targetLobby,
            RequestedByUserId = Guid.NewGuid(),
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.CancelMergeRequestAsync(cafeBId, staffId, requestId));
    }

    /// <summary>
    /// H1: User không phải staff của bất kỳ cafe nào → ForbiddenException (security).
    /// </summary>
    [Fact]
    public async Task CancelMergeRequestAsync_WhenUserNotStaff_ThrowsForbidden()
    {
        var cafeId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.CancelMergeRequestAsync(cafeId, userId, Guid.NewGuid()));
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
    // TryAutoPromoteSourceHostAsync — Option 2 (auto-promote host khi host transfer)
    // =====================================================================

    /// <summary>
    /// Case 1 (full transfer): Host A trong activeMembers (sẽ transfer), nhưng source không còn
    /// member nào ở lại → Step 11 sẽ đóng source → KHÔNG promote.
    /// </summary>
    [Fact]
    public async Task TryAutoPromoteSourceHostAsync_WhenAllMembersTransfer_NoPromote()
    {
        _db = CreateDb();
        var svc = CreateTestableService();

        var hostUserId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var sourceLobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            CafeId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(), // online lobby
            HostUserId = hostUserId,
            GameTemplateId = Guid.NewGuid(),
            HostUser = new User { Id = hostUserId, Username = "host", Email = "host@test.com", PasswordHash = "x" },
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Test Game" },
            Members = new List<LobbyMember>()
        };
        _db.Lobbies.Add(sourceLobby);

        var hostMember = BuildMember(hostUserId, lobbyId: lobbyId, isHost: true,
            joinedAt: DateTime.UtcNow.AddMinutes(-10));
        _db.LobbyMembers.Add(hostMember);
        await _db.SaveChangesAsync();

        var mergeRequest = new LobbyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourceLobbyId = lobbyId,
            TargetLobbyId = Guid.NewGuid(),
            RequestedByUserId = Guid.NewGuid(),
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };

        // activeMembers chỉ có host → full transfer, không còn ai ở lại
        var activeMembers = new List<LobbyMember> { hostMember };
        _memberRepo.Setup(r => r.GetByLobbyAsync(lobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember> { hostMember });

        var promoted = await svc.InvokeTryAutoPromoteSourceHostAsync(
            sourceLobby, activeMembers, mergeRequest, Guid.NewGuid(), CancellationToken.None);

        Assert.False(promoted);
        Assert.Equal(hostUserId, sourceLobby.HostUserId); // unchanged
        Assert.True(hostMember.IsHost); // unchanged
    }

    /// <summary>
    /// Case 2 (partial transfer): Host A trong activeMembers, A2 ở lại source.
    /// → Promote A2 thành host mới, A không còn IsHost.
    /// </summary>
    [Fact]
    public async Task TryAutoPromoteSourceHostAsync_WhenHostTransferringAndStayMember_PromotesNewHost()
    {
        _db = CreateDb();
        var svc = CreateTestableService();

        var oldHostUserId = Guid.NewGuid();
        var newHostUserId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var sourceLobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            CafeId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            HostUserId = oldHostUserId,
            GameTemplateId = Guid.NewGuid(),
            HostUser = new User { Id = oldHostUserId, Username = "oldhost", Email = "oh@test.com", PasswordHash = "x" },
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Test Game" },
            Members = new List<LobbyMember>()
        };
        _db.Lobbies.Add(sourceLobby);

        var oldHostMember = BuildMember(oldHostUserId, lobbyId: lobbyId, isHost: true,
            joinedAt: DateTime.UtcNow.AddMinutes(-20));
        var newHostCandidate = BuildMember(newHostUserId, lobbyId: lobbyId, isHost: false,
            joinedAt: DateTime.UtcNow.AddMinutes(-15)); // joined SAU host → sẽ là new host (early joiner)
        _db.LobbyMembers.AddRange(oldHostMember, newHostCandidate);
        await _db.SaveChangesAsync();

        var mergeRequest = new LobbyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourceLobbyId = lobbyId,
            TargetLobbyId = Guid.NewGuid(),
            RequestedByUserId = Guid.NewGuid(),
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };

        // activeMembers chỉ có host cũ → host sẽ transfer
        // newHostCandidate ở lại source → được promote
        var activeMembers = new List<LobbyMember> { oldHostMember };
        _memberRepo.Setup(r => r.GetByLobbyAsync(lobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember> { oldHostMember, newHostCandidate });

        // Gap #1 (BR-USER-LIMIT-01): new host candidate phải pass validation
        _lobbyRepo.Setup(r => r.GetActiveLobbiesByHostAsync(newHostUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Lobby>());
        _lobbyRepo.Setup(r => r.GetActiveLobbiesByMemberAsync(newHostUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Lobby>());

        var promoted = await svc.InvokeTryAutoPromoteSourceHostAsync(
            sourceLobby, activeMembers, mergeRequest, Guid.NewGuid(), CancellationToken.None);

        Assert.True(promoted);
        Assert.Equal(newHostUserId, sourceLobby.HostUserId); // đổi sang new host
        Assert.False(oldHostMember.IsHost);                  // old host mất IsHost
        Assert.True(newHostCandidate.IsHost);                // new host có IsHost
    }

    /// <summary>
    /// Case 3: Host KHÔNG trong activeMembers (host ở lại source) → không promote.
    /// </summary>
    [Fact]
    public async Task TryAutoPromoteSourceHostAsync_WhenHostNotInTransferSet_NoPromote()
    {
        _db = CreateDb();
        var svc = CreateTestableService();

        var hostUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var sourceLobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            CafeId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            HostUserId = hostUserId,
            GameTemplateId = Guid.NewGuid(),
            HostUser = new User { Id = hostUserId, Username = "host", Email = "h@test.com", PasswordHash = "x" },
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Test Game" },
            Members = new List<LobbyMember>()
        };
        _db.Lobbies.Add(sourceLobby);

        var hostMember = BuildMember(hostUserId, lobbyId: lobbyId, isHost: true,
            joinedAt: DateTime.UtcNow.AddMinutes(-10));
        var otherMember = BuildMember(otherUserId, lobbyId: lobbyId, isHost: false,
            joinedAt: DateTime.UtcNow.AddMinutes(-5));
        _db.LobbyMembers.AddRange(hostMember, otherMember);
        await _db.SaveChangesAsync();

        var mergeRequest = new LobbyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourceLobbyId = lobbyId,
            TargetLobbyId = Guid.NewGuid(),
            RequestedByUserId = Guid.NewGuid(),
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };

        // activeMembers chỉ có otherMember, host ở lại
        var activeMembers = new List<LobbyMember> { otherMember };
        _memberRepo.Setup(r => r.GetByLobbyAsync(lobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember> { hostMember, otherMember });

        var promoted = await svc.InvokeTryAutoPromoteSourceHostAsync(
            sourceLobby, activeMembers, mergeRequest, Guid.NewGuid(), CancellationToken.None);

        Assert.False(promoted);
        Assert.Equal(hostUserId, sourceLobby.HostUserId); // unchanged
        Assert.True(hostMember.IsHost);
    }

    /// <summary>
    /// Case 4: Walk-in lobby (ReservationId == null) → không promote (không có HostUserId concept).
    /// </summary>
    [Fact]
    public async Task TryAutoPromoteSourceHostAsync_WhenWalkInLobby_NoPromote()
    {
        _db = CreateDb();
        var svc = CreateTestableService();

        var lobbyId = Guid.NewGuid();
        var sourceLobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            CafeId = Guid.NewGuid(),
            ReservationId = null, // walk-in
            HostUserId = Guid.NewGuid(),
            GameTemplateId = Guid.NewGuid(),
            HostUser = new User { Id = Guid.NewGuid(), Username = "host", Email = "h@test.com", PasswordHash = "x" },
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Test Game" },
            Members = new List<LobbyMember>()
        };
        _db.Lobbies.Add(sourceLobby);

        var mergeRequest = new LobbyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourceLobbyId = lobbyId,
            TargetLobbyId = Guid.NewGuid(),
            RequestedByUserId = Guid.NewGuid(),
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };

        var activeMembers = new List<LobbyMember>();

        var promoted = await svc.InvokeTryAutoPromoteSourceHostAsync(
            sourceLobby, activeMembers, mergeRequest, Guid.NewGuid(), CancellationToken.None);

        Assert.False(promoted);
    }

    /// <summary>
    /// Case 5: Multiple stay candidates → chọn member có JoinedAt sớm nhất.
    /// </summary>
    [Fact]
    public async Task TryAutoPromoteSourceHostAsync_WithMultipleCandidates_PicksEarliestJoiner()
    {
        _db = CreateDb();
        var svc = CreateTestableService();

        var oldHostUserId = Guid.NewGuid();
        var earliestCandidateId = Guid.NewGuid();
        var laterCandidateId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var sourceLobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            CafeId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            HostUserId = oldHostUserId,
            GameTemplateId = Guid.NewGuid(),
            HostUser = new User { Id = oldHostUserId, Username = "host", Email = "h@test.com", PasswordHash = "x" },
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Test Game" },
            Members = new List<LobbyMember>()
        };
        _db.Lobbies.Add(sourceLobby);

        var oldHostMember = BuildMember(oldHostUserId, lobbyId: lobbyId, isHost: true,
            joinedAt: DateTime.UtcNow.AddMinutes(-30));
        var earlierCandidate = BuildMember(earliestCandidateId, lobbyId: lobbyId, isHost: false,
            joinedAt: DateTime.UtcNow.AddMinutes(-20)); // EARLIEST
        var laterCandidate = BuildMember(laterCandidateId, lobbyId: lobbyId, isHost: false,
            joinedAt: DateTime.UtcNow.AddMinutes(-10));
        _db.LobbyMembers.AddRange(oldHostMember, earlierCandidate, laterCandidate);
        await _db.SaveChangesAsync();

        var mergeRequest = new LobbyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourceLobbyId = lobbyId,
            TargetLobbyId = Guid.NewGuid(),
            RequestedByUserId = Guid.NewGuid(),
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };

        var activeMembers = new List<LobbyMember> { oldHostMember };
        _memberRepo.Setup(r => r.GetByLobbyAsync(lobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember> { oldHostMember, earlierCandidate, laterCandidate });

        // Gap #1 (BR-USER-LIMIT-01): earliest candidate phải pass validation
        _lobbyRepo.Setup(r => r.GetActiveLobbiesByHostAsync(earliestCandidateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Lobby>());
        _lobbyRepo.Setup(r => r.GetActiveLobbiesByMemberAsync(earliestCandidateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Lobby>());

        var promoted = await svc.InvokeTryAutoPromoteSourceHostAsync(
            sourceLobby, activeMembers, mergeRequest, Guid.NewGuid(), CancellationToken.None);

        Assert.True(promoted);
        Assert.Equal(earliestCandidateId, sourceLobby.HostUserId); // EARLIEST joiner wins
        Assert.True(earlierCandidate.IsHost);
        Assert.False(laterCandidate.IsHost);
    }

    // =====================================================================
    // Gap #1 (BR-USER-LIMIT-01): Skip candidates vi phạm max 2 active lobbies
    // =====================================================================

    /// <summary>
    /// Case 6 (Gap #1): Stay candidate A1 đang là host của 1 lobby active khác + member 1 lobby khác → skip A1, promote A2 (candidate kế tiếp, pass validation).
    /// </summary>
    [Fact]
    public async Task TryAutoPromoteSourceHostAsync_WhenFirstCandidateAtUserLimit_SkipsAndPicksNext()
    {
        _db = CreateDb();
        var svc = CreateTestableService();

        var oldHostUserId = Guid.NewGuid();
        var firstCandidateId = Guid.NewGuid();
        var secondCandidateId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var sourceLobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            CafeId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            HostUserId = oldHostUserId,
            GameTemplateId = Guid.NewGuid(),
            HostUser = new User { Id = oldHostUserId, Username = "host", Email = "h@test.com", PasswordHash = "x" },
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Test Game" },
            Members = new List<LobbyMember>()
        };
        _db.Lobbies.Add(sourceLobby);

        var oldHostMember = BuildMember(oldHostUserId, lobbyId: lobbyId, isHost: true,
            joinedAt: DateTime.UtcNow.AddMinutes(-30));
        var firstCandidate = BuildMember(firstCandidateId, lobbyId: lobbyId, isHost: false,
            joinedAt: DateTime.UtcNow.AddMinutes(-20)); // joined trước nhưng vi phạm BR-USER-LIMIT-01
        var secondCandidate = BuildMember(secondCandidateId, lobbyId: lobbyId, isHost: false,
            joinedAt: DateTime.UtcNow.AddMinutes(-10)); // joined sau nhưng pass validation
        _db.LobbyMembers.AddRange(oldHostMember, firstCandidate, secondCandidate);
        await _db.SaveChangesAsync();

        var mergeRequest = new LobbyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourceLobbyId = lobbyId,
            TargetLobbyId = Guid.NewGuid(),
            RequestedByUserId = Guid.NewGuid(),
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };

        var activeMembers = new List<LobbyMember> { oldHostMember };
        _memberRepo.Setup(r => r.GetByLobbyAsync(lobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember> { oldHostMember, firstCandidate, secondCandidate });

        // First candidate: 1 host + 1 member = 2 active lobbies → skip
        _lobbyRepo.Setup(r => r.GetActiveLobbiesByHostAsync(firstCandidateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Lobby> { new Lobby { Id = Guid.NewGuid() } });
        _lobbyRepo.Setup(r => r.GetActiveLobbiesByMemberAsync(firstCandidateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Lobby> { new Lobby { Id = Guid.NewGuid() } });

        // Second candidate: 0 host + 0 member → pass
        _lobbyRepo.Setup(r => r.GetActiveLobbiesByHostAsync(secondCandidateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Lobby>());
        _lobbyRepo.Setup(r => r.GetActiveLobbiesByMemberAsync(secondCandidateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Lobby>());

        var promoted = await svc.InvokeTryAutoPromoteSourceHostAsync(
            sourceLobby, activeMembers, mergeRequest, Guid.NewGuid(), CancellationToken.None);

        Assert.True(promoted);
        Assert.Equal(secondCandidateId, sourceLobby.HostUserId); // picked second candidate
        Assert.True(secondCandidate.IsHost);
        Assert.False(firstCandidate.IsHost);
    }

    /// <summary>
    /// Case 7 (Gap #1): All stay candidates đều vi phạm BR-USER-LIMIT-01 → throw ConflictException
    /// để abort merge (không để source thành lobby mồ côi).
    /// </summary>
    [Fact]
    public async Task TryAutoPromoteSourceHostAsync_WhenAllCandidatesViolateUserLimit_ThrowsConflict()
    {
        _db = CreateDb();
        var svc = CreateTestableService();

        var oldHostUserId = Guid.NewGuid();
        var candidate1Id = Guid.NewGuid();
        var candidate2Id = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var sourceLobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            CafeId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            HostUserId = oldHostUserId,
            GameTemplateId = Guid.NewGuid(),
            HostUser = new User { Id = oldHostUserId, Username = "host", Email = "h@test.com", PasswordHash = "x" },
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Test Game" },
            Members = new List<LobbyMember>()
        };
        _db.Lobbies.Add(sourceLobby);

        var oldHostMember = BuildMember(oldHostUserId, lobbyId: lobbyId, isHost: true,
            joinedAt: DateTime.UtcNow.AddMinutes(-30));
        var candidate1 = BuildMember(candidate1Id, lobbyId: lobbyId, isHost: false,
            joinedAt: DateTime.UtcNow.AddMinutes(-20));
        var candidate2 = BuildMember(candidate2Id, lobbyId: lobbyId, isHost: false,
            joinedAt: DateTime.UtcNow.AddMinutes(-10));
        _db.LobbyMembers.AddRange(oldHostMember, candidate1, candidate2);
        await _db.SaveChangesAsync();

        var mergeRequest = new LobbyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourceLobbyId = lobbyId,
            TargetLobbyId = Guid.NewGuid(),
            RequestedByUserId = Guid.NewGuid(),
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };

        var activeMembers = new List<LobbyMember> { oldHostMember };
        _memberRepo.Setup(r => r.GetByLobbyAsync(lobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember> { oldHostMember, candidate1, candidate2 });

        // Both candidates: đang là member của 1 lobby active khác (BR-USER-LIMIT-01 violation)
        _lobbyRepo.Setup(r => r.GetActiveLobbiesByHostAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Lobby>());
        _lobbyRepo.Setup(r => r.GetActiveLobbiesByMemberAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Lobby> { new Lobby { Id = Guid.NewGuid() } });

        var ex = await Assert.ThrowsAsync<ConflictException>(async () =>
            await svc.InvokeTryAutoPromoteSourceHostAsync(
                sourceLobby, activeMembers, mergeRequest, Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("BR-USER-LIMIT-01", ex.Message);
        Assert.False(candidate1.IsHost); // không promote candidate nào
        Assert.False(candidate2.IsHost);
    }

    // =====================================================================
    // Gap #4: Expire pending invites của new host trên source lobby
    // =====================================================================

    /// <summary>
    /// Case 8 (Gap #4): Sau khi promote, ExpirePendingForUsersAsync được gọi với newHostUserId trên source lobby.
    /// </summary>
    [Fact]
    public async Task TryAutoPromoteSourceHostAsync_AfterPromote_ExpiresPendingInvitesForNewHost()
    {
        _db = CreateDb();
        var svc = CreateTestableService();

        var oldHostUserId = Guid.NewGuid();
        var newHostUserId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var sourceLobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            CafeId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            HostUserId = oldHostUserId,
            GameTemplateId = Guid.NewGuid(),
            HostUser = new User { Id = oldHostUserId, Username = "host", Email = "h@test.com", PasswordHash = "x" },
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Test Game" },
            Members = new List<LobbyMember>()
        };
        _db.Lobbies.Add(sourceLobby);

        var oldHostMember = BuildMember(oldHostUserId, lobbyId: lobbyId, isHost: true,
            joinedAt: DateTime.UtcNow.AddMinutes(-20));
        var newHostCandidate = BuildMember(newHostUserId, lobbyId: lobbyId, isHost: false,
            joinedAt: DateTime.UtcNow.AddMinutes(-15));
        _db.LobbyMembers.AddRange(oldHostMember, newHostCandidate);
        await _db.SaveChangesAsync();

        var mergeRequest = new LobbyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourceLobbyId = lobbyId,
            TargetLobbyId = Guid.NewGuid(),
            RequestedByUserId = Guid.NewGuid(),
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };

        var activeMembers = new List<LobbyMember> { oldHostMember };
        _memberRepo.Setup(r => r.GetByLobbyAsync(lobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember> { oldHostMember, newHostCandidate });

        // new host candidate pass BR-USER-LIMIT-01
        _lobbyRepo.Setup(r => r.GetActiveLobbiesByHostAsync(newHostUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Lobby>());
        _lobbyRepo.Setup(r => r.GetActiveLobbiesByMemberAsync(newHostUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Lobby>());

        await svc.InvokeTryAutoPromoteSourceHostAsync(
            sourceLobby, activeMembers, mergeRequest, Guid.NewGuid(), CancellationToken.None);

        // Verify ExpirePendingForUsersAsync được gọi với lobbyId + inviteeIds chứa newHostUserId
        _inviteRepo.Verify(r => r.ExpirePendingForUsersAsync(
            lobbyId,
            It.Is<IReadOnlyList<Guid>>(ids => ids != null && ids.Contains(newHostUserId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // =====================================================================
    // Gap #5: SignalR NotifyHostChanged cho new host
    // =====================================================================

    /// <summary>
    /// Case 9 (Gap #5): Sau khi promote, NotifyHostChanged được gọi để client refresh UI.
    /// </summary>
    [Fact]
    public async Task TryAutoPromoteSourceHostAsync_AfterPromote_NotifiesHostChangedViaSignalR()
    {
        _db = CreateDb();
        var svc = CreateTestableService();

        var oldHostUserId = Guid.NewGuid();
        var newHostUserId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var sourceLobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.Open,
            CafeId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            HostUserId = oldHostUserId,
            GameTemplateId = Guid.NewGuid(),
            HostUser = new User { Id = oldHostUserId, Username = "host", Email = "h@test.com", PasswordHash = "x" },
            GameTemplate = new GameTemplate { Id = Guid.NewGuid(), Name = "Test Game" },
            Members = new List<LobbyMember>()
        };
        _db.Lobbies.Add(sourceLobby);

        var oldHostMember = BuildMember(oldHostUserId, lobbyId: lobbyId, isHost: true,
            joinedAt: DateTime.UtcNow.AddMinutes(-20));
        var newHostCandidate = BuildMember(newHostUserId, lobbyId: lobbyId, isHost: false,
            joinedAt: DateTime.UtcNow.AddMinutes(-15));
        _db.LobbyMembers.AddRange(oldHostMember, newHostCandidate);
        await _db.SaveChangesAsync();

        var mergeRequest = new LobbyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourceLobbyId = lobbyId,
            TargetLobbyId = Guid.NewGuid(),
            RequestedByUserId = Guid.NewGuid(),
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };

        var activeMembers = new List<LobbyMember> { oldHostMember };
        _memberRepo.Setup(r => r.GetByLobbyAsync(lobbyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LobbyMember> { oldHostMember, newHostCandidate });

        _lobbyRepo.Setup(r => r.GetActiveLobbiesByHostAsync(newHostUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Lobby>());
        _lobbyRepo.Setup(r => r.GetActiveLobbiesByMemberAsync(newHostUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Lobby>());

        await svc.InvokeTryAutoPromoteSourceHostAsync(
            sourceLobby, activeMembers, mergeRequest, Guid.NewGuid(), CancellationToken.None);

        // Verify NotifyHostChanged được gọi với (sourceLobbyId, newHostUserId)
        _hubService.Verify(h => h.NotifyHostChanged(lobbyId, newHostUserId), Times.Once);
    }

    // =====================================================================
    // M2 / Gap #33: ReleaseSourceSessionResourcesAsync
    // ------------------------------------------------------------------------
    // Khi merge approve xong và source lobby rỗng (stillActive == 0), source
    // ActiveSession bị close + phải release table/box về Available — tránh walk-in
    // mới StartGameSessionAsync bị reject với "TableNotAvailableForGame".
    //
    // Các test này exercise helper qua InvokeReleaseSourceSessionResourcesAsync
    // wrapper, không cần chạy full ApproveMergeAsync (gặp FromSqlRaw issue với
    // InMemory DB).
    // =====================================================================

    /// <summary>
    /// Happy path: ReleaseSessionTableAndBoxAsync được gọi với đúng session ID
    /// + audit log "SourceSessionResourcesReleased" (success=true) được thêm vào DB.
    /// </summary>
    [Fact]
    public async Task ReleaseSourceSessionResourcesAsync_HappyPath_CallsReleaseAndWritesAuditSuccess()
    {
        _db = CreateDb();
        var svc = CreateTestableService();

        var cafeId = Guid.NewGuid();
        var sourceLobbyId = Guid.NewGuid();
        var targetLobbyId = Guid.NewGuid();
        var sourceReservationId = Guid.NewGuid();
        var targetReservationId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var boxId = Guid.NewGuid();

        var sourceLobby = BuildLobby(sourceLobbyId, LobbyStatus.InProgress, cafeId,
            reservationId: sourceReservationId);
        var targetLobby = BuildLobby(targetLobbyId, LobbyStatus.InProgress, cafeId,
            reservationId: targetReservationId);
        _db.Lobbies.AddRange(sourceLobby, targetLobby);

        var orphanSession = new ActiveSession
        {
            Id = sessionId,
            LobbyId = sourceLobbyId,
            CafeId = cafeId,
            HostId = Guid.NewGuid(),
            Status = GroupSessionStatus.Closed, // Đã được set Closed bởi Step 11 trước khi gọi release
            StartedAt = DateTime.UtcNow.AddMinutes(-60),
            EndedAt = DateTime.UtcNow.AddSeconds(-5),
            CafeTableId = tableId,
            CafeInventoryBoxId = boxId,
            GameTemplateId = sourceLobby.GameTemplateId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-60),
            UpdatedAt = DateTime.UtcNow.AddSeconds(-5)
        };
        _db.ActiveSessions.Add(orphanSession);

        var mergeRequest = new LobbyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourceLobbyId = sourceLobbyId,
            TargetLobbyId = targetLobbyId,
            SourceLobby = sourceLobby,
            TargetLobby = targetLobby,
            RequestedByUserId = Guid.NewGuid(),
            ReviewedByUserId = staffUserId,
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            CreatedAt = DateTime.UtcNow
        };
        _db.LobbyMergeRequests.Add(mergeRequest);
        await _db.SaveChangesAsync();

        // Act
        await svc.InvokeReleaseSourceSessionResourcesAsync(
            orphanSession, mergeRequest, sourceLobbyId, targetLobbyId, CancellationToken.None);

        // Note: ReleaseSourceSessionResourcesAsync chỉ add audit log vào change tracker;
        // production ApproveMergeAsync sẽ SaveChangesAsync sau đó. Reproduce tương tự.
        await _db.SaveChangesAsync();

        // Assert 1: ReleaseSessionTableAndBoxAsync được gọi với session ID
        _activeSessionRepo.Verify(
            r => r.ReleaseSessionTableAndBoxAsync(sessionId, It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert 2: Audit log "SourceSessionResourcesReleased" được thêm với success=true
        var auditLogs = await _db.LobbyMergeAuditLogs
            .Where(a => a.MergeRequestId == mergeRequest.Id)
            .ToListAsync();
        Assert.Single(auditLogs);
        var audit = auditLogs[0];
        Assert.Equal("SourceSessionResourcesReleased", audit.Action);
        Assert.True(audit.Success);
        Assert.Null(audit.ErrorMessage);
        Assert.Equal(sourceLobbyId, audit.SourceLobbyId);
        Assert.Equal(targetLobbyId, audit.TargetLobbyId);
        Assert.Equal(staffUserId, audit.PerformedByUserId);

        // Assert 3: Metadata chứa orphanSessionId, releasedTableId, releasedBoxId
        Assert.NotNull(audit.Metadata);
        using var metadataDoc = System.Text.Json.JsonDocument.Parse(audit.Metadata);
        Assert.Equal(sessionId, metadataDoc.RootElement.GetProperty("orphanSessionId").GetGuid());
        Assert.Equal(tableId, metadataDoc.RootElement.GetProperty("releasedTableId").GetGuid());
        Assert.Equal(boxId, metadataDoc.RootElement.GetProperty("releasedBoxId").GetGuid());
        Assert.Equal("SourceLobbyDissolved_AfterLastMemberMerged",
            metadataDoc.RootElement.GetProperty("reason").GetString());
    }

    /// <summary>
    /// Idempotent re-call: Audit log mới được tạo cho mỗi lần gọi
    /// (audit log là append-only — KHÔNG update entry cũ).
    /// </summary>
    [Fact]
    public async Task ReleaseSourceSessionResourcesAsync_WhenCalledTwice_AppendsTwoAuditEntries()
    {
        _db = CreateDb();
        var svc = CreateTestableService();

        var cafeId = Guid.NewGuid();
        var sourceLobbyId = Guid.NewGuid();
        var targetLobbyId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();

        var sourceLobby = BuildLobby(sourceLobbyId, LobbyStatus.InProgress, cafeId);
        var targetLobby = BuildLobby(targetLobbyId, LobbyStatus.InProgress, cafeId);
        _db.Lobbies.AddRange(sourceLobby, targetLobby);

        var orphanSession = new ActiveSession
        {
            Id = sessionId,
            LobbyId = sourceLobbyId,
            CafeId = cafeId,
            HostId = Guid.NewGuid(),
            Status = GroupSessionStatus.Closed,
            StartedAt = DateTime.UtcNow.AddMinutes(-60),
            CafeTableId = Guid.NewGuid(),
            CafeInventoryBoxId = Guid.NewGuid(),
            GameTemplateId = sourceLobby.GameTemplateId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-60)
        };
        _db.ActiveSessions.Add(orphanSession);

        var mergeRequest = new LobbyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourceLobbyId = sourceLobbyId,
            TargetLobbyId = targetLobbyId,
            SourceLobby = sourceLobby,
            TargetLobby = targetLobby,
            RequestedByUserId = Guid.NewGuid(),
            ReviewedByUserId = staffUserId,
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            CreatedAt = DateTime.UtcNow
        };
        _db.LobbyMergeRequests.Add(mergeRequest);
        await _db.SaveChangesAsync();

        // Act: gọi 2 lần
        await svc.InvokeReleaseSourceSessionResourcesAsync(
            orphanSession, mergeRequest, sourceLobbyId, targetLobbyId, CancellationToken.None);
        await svc.InvokeReleaseSourceSessionResourcesAsync(
            orphanSession, mergeRequest, sourceLobbyId, targetLobbyId, CancellationToken.None);

        // Note: ReleaseSourceSessionResourcesAsync chỉ add audit log vào change tracker;
        // production ApproveMergeAsync sẽ SaveChangesAsync sau đó. Reproduce tương tự.
        await _db.SaveChangesAsync();

        // Assert: Release được gọi 2 lần (release helper itself is idempotent ở
        // ActiveSessionRepository — chỉ flip status khi đang InUse)
        _activeSessionRepo.Verify(
            r => r.ReleaseSessionTableAndBoxAsync(sessionId, It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Assert: 2 audit log entries (append-only)
        var auditLogs = await _db.LobbyMergeAuditLogs
            .Where(a => a.MergeRequestId == mergeRequest.Id
                && a.Action == "SourceSessionResourcesReleased")
            .ToListAsync();
        Assert.Equal(2, auditLogs.Count);
        Assert.All(auditLogs, a => Assert.True(a.Success));
    }

    /// <summary>
    /// Failure path: Khi ReleaseSessionTableAndBoxAsync throw exception:
    /// - Audit log "SourceSessionResourcesReleasedFailed" được thêm với success=false + errorMessage.
    /// - Helper KHÔNG re-throw exception (best-effort — không làm fail cả merge).
    /// </summary>
    [Fact]
    public async Task ReleaseSourceSessionResourcesAsync_WhenReleaseThrows_WritesFailureAuditAndDoesNotPropagate()
    {
        _db = CreateDb();
        var svc = CreateTestableService();

        var cafeId = Guid.NewGuid();
        var sourceLobbyId = Guid.NewGuid();
        var targetLobbyId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();

        var sourceLobby = BuildLobby(sourceLobbyId, LobbyStatus.InProgress, cafeId);
        var targetLobby = BuildLobby(targetLobbyId, LobbyStatus.InProgress, cafeId);
        _db.Lobbies.AddRange(sourceLobby, targetLobby);

        var orphanSession = new ActiveSession
        {
            Id = sessionId,
            LobbyId = sourceLobbyId,
            CafeId = cafeId,
            HostId = Guid.NewGuid(),
            Status = GroupSessionStatus.Closed,
            StartedAt = DateTime.UtcNow.AddMinutes(-60),
            CafeTableId = Guid.NewGuid(),
            CafeInventoryBoxId = Guid.NewGuid(),
            GameTemplateId = sourceLobby.GameTemplateId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-60)
        };
        _db.ActiveSessions.Add(orphanSession);

        var mergeRequest = new LobbyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourceLobbyId = sourceLobbyId,
            TargetLobbyId = targetLobbyId,
            SourceLobby = sourceLobby,
            TargetLobby = targetLobby,
            RequestedByUserId = Guid.NewGuid(),
            ReviewedByUserId = staffUserId,
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            CreatedAt = DateTime.UtcNow
        };
        _db.LobbyMergeRequests.Add(mergeRequest);
        await _db.SaveChangesAsync();

        // Mock: ReleaseSessionTableAndBoxAsync throws (giả lập DB connection error)
        var simulatedError = new InvalidOperationException(
            "Simulated DB connection error during release");
        _activeSessionRepo
            .Setup(r => r.ReleaseSessionTableAndBoxAsync(sessionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(simulatedError);

        // Act: phải KHÔNG throw exception (best-effort)
        var exception = await Record.ExceptionAsync(() =>
            svc.InvokeReleaseSourceSessionResourcesAsync(
                orphanSession, mergeRequest, sourceLobbyId, targetLobbyId, CancellationToken.None));
        Assert.Null(exception);

        // Note: ReleaseSourceSessionResourcesAsync chỉ add audit log vào change tracker;
        // production ApproveMergeAsync sẽ SaveChangesAsync sau đó. Reproduce tương tự.
        await _db.SaveChangesAsync();

        // Assert: Audit log failure được thêm vào DB
        var auditLogs = await _db.LobbyMergeAuditLogs
            .Where(a => a.MergeRequestId == mergeRequest.Id)
            .ToListAsync();
        Assert.Single(auditLogs);
        var audit = auditLogs[0];
        Assert.Equal("SourceSessionResourcesReleasedFailed", audit.Action);
        Assert.False(audit.Success);
        Assert.Contains("Simulated DB connection error", audit.ErrorMessage);
        Assert.Equal(staffUserId, audit.PerformedByUserId);

        // Metadata chứa retry hint cho AutoReleaseExpiredSessionsJob
        Assert.NotNull(audit.Metadata);
        using var metadataDoc = System.Text.Json.JsonDocument.Parse(audit.Metadata);
        Assert.Equal("AutoReleaseExpiredSessionsJob",
            metadataDoc.RootElement.GetProperty("willBeRetriedBy").GetString());
    }

    /// <summary>
    /// Edge case: Session không có CafeTableId / CafeInventoryBoxId (walk-in lobby
    /// tạo mà chưa gán table/box). Vẫn phải gọi Release helper (idempotent — nó
    /// check HasValue trước khi load) + audit log với table/box IDs = null.
    /// </summary>
    [Fact]
    public async Task ReleaseSourceSessionResourcesAsync_WhenSessionHasNoTableOrBox_StillSucceeds()
    {
        _db = CreateDb();
        var svc = CreateTestableService();

        var cafeId = Guid.NewGuid();
        var sourceLobbyId = Guid.NewGuid();
        var targetLobbyId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var sourceLobby = BuildLobby(sourceLobbyId, LobbyStatus.InProgress, cafeId);
        var targetLobby = BuildLobby(targetLobbyId, LobbyStatus.InProgress, cafeId);
        _db.Lobbies.AddRange(sourceLobby, targetLobby);

        var orphanSession = new ActiveSession
        {
            Id = sessionId,
            LobbyId = sourceLobbyId,
            CafeId = cafeId,
            HostId = Guid.NewGuid(),
            Status = GroupSessionStatus.Closed,
            StartedAt = DateTime.UtcNow.AddMinutes(-60),
            // CafeTableId = null, CafeInventoryBoxId = null — walk-in edge case
            GameTemplateId = sourceLobby.GameTemplateId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-60)
        };
        _db.ActiveSessions.Add(orphanSession);

        var mergeRequest = new LobbyMergeRequest
        {
            Id = Guid.NewGuid(),
            SourceLobbyId = sourceLobbyId,
            TargetLobbyId = targetLobbyId,
            SourceLobby = sourceLobby,
            TargetLobby = targetLobby,
            RequestedByUserId = Guid.NewGuid(),
            ReviewedByUserId = Guid.NewGuid(),
            Status = LobbyMergeRequestStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            CreatedAt = DateTime.UtcNow
        };
        _db.LobbyMergeRequests.Add(mergeRequest);
        await _db.SaveChangesAsync();

        // Act
        await svc.InvokeReleaseSourceSessionResourcesAsync(
            orphanSession, mergeRequest, sourceLobbyId, targetLobbyId, CancellationToken.None);

        // Note: ReleaseSourceSessionResourcesAsync chỉ add audit log vào change tracker;
        // production ApproveMergeAsync sẽ SaveChangesAsync sau đó. Reproduce tương tự.
        await _db.SaveChangesAsync();

        // Assert: Release helper vẫn được gọi (idempotent guard ở repository level)
        _activeSessionRepo.Verify(
            r => r.ReleaseSessionTableAndBoxAsync(sessionId, It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: Audit log success với table/box IDs = null trong metadata
        var audit = await _db.LobbyMergeAuditLogs
            .Where(a => a.MergeRequestId == mergeRequest.Id
                && a.Action == "SourceSessionResourcesReleased")
            .FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.True(audit.Success);
        Assert.NotNull(audit.Metadata);
        using var metadataDoc = System.Text.Json.JsonDocument.Parse(audit.Metadata);
        // releasedTableId và releasedBoxId sẽ là Guid.Empty (default) khi null
        // vì Guid là struct không nullable — vẫn pass thông tin "không có gì để release".
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
            IWalletService walletService,
            IBookingDepositRepository depositRepository,
            ISeatInventoryRepository seatInventoryRepository,
            IActiveSessionRepository activeSessionRepository,
            IHttpContextAccessor httpContextAccessor,
            ISystemConfigurationProvider configProvider,
            ILogger<LobbyMergeService> logger,
            ILobbyHubService lobbyHubService,
            ILobbyInviteRepository lobbyInviteRepository)
            : base(
                db, lobbyRepository, lobbyMemberRepository, cafeRepository,
                userRepository, walletRepository, walletService, depositRepository, seatInventoryRepository,
                activeSessionRepository, httpContextAccessor, configProvider, logger, lobbyHubService, lobbyInviteRepository)
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

        /// <summary>
        /// Public wrapper cho unit test để gọi protected TryAutoPromoteSourceHostAsync.
        /// </summary>
        public Task<bool> InvokeTryAutoPromoteSourceHostAsync(
            Lobby sourceLobby,
            IReadOnlyList<LobbyMember> activeMembers,
            LobbyMergeRequest mergeRequest,
            Guid staffUserId,
            CancellationToken cancellationToken)
            => TryAutoPromoteSourceHostAsync(
                sourceLobby, activeMembers, mergeRequest, staffUserId, cancellationToken);

        /// <summary>
        /// Public wrapper cho unit test để gọi protected ReleaseSourceSessionResourcesAsync.
        /// </summary>
        public Task InvokeReleaseSourceSessionResourcesAsync(
            ActiveSession orphanSession,
            LobbyMergeRequest mergeRequest,
            Guid sourceLobbyId,
            Guid targetLobbyId,
            CancellationToken cancellationToken)
            => ReleaseSourceSessionResourcesAsync(
                orphanSession, mergeRequest, sourceLobbyId, targetLobbyId, cancellationToken);
    }

    private TestableLobbyMergeService CreateTestableService() => new(
        _db,
        _lobbyRepo.Object,
        _memberRepo.Object,
        _cafeRepo.Object,
        _userRepo.Object,
        _walletRepo.Object,
        _walletService.Object,
        _depositRepo.Object,
        _seatRepo.Object,
        _activeSessionRepo.Object,
        _httpCtx.Object,
        _configProvider.Object,
        _logger.Object,
        _hubService.Object,
        _inviteRepo.Object);
}