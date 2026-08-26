using BoardVerse.Core.DTOs.Wallet;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.Services;
using BoardVerse.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// BvcRefundRequestService — player gửi yêu cầu hoàn BVC, admin duyệt/từ chối.
/// BR-RISK-05: mọi admin resolve action ghi PlayerActionHistory.
/// BR § III.3: ledger append-only — admin approve tạo entry AdminCredit mới.
/// BR § XVII.1: idempotent theo IdempotencyKey.
/// </summary>
public class BvcRefundRequestServiceTests
{
    private readonly Mock<IBvcRefundRequestRepository> _mockRefundRepo;
    private readonly Mock<IBvcLedgerEntryRepository> _mockLedgerRepo;
    private readonly Mock<IUserManagementRepository> _mockUserRepo;
    private readonly Mock<IWalletRepository> _mockWalletRepo;
    private readonly Mock<ILogger<BvcRefundRequestService>> _mockLogger;
    private readonly BoardVerseDbContext _dbContext;
    private readonly BvcRefundRequestService _service;

    public BvcRefundRequestServiceTests()
    {
        _mockRefundRepo = new Mock<IBvcRefundRequestRepository>();
        _mockLedgerRepo = new Mock<IBvcLedgerEntryRepository>();
        _mockUserRepo = new Mock<IUserManagementRepository>();
        _mockWalletRepo = new Mock<IWalletRepository>();
        _mockLogger = new Mock<ILogger<BvcRefundRequestService>>();

        // FakeDbContext dùng Options.Empty — không gọi _db.Database.* / SaveChanges
        // (BR-RISK-05 audit log via PlayerActionHistories được test ở integration test với DB thật).
        _dbContext = new FakeDbContext();

        _service = new BvcRefundRequestService(
            _mockRefundRepo.Object,
            _mockLedgerRepo.Object,
            _mockUserRepo.Object,
            _mockWalletRepo.Object,
            _mockLogger.Object,
            _dbContext);
    }

    // ========================================================================
    // CREATE — validation
    // ========================================================================

    [Fact]
    public async Task CreateAsync_MissingIdempotencyKey_ThrowsBadRequest()
    {
        var userId = Guid.NewGuid();
        var req = ValidRequest();

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateAsync(userId, req, idempotencyKey: ""));

        Assert.Equal(ApiErrorMessages.Wallet.IdempotencyKeyRequired, ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ZeroAmount_ThrowsBadRequest()
    {
        var userId = Guid.NewGuid();
        var req = ValidRequest();
        req.RequestedAmountBvc = 0;

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateAsync(userId, req, idempotencyKey: "key-001"));

        Assert.Equal(ApiErrorMessages.Wallet.RefundRequestInvalidAmount, ex.Message);
    }

    [Fact]
    public async Task CreateAsync_NegativeAmount_ThrowsBadRequest()
    {
        var userId = Guid.NewGuid();
        var req = ValidRequest();
        req.RequestedAmountBvc = -50;

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateAsync(userId, req, idempotencyKey: "key-001"));

        Assert.Equal(ApiErrorMessages.Wallet.RefundRequestInvalidAmount, ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ReasonTooShort_ThrowsBadRequest()
    {
        var userId = Guid.NewGuid();
        var req = ValidRequest();
        req.PlayerReason = "abc";

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateAsync(userId, req, idempotencyKey: "key-001"));

        Assert.Equal(ApiErrorMessages.Wallet.RefundRequestReasonTooShort, ex.Message);
    }

    [Fact]
    public async Task CreateAsync_LedgerEntryNotFound_ThrowsNotFound()
    {
        var userId = Guid.NewGuid();
        var req = ValidRequest();
        const string key = "unique-key-001";

        _mockRefundRepo
            .Setup(r => r.GetByIdempotencyKeyAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BvcRefundRequest?)null);
        _mockLedgerRepo
            .Setup(r => r.GetByIdAsync(req.RelatedLedgerEntryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BvcLedgerEntry?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateAsync(userId, req, key));

        Assert.Equal(ApiErrorMessages.Wallet.RefundRequestLedgerEntryNotFound, ex.Message);
    }

