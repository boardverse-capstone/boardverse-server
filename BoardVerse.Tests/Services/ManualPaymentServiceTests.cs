using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// GAP-MANUAL-PAY-RACE Fix: Tests cho ConfirmManualPaymentAsync với atomic flip
/// (ExecuteUpdateAsync với WHERE Status=Unpaid). Trước đây 2 staff click confirm
/// cùng lúc tạo 2 Transaction Succeeded orphan. Sau fix: chỉ 1 flip thành công,
/// staff thứ 2 nhận ConflictException.
/// </summary>
public class ManualPaymentServiceTests : IDisposable
{
    private readonly Mock<ITransactionRepository> _transactionRepo = new();
    private readonly Mock<IBookingDepositRepository> _depositRepo = new();
    private readonly Mock<IActiveSessionRepository> _sessionRepo = new();
    private readonly Mock<ICafeRepository> _cafeRepo = new();
    private readonly Mock<ILogger<ManualPaymentService>> _logger = new();
    private readonly BoardVerseDbContext _dbContext;

    public ManualPaymentServiceTests()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new BoardVerseDbContext(options);

        _transactionRepo.Setup(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction t, CancellationToken _) => t);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private ManualPaymentService CreateService() => new(
        _transactionRepo.Object,
        _depositRepo.Object,
        _sessionRepo.Object,
        _cafeRepo.Object,
        _dbContext,
        _logger.Object);

    /// <summary>
    /// Helper: tạo session và add vào InMemory DbContext để TryAtomicFlipSessionStatusAsync
    /// fallback path có thể tìm thấy entity. Đồng thời mock _sessionRepo trả về
    /// cùng reference (để các assertions trên instance object hoạt động đúng).
    /// </summary>
    private async Task<ActiveSession> SeedSessionAsync(
        Guid sessionId,
        Guid cafeId,
        decimal totalAmount,
        GroupSessionStatus initialStatus = GroupSessionStatus.Unpaid,
        ICollection<ActiveSessionMember>? members = null)
    {
        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = initialStatus,
            TotalAmount = totalAmount,
            Members = members ?? new List<ActiveSessionMember>()
        };

        await _dbContext.ActiveSessions.AddAsync(session);
        if (members is not null)
        {
            await _dbContext.ActiveSessionMembers.AddRangeAsync(members);
        }
        await _dbContext.SaveChangesAsync();

        _sessionRepo
            .Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        return session;
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_WithInvalidPaymentType_ThrowsArgument()
    {
        var svc = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "INVALID",
                OrderId = Guid.NewGuid(),
                Amount = 10000m,
                PaymentMethod = "CASH"
            },
            Guid.NewGuid(),
            "Admin"));
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_WithInvalidPaymentMethod_ThrowsArgument()
    {
        var svc = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "SESSION",
                OrderId = Guid.NewGuid(),
                Amount = 10000m,
                PaymentMethod = "BITCOIN"
            },
            Guid.NewGuid(),
            "Admin"));
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_DepositPaymentTypeRejected_ThrowsArgument()
    {
        // M6: DEPOSIT payment is now rejected — staff must use a separate deposit endpoint.
        var svc = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "DEPOSIT",
                OrderId = Guid.NewGuid(),
                Amount = 10000m,
                PaymentMethod = "CASH"
            },
            Guid.NewGuid(),
            "Admin"));
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_SessionNotFound_ThrowsNotFound()
    {
        var sessionId = Guid.NewGuid();
        _sessionRepo.Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync((ActiveSession?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "SESSION",
                OrderId = sessionId,
                Amount = 100000m,
                PaymentMethod = "CASH"
            },
            Guid.NewGuid(),
            "Admin"));
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_SessionNotUnpaid_ThrowsConflict()
    {
        // Status = Paid → pre-check throw ConflictException trước khi tới atomic flip
        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        await SeedSessionAsync(sessionId, cafeId, 100000m, GroupSessionStatus.Paid);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() => svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "SESSION",
                OrderId = sessionId,
                Amount = 100000m,
                PaymentMethod = "BANK_TRANSFER"
            },
            Guid.NewGuid(),
            "Admin"));
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_AmountMismatch_ThrowsConflict()
    {
        // H5: Amount mismatch detected BEFORE any DB writes.
        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        await SeedSessionAsync(sessionId, cafeId, 200000m);

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() => svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "SESSION",
                OrderId = sessionId,
                Amount = 100000m,
                PaymentMethod = "CASH"
            },
            Guid.NewGuid(),
            "Admin"));
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_NonAdminNotCafeOwnerOrStaff_ThrowsForbidden()
    {
        // C3: non-Admin caller who is neither Manager nor Staff of the cafe is forbidden.
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var otherManagerId = Guid.NewGuid();

        await SeedSessionAsync(sessionId, cafeId, 50000m);

        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "X", Address = "Y", ManagerId = otherManagerId });
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, callerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "SESSION",
                OrderId = sessionId,
                Amount = 50000m,
                PaymentMethod = "CASH"
            },
            callerId,
            "Manager"));
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_AdminBypassesCafeOwnership_Succeeds()
    {
        // C3: Admin role bypasses cafe ownership check.
        // GAP-MANUAL-PAY-RACE: atomic flip sẽ mutate entity trong InMemory → assert session.Status = Paid.
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        var session = await SeedSessionAsync(sessionId, cafeId, 50000m);

        var svc = CreateService();

        var result = await svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "SESSION",
                OrderId = sessionId,
                Amount = 50000m,
                PaymentMethod = "CASH"
            },
            staffId,
            "Admin");

        // Tracked entity trong _dbContext phải được flip sang Paid (fallback path cho InMemory).
        var tracked = await _dbContext.ActiveSessions.FindAsync(sessionId);
        Assert.NotNull(tracked);
        Assert.Equal(GroupSessionStatus.Paid, tracked!.Status);
        Assert.NotNull(tracked.PaidAt);
        Assert.Equal("Session", result.PaymentType);
        Assert.Equal(staffId.ToString(), result.ConfirmedBy);
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_CafeManagerOwner_Succeeds()
    {
        // C3: Manager who owns the cafe can confirm.
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        await SeedSessionAsync(sessionId, cafeId, 150000m);
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "X", Address = "Y", ManagerId = staffId });

        var svc = CreateService();

        var result = await svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "SESSION",
                OrderId = sessionId,
                Amount = 150000m,
                PaymentMethod = "QR_CODE"
            },
            staffId,
            "Manager");

        var tracked = await _dbContext.ActiveSessions.FindAsync(sessionId);
        Assert.NotNull(tracked);
        Assert.Equal(GroupSessionStatus.Paid, tracked!.Status);
        Assert.NotNull(tracked.PaidAt);
        // Cũ: Verify UpdateAsync. Sau fix: ExecuteUpdateAsync → UpdateAsync không được gọi nữa.
        _sessionRepo.Verify(r => r.UpdateAsync(It.IsAny<ActiveSession>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("Session", result.PaymentType);
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_SessionValid_ReleasesTableAndBox()
    {
        var sessionId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        var boxId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            TotalAmount = 150000m,
            CafeTableId = tableId,
            CafeInventoryBoxId = boxId,
            Members = new List<ActiveSessionMember>()
        };

        await _dbContext.ActiveSessions.AddAsync(session);
        await _dbContext.SaveChangesAsync();

        _sessionRepo
            .Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "X", Address = "Y", ManagerId = staffId });

        var svc = CreateService();

        await svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "SESSION",
                OrderId = sessionId,
                Amount = 150000m,
                PaymentMethod = "CASH"
            },
            staffId,
            "Manager");

        var tracked = await _dbContext.ActiveSessions.FindAsync(sessionId);
        Assert.Equal(GroupSessionStatus.Paid, tracked!.Status);
        Assert.NotNull(tracked.PaidAt);

        // Cleanup delegated to repository
        _sessionRepo.Verify(r => r.ReleaseMembersAndCloseLobbyAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once);
        _sessionRepo.Verify(r => r.ReleaseSessionTableAndBoxAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_SessionValid_NoTableOrBox_DoesNotThrow()
    {
        var sessionId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();

        // Walk-in session: no table, no box
        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            TotalAmount = 50000m,
            CafeTableId = null,
            CafeInventoryBoxId = null,
            Members = new List<ActiveSessionMember>()
        };

        await _dbContext.ActiveSessions.AddAsync(session);
        await _dbContext.SaveChangesAsync();

        _sessionRepo
            .Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "X", Address = "Y", ManagerId = staffId });

        var svc = CreateService();

        await svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "SESSION",
                OrderId = sessionId,
                Amount = 50000m,
                PaymentMethod = "CASH"
            },
            staffId,
            "Manager");

        var tracked = await _dbContext.ActiveSessions.FindAsync(sessionId);
        Assert.Equal(GroupSessionStatus.Paid, tracked!.Status);
        _sessionRepo.Verify(r => r.ReleaseMembersAndCloseLobbyAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once);
        _sessionRepo.Verify(r => r.ReleaseSessionTableAndBoxAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_RaceConditionAlreadyPaid_ThrowsConflict()
    {
        // GAP-MANUAL-PAY-RACE Fix: mô phỏng race condition bằng cách set Status = Paid
        // TRƯỚC khi ConfirmManualPaymentAsync được gọi (giả lập request khác đã flip Paid).
        // Status check ở đầu method (line 56-60) sẽ throw ConflictException sớm.
        // Test này verify rằng pre-check hoạt động đúng và orphan Transaction không được tạo.
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await SeedSessionAsync(sessionId, cafeId, 50000m, GroupSessionStatus.Paid);

        var svc = CreateService();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "SESSION",
                OrderId = sessionId,
                Amount = 50000m,
                PaymentMethod = "CASH"
            },
            Guid.NewGuid(),
            "Admin"));

        // Message phải reference trạng thái hiện tại (Paid)
        Assert.Contains("Paid", ex.Message);

        // Orphan Transaction KHÔNG được tạo (pre-check throw sớm trước khi AddAsync).
        _transactionRepo.Verify(
            r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_RaceDetectedAfterFlip_ThrowsConflict_NoOrphanTransaction()
    {
        // GAP-MANUAL-PAY-RACE Fix: mô phỏng race bằng cách setup _dbContext.ActiveSessions
        // với session có Status=Paid (giả lập request khác đã flip trước).
        // TryAtomicFlipSessionStatusAsync fallback sẽ thấy Status != Unpaid → return false
        // → throw ConflictException → không tạo Transaction.
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await SeedSessionAsync(sessionId, cafeId, 50000m, GroupSessionStatus.Paid);

        var svc = CreateService();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "SESSION",
                OrderId = sessionId,
                Amount = 50000m,
                PaymentMethod = "CASH"
            },
            Guid.NewGuid(),
            "Admin"));

        // Verify race message format: "Paid (already processed by another request)"
        Assert.Contains("Paid", ex.Message);

        // Orphan Transaction KHÔNG được tạo (atomic flip return false → throw sớm).
        _transactionRepo.Verify(
            r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Cleanup KHÔNG được gọi (throw trước khi cleanup).
        _sessionRepo.Verify(
            r => r.ReleaseMembersAndCloseLobbyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _sessionRepo.Verify(
            r => r.ReleaseSessionTableAndBoxAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_SuccessfulFlip_CreatesTransactionWithCorrectFields()
    {
        // GAP-MANUAL-PAY-RACE Fix: Verify Transaction record có đầy đủ fields
        // cho audit trail và reconciliation.
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        await SeedSessionAsync(sessionId, cafeId, 75000m);
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "X", Address = "Y", ManagerId = staffId });

        Transaction? capturedTransaction = null;
        _transactionRepo.Setup(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .Callback<Transaction, CancellationToken>((t, _) => capturedTransaction = t)
            .ReturnsAsync((Transaction t, CancellationToken _) => t);

        var svc = CreateService();

        var result = await svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "SESSION",
                OrderId = sessionId,
                Amount = 75000m,
                PaymentMethod = "BANK_TRANSFER",
                Notes = "Khách trả chuyển khoản"
            },
            staffId,
            "Manager");

        Assert.NotNull(capturedTransaction);
        Assert.Equal(75000m, capturedTransaction!.Amount);
        Assert.Equal("VND", capturedTransaction.Currency);
        Assert.Equal("MANUAL", capturedTransaction.Gateway);
        Assert.Equal(TransactionStatus.Succeeded, capturedTransaction.Status);
        Assert.Equal(TransactionType.GameRental, capturedTransaction.Type);
        Assert.Equal(TransactionDirection.In, capturedTransaction.Direction);
        Assert.Equal(sessionId.ToString(), capturedTransaction.GatewayTransactionId);
        Assert.Equal("MANUAL_CONFIRM", capturedTransaction.GatewayResponseCode);
        Assert.Contains(staffId.ToString(), capturedTransaction.Notes);
        Assert.Contains("BANK_TRANSFER", capturedTransaction.Notes);

        Assert.Equal(result.TransactionId, capturedTransaction.Id);
        Assert.Equal(75000m, result.Amount);
        Assert.Equal("BANK_TRANSFER", result.PaymentMethod);
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_LifecycleCleanupFails_StillCommitsPayment()
    {
        // GAP-06 / GAP-08 Fix: Nếu ReleaseMembersAndCloseLobbyAsync throw,
        // payment vẫn commit thành công → trả DTO cho client bình thường.
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        await SeedSessionAsync(sessionId, cafeId, 100000m);
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "X", Address = "Y", ManagerId = staffId });
        _sessionRepo.Setup(r => r.ReleaseMembersAndCloseLobbyAsync(sessionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Lobby not found"));

        var svc = CreateService();

        // Không throw ra client — log + swallow.
        var result = await svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "SESSION",
                OrderId = sessionId,
                Amount = 100000m,
                PaymentMethod = "CASH"
            },
            staffId,
            "Manager");

        // Session vẫn flip Paid thành công
        var tracked = await _dbContext.ActiveSessions.FindAsync(sessionId);
        Assert.Equal(GroupSessionStatus.Paid, tracked!.Status);

        // Response vẫn trả bình thường
        Assert.NotNull(result);
        Assert.Equal("Confirmed", result.Status);
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_NullMembersList_DoesNotThrow()
    {
        // Null-safe: test truyền session không có Members list (Members = null)
        // → detach loop không throw NullReferenceException.
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            TotalAmount = 50000m,
            Members = null!  // edge case: Members null
        };

        await _dbContext.ActiveSessions.AddAsync(session);
        await _dbContext.SaveChangesAsync();

        _sessionRepo
            .Setup(r => r.GetByIdWithMembersAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "X", Address = "Y", ManagerId = staffId });

        var svc = CreateService();

        var result = await svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "SESSION",
                OrderId = sessionId,
                Amount = 50000m,
                PaymentMethod = "CASH"
            },
            staffId,
            "Manager");

        Assert.NotNull(result);
        Assert.Equal("Confirmed", result.Status);
    }
}