    [Fact]
    public async Task CreateAsync_LedgerEntryNotOwned_ThrowsForbidden()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var req = ValidRequest();
        const string key = "unique-key-002";

        _mockRefundRepo
            .Setup(r => r.GetByIdempotencyKeyAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BvcRefundRequest?)null);
        _mockLedgerRepo
            .Setup(r => r.GetByIdAsync(req.RelatedLedgerEntryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcLedgerEntry
            {
                Id = req.RelatedLedgerEntryId,
                UserId = otherUserId,
                Amount = 100_000,
                Type = LedgerEntryType.TopUp
            });

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreateAsync(userId, req, key));

        Assert.Equal(ApiErrorMessages.Wallet.RefundRequestLedgerEntryNotOwned, ex.Message);
    }

    // ========================================================================
    // CREATE — idempotency
    // ========================================================================

    [Fact]
    public async Task CreateAsync_ExistingIdempotencyKeySameUser_ReturnsExisting()
    {
        var userId = Guid.NewGuid();
        var req = ValidRequest();
        const string key = "replay-key-001";
        var existing = new BvcRefundRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RelatedLedgerEntryId = req.RelatedLedgerEntryId,
            RequestedAmountBvc = req.RequestedAmountBvc,
            PlayerReason = req.PlayerReason,
            IdempotencyKey = key,
            Status = RefundRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        _mockRefundRepo
            .Setup(r => r.GetByIdempotencyKeyAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var dto = await _service.CreateAsync(userId, req, key);

        Assert.Equal(existing.Id, dto.Id);
        Assert.Equal(RefundRequestStatus.Pending, dto.Status);
        // KHÔNG gọi ledger repo khi replay.
        _mockLedgerRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRefundRepo.Verify(r => r.AddAsync(It.IsAny<BvcRefundRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ExistingIdempotencyKeyDifferentUser_ThrowsConflict()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var req = ValidRequest();
        const string key = "shared-key-001";
        var existing = new BvcRefundRequest
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            RelatedLedgerEntryId = req.RelatedLedgerEntryId,
            RequestedAmountBvc = req.RequestedAmountBvc,
            PlayerReason = req.PlayerReason,
            IdempotencyKey = key,
            Status = RefundRequestStatus.Pending
        };

        _mockRefundRepo
            .Setup(r => r.GetByIdempotencyKeyAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateAsync(userId, req, key));

        Assert.Equal(ApiErrorMessages.Reservation.IdempotencyKeyConflict, ex.Message);
    }

    // ========================================================================
    // CREATE — happy path
    // ========================================================================

    [Fact]
    public async Task CreateAsync_NewRequest_PersistsAndReturnsDto()
    {
        var userId = Guid.NewGuid();
        var req = ValidRequest();
        const string key = "new-key-001";

        _mockRefundRepo
            .Setup(r => r.GetByIdempotencyKeyAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BvcRefundRequest?)null);
        _mockLedgerRepo
            .Setup(r => r.GetByIdAsync(req.RelatedLedgerEntryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcLedgerEntry
            {
                Id = req.RelatedLedgerEntryId,
                UserId = userId,
                Amount = 200_000,
                Type = LedgerEntryType.TopUp
            });

        BvcRefundRequest? savedRequest = null;
        _mockRefundRepo
            .Setup(r => r.AddAsync(It.IsAny<BvcRefundRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BvcRefundRequest, CancellationToken>((r, _) => savedRequest = r)
            .Returns(Task.CompletedTask);

        var dto = await _service.CreateAsync(userId, req, key);

        Assert.NotNull(savedRequest);
        Assert.Equal(userId, savedRequest!.UserId);
        Assert.Equal(req.RelatedLedgerEntryId, savedRequest.RelatedLedgerEntryId);
        Assert.Equal(req.RequestedAmountBvc, savedRequest.RequestedAmountBvc);
        Assert.Equal(RefundRequestStatus.Pending, savedRequest.Status);
        Assert.Equal(key, savedRequest.IdempotencyKey);

        Assert.Equal(savedRequest.Id, dto.Id);
        Assert.Equal(RefundRequestStatus.Pending, dto.Status);

        _mockRefundRepo.Verify(r => r.AddAsync(It.IsAny<BvcRefundRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockRefundRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ========================================================================
    // CANCEL
    // ========================================================================

    [Fact]
    public async Task CancelAsync_RequestNotFound_ThrowsNotFound()
    {
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _mockRefundRepo
            .Setup(r => r.GetByIdAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BvcRefundRequest?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CancelAsync(requestId, userId));

        Assert.Equal(ApiErrorMessages.Wallet.RefundRequestNotFound(requestId), ex.Message);
    }

    [Fact]
    public async Task CancelAsync_RequestNotOwned_ThrowsForbidden()
    {
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUser = Guid.NewGuid();

        _mockRefundRepo
            .Setup(r => r.GetByIdForUpdateAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcRefundRequest
            {
                Id = requestId,
                UserId = otherUser,
                Status = RefundRequestStatus.Pending
            });

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CancelAsync(requestId, userId));

        Assert.Equal(ApiErrorMessages.Wallet.RefundRequestNotOwned, ex.Message);
    }

    [Fact]
    public async Task CancelAsync_AlreadyApproved_ThrowsConflict()
    {
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _mockRefundRepo
            .Setup(r => r.GetByIdForUpdateAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcRefundRequest
            {
                Id = requestId,
                UserId = userId,
                Status = RefundRequestStatus.Approved
            });

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CancelAsync(requestId, userId));

        Assert.Equal(ApiErrorMessages.Wallet.RefundRequestNotPending, ex.Message);
    }

    [Fact]
    public async Task CancelAsync_PendingOwned_MarksCancelled()
    {
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var pending = new BvcRefundRequest
        {
            Id = requestId,
            UserId = userId,
            Status = RefundRequestStatus.Pending,
            PlayerReason = "Top-up sai số tiền mong muốn, muốn hoàn lại",
            IdempotencyKey = "key"
        };

        _mockRefundRepo
            .Setup(r => r.GetByIdForUpdateAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        await _service.CancelAsync(requestId, userId);

        Assert.Equal(RefundRequestStatus.Cancelled, pending.Status);
        _mockRefundRepo.Verify(r => r.UpdateAsync(pending, It.IsAny<CancellationToken>()), Times.Once);
        _mockRefundRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ========================================================================
    // RESOLVE — validation
    // ========================================================================

    [Fact]
    public async Task ResolveAsync_MissingIdempotencyKey_ThrowsBadRequest()
    {
        var requestId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var req = ValidResolveRequest();

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.ResolveAsync(requestId, req, adminId, idempotencyKey: ""));

        Assert.Equal(ApiErrorMessages.Wallet.IdempotencyKeyRequired, ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_AdminNoteTooShort_ThrowsBadRequest()
    {
        var requestId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var req = ValidResolveRequest();
        req.AdminNote = "ok"; // < 5 chars

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.ResolveAsync(requestId, req, adminId, "key-001"));

        Assert.Equal(ApiErrorMessages.Wallet.RefundRequestAdminNoteRequired, ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_ApproveWithoutAmount_ThrowsBadRequest()
    {
        var requestId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var req = ValidResolveRequest();
        req.Decision = RefundDecision.Approve;
        req.ApprovedAmountBvc = null;

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.ResolveAsync(requestId, req, adminId, "key-001"));

        Assert.Contains("Số BVC", ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_AdminNotFound_ThrowsNotFound()
    {
        var requestId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var req = ValidResolveRequest();

        _mockUserRepo.Setup(r => r.GetByIdAsync(adminId, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.ResolveAsync(requestId, req, adminId, "key-001"));

        Assert.Equal(ApiErrorMessages.AdminUsers.UserNotFound(adminId), ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_RefundRequestNotFound_ThrowsNotFound()
    {
        var requestId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var req = ValidResolveRequest();

        _mockUserRepo.Setup(r => r.GetByIdAsync(adminId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = adminId, Username = "admin", Email = "admin@test.com" });
        _mockRefundRepo
            .Setup(r => r.GetByIdForUpdateAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BvcRefundRequest?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.ResolveAsync(requestId, req, adminId, "key-001"));

        Assert.Equal(ApiErrorMessages.Wallet.RefundRequestNotFound(requestId), ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_RequestAlreadyResolved_ThrowsConflict()
    {
        var requestId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var req = ValidResolveRequest();

        _mockUserRepo.Setup(r => r.GetByIdAsync(adminId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = adminId, Username = "admin", Email = "admin@test.com" });
        _mockRefundRepo
            .Setup(r => r.GetByIdForUpdateAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcRefundRequest
            {
                Id = requestId,
                UserId = Guid.NewGuid(),
                Status = RefundRequestStatus.Approved
            });

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.ResolveAsync(requestId, req, adminId, "key-001"));

        Assert.Equal(ApiErrorMessages.Wallet.RefundRequestNotPending, ex.Message);
    }

    // ========================================================================
    // RESOLVE — happy paths
    // ========================================================================

    [Fact]
    public async Task ResolveAsync_Approve_CreditsWalletAndWritesAuditLog()
    {
        var userId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var ledgerEntryId = Guid.NewGuid();
        var req = ValidResolveRequest();
        req.Decision = RefundDecision.Approve;
        req.ApprovedAmountBvc = 50_000;

        var refundRequest = new BvcRefundRequest
        {
            Id = requestId,
            UserId = userId,
            RelatedLedgerEntryId = Guid.NewGuid(),
            RequestedAmountBvc = 80_000,
            PlayerReason = "Top-up nhầm, đã thanh toán thành công nhưng muốn hủy",
            IdempotencyKey = "irrelevant",
            Status = RefundRequestStatus.Pending
        };

        var wallet = new Wallet
        {
            UserId = userId,
            AvailableBalance = 100_000,
            HeldBalance = 0
        };

        _mockUserRepo.Setup(r => r.GetByIdAsync(adminId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = adminId, Username = "admin", Email = "admin@test.com" });
        _mockRefundRepo
            .Setup(r => r.GetByIdForUpdateAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(refundRequest);
        _mockLedgerRepo
            .Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BvcLedgerEntry?)null);
        _mockWalletRepo
            .Setup(r => r.GetByUserIdForUpdateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        await _service.ResolveAsync(requestId, req, adminId, "admin-key-001");

        // Wallet cộng 50k.
        Assert.Equal(150_000, wallet.AvailableBalance);

        // Ledger entry mới được tạo.
        _mockLedgerRepo.Verify(r => r.AddAsync(It.Is<BvcLedgerEntry>(e =>
            e.UserId == userId
            && e.Amount == 50_000
            && e.Type == LedgerEntryType.AdminCredit
            && e.BalanceSnapshot == 150_000
            && e.Note!.Contains($"Request={requestId}")
            && e.IdempotencyKey.Contains($"refund:{requestId}:admin:{adminId}:admin-key-001")
        ), It.IsAny<CancellationToken>()), Times.Once);

        // RefundRequest cập nhật status + admin info.
        Assert.Equal(RefundRequestStatus.Approved, refundRequest.Status);
        Assert.Equal(50_000, refundRequest.ApprovedAmountBvc);
        Assert.Equal(adminId, refundRequest.ResolvedByAdminId);
        Assert.NotNull(refundRequest.ResolvedAt);
        Assert.NotNull(refundRequest.ResultLedgerEntryId);

        // (BR-RISK-05 audit log → PlayerActionHistories.Add được verify ở integration test với DB thật
        // vì InMemory provider không bind được JsonDocument của PlayerActionHistory.Metadata.)

        _mockRefundRepo.Verify(r => r.UpdateAsync(refundRequest, It.IsAny<CancellationToken>()), Times.Once);
        _mockRefundRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockWalletRepo.Verify(r => r.UpdateAsync(wallet, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_Reject_DoesNotCreditWalletAndWritesAuditLog()
    {
        var userId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var req = ValidResolveRequest();
        req.Decision = RefundDecision.Reject;
        req.ApprovedAmountBvc = null;
        req.AdminNote = "Lý do không hợp lệ - không hoàn tiền";

        var refundRequest = new BvcRefundRequest
        {
            Id = requestId,
            UserId = userId,
            RelatedLedgerEntryId = Guid.NewGuid(),
            RequestedAmountBvc = 30_000,
            PlayerReason = "Top-up nhầm, đã thanh toán thành công nhưng muốn hủy",
            IdempotencyKey = "irrelevant",
            Status = RefundRequestStatus.Pending
        };

        _mockUserRepo.Setup(r => r.GetByIdAsync(adminId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = adminId, Username = "admin", Email = "admin@test.com" });
        _mockRefundRepo
            .Setup(r => r.GetByIdForUpdateAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(refundRequest);

        await _service.ResolveAsync(requestId, req, adminId, "admin-key-002");

        // KHÔNG gọi wallet repo (không có tiền cộng).
        _mockWalletRepo.Verify(r => r.UpdateAsync(It.IsAny<Wallet>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockWalletRepo.Verify(r => r.GetByUserIdForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        // KHÔNG tạo ledger entry.
        _mockLedgerRepo.Verify(r => r.AddAsync(It.IsAny<BvcLedgerEntry>(), It.IsAny<CancellationToken>()), Times.Never);

        // RefundRequest status = Rejected, không có ApprovedAmount.
        Assert.Equal(RefundRequestStatus.Rejected, refundRequest.Status);
        Assert.Null(refundRequest.ApprovedAmountBvc);
        Assert.Null(refundRequest.ResultLedgerEntryId);

        // (BR-RISK-05 audit log → PlayerActionHistories.Add được verify ở integration test với DB thật
        // vì InMemory provider không bind được JsonDocument của PlayerActionHistory.Metadata.)
    }

    [Fact]
    public async Task ResolveAsync_ApproveIdempotentReplay_DoesNotDoubleCredit()
    {
        var userId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var existingLedgerId = Guid.NewGuid();
        var req = ValidResolveRequest();
        req.Decision = RefundDecision.Approve;
        req.ApprovedAmountBvc = 50_000;

        var refundRequest = new BvcRefundRequest
        {
            Id = requestId,
            UserId = userId,
            RelatedLedgerEntryId = Guid.NewGuid(),
            RequestedAmountBvc = 80_000,
            PlayerReason = "Top-up nhầm, đã thanh toán thành công nhưng muốn hủy",
            IdempotencyKey = "irrelevant",
            Status = RefundRequestStatus.Pending
        };

        _mockUserRepo.Setup(r => r.GetByIdAsync(adminId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = adminId, Username = "admin", Email = "admin@test.com" });
        _mockRefundRepo
            .Setup(r => r.GetByIdForUpdateAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(refundRequest);

        // Replay: ledger đã tồn tại với cùng key.
        _mockLedgerRepo
            .Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BvcLedgerEntry
            {
                Id = existingLedgerId,
                UserId = userId,
                Type = LedgerEntryType.AdminCredit,
                Amount = 50_000,
                IdempotencyKey = "refund-replay-key"
            });

        await _service.ResolveAsync(requestId, req, adminId, "replay-key-001");

        // KHÔNG thêm ledger entry mới.
        _mockLedgerRepo.Verify(r => r.AddAsync(It.IsAny<BvcLedgerEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        // KHÔNG cập nhật wallet.
        _mockWalletRepo.Verify(r => r.UpdateAsync(It.IsAny<Wallet>(), It.IsAny<CancellationToken>()), Times.Never);

        // Reuse ledger id.
        Assert.Equal(existingLedgerId, refundRequest.ResultLedgerEntryId);
        Assert.Equal(RefundRequestStatus.Approved, refundRequest.Status);
    }

    // ========================================================================
    // HELPERS
    // ========================================================================

    private static CreateRefundRequestDto ValidRequest() => new()
    {
        RelatedLedgerEntryId = Guid.NewGuid(),
        RequestedAmountBvc = 100_000,
        PlayerReason = "Tôi nạp nhầm số tiền và muốn được hoàn lại một phần"
    };

    private static ResolveRefundRequestDto ValidResolveRequest() => new()
    {
        Decision = RefundDecision.Approve,
        ApprovedAmountBvc = 50_000,
        AdminNote = "Xác nhận giao dịch hợp lệ, hoàn một phần"
    };
}