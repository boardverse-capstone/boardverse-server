using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

public class ManualPaymentServiceTests
{
    private readonly Mock<ITransactionRepository> _transactionRepo = new();
    private readonly Mock<IBookingDepositRepository> _depositRepo = new();
    private readonly Mock<IActiveSessionRepository> _sessionRepo = new();
    private readonly Mock<ILogger<ManualPaymentService>> _logger = new();

    private ManualPaymentService CreateService() => new(
        _transactionRepo.Object, _depositRepo.Object, _sessionRepo.Object, _logger.Object);

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
            Guid.NewGuid()));
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_WithInvalidPaymentMethod_ThrowsArgument()
    {
        var svc = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "Deposit",
                OrderId = Guid.NewGuid(),
                Amount = 10000m,
                PaymentMethod = "BITCOIN"
            },
            Guid.NewGuid()));
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_DepositNotFound_ThrowsNotFound()
    {
        var depositId = Guid.NewGuid();
        _depositRepo.Setup(r => r.GetByIdAsync(depositId)).ReturnsAsync((BookingDeposit?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "Deposit",
                OrderId = depositId,
                Amount = 10000m,
                PaymentMethod = "CASH"
            },
            Guid.NewGuid()));
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_DepositNotPending_ThrowsConflict()
    {
        var depositId = Guid.NewGuid();
        _depositRepo.Setup(r => r.GetByIdAsync(depositId)).ReturnsAsync(new BookingDeposit
        {
            Id = depositId,
            Status = BookingDepositStatus.Paid
        });

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() => svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "Deposit",
                OrderId = depositId,
                Amount = 10000m,
                PaymentMethod = "CASH"
            },
            Guid.NewGuid()));
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_DepositValid_SetsPaidAndCreatesTransaction()
    {
        var depositId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var deposit = new BookingDeposit { Id = depositId, Status = BookingDepositStatus.Pending };

        _depositRepo.Setup(r => r.GetByIdAsync(depositId)).ReturnsAsync(deposit);

        Transaction? captured = null;
        _transactionRepo.Setup(r => r.AddAsync(It.IsAny<Transaction>(), default))
            .Callback<Transaction, CancellationToken>((t, _) => captured = t)
            .ReturnsAsync((Transaction t, CancellationToken _) => t);

        var svc = CreateService();

        var result = await svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "DEPOSIT",
                OrderId = depositId,
                Amount = 50000m,
                PaymentMethod = "CASH",
                Notes = "Customer paid cash"
            },
            staffId);

        Assert.Equal(BookingDepositStatus.Paid, deposit.Status);
        Assert.NotNull(deposit.PaidAt);
        _depositRepo.Verify(r => r.UpdateAsync(deposit), Times.Once);
        Assert.NotNull(captured);
        Assert.Equal(TransactionStatus.Succeeded, captured!.Status);
        Assert.Equal(TransactionType.BookingDeposit, captured.Type);
        Assert.Equal(50000m, captured.Amount);
        Assert.Equal(depositId.ToString(), captured.GatewayTransactionId);
        Assert.Equal("Confirmed", result.Status);
        Assert.Equal(staffId.ToString(), result.ConfirmedBy);
        Assert.Equal("Deposit", result.PaymentType);
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_SessionNotFound_ThrowsNotFound()
    {
        var sessionId = Guid.NewGuid();
        _sessionRepo.Setup(r => r.GetByIdWithMembersAsync(sessionId)).ReturnsAsync((ActiveSession?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "Session",
                OrderId = sessionId,
                Amount = 100000m,
                PaymentMethod = "CASH"
            },
            Guid.NewGuid()));
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_SessionNotUnpaid_ThrowsConflict()
    {
        var sessionId = Guid.NewGuid();
        _sessionRepo.Setup(r => r.GetByIdWithMembersAsync(sessionId)).ReturnsAsync(new ActiveSession
        {
            Id = sessionId,
            Status = GroupSessionStatus.Paid
        });

        var svc = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() => svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "Session",
                OrderId = sessionId,
                Amount = 100000m,
                PaymentMethod = "BANK_TRANSFER"
            },
            Guid.NewGuid()));
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_SessionValid_SetsPaid()
    {
        var sessionId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var session = new ActiveSession { Id = sessionId, Status = GroupSessionStatus.Unpaid };

        _sessionRepo.Setup(r => r.GetByIdWithMembersAsync(sessionId)).ReturnsAsync(session);
        _transactionRepo.Setup(r => r.AddAsync(It.IsAny<Transaction>(), default))
            .ReturnsAsync((Transaction t, CancellationToken _) => t);

        var svc = CreateService();

        var result = await svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "Session",
                OrderId = sessionId,
                Amount = 150000m,
                PaymentMethod = "QR_CODE"
            },
            staffId);

        Assert.Equal(GroupSessionStatus.Paid, session.Status);
        Assert.NotNull(session.PaidAt);
        _sessionRepo.Verify(r => r.UpdateAsync(session), Times.Once);
        Assert.Equal("Session", result.PaymentType);
        Assert.Equal(staffId.ToString(), result.ConfirmedBy);
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
            CafeTableId = tableId,
            CafeInventoryBoxId = boxId
        };

        _sessionRepo.Setup(r => r.GetByIdWithMembersAsync(sessionId)).ReturnsAsync(session);
        _transactionRepo.Setup(r => r.AddAsync(It.IsAny<Transaction>(), default))
            .ReturnsAsync((Transaction t, CancellationToken _) => t);

        var svc = CreateService();

        await svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "Session",
                OrderId = sessionId,
                Amount = 150000m,
                PaymentMethod = "CASH"
            },
            staffId);

        // Session marked Paid
        Assert.Equal(GroupSessionStatus.Paid, session.Status);
        Assert.NotNull(session.PaidAt);

        // Cleanup delegated to repository
        _sessionRepo.Verify(r => r.CompleteSessionPaymentCleanupAsync(sessionId), Times.Once);

        _sessionRepo.Verify(r => r.SaveChangesAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_SessionValid_NoTableOrBox_DoesNotThrow()
    {
        var sessionId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        // Walk-in session: no table, no box
        var session = new ActiveSession
        {
            Id = sessionId,
            Status = GroupSessionStatus.Unpaid,
            CafeTableId = null,
            CafeInventoryBoxId = null
        };

        _sessionRepo.Setup(r => r.GetByIdWithMembersAsync(sessionId)).ReturnsAsync(session);
        _transactionRepo.Setup(r => r.AddAsync(It.IsAny<Transaction>(), default))
            .ReturnsAsync((Transaction t, CancellationToken _) => t);

        var svc = CreateService();

        await svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "Session",
                OrderId = sessionId,
                Amount = 50000m,
                PaymentMethod = "CASH"
            },
            staffId);

        Assert.Equal(GroupSessionStatus.Paid, session.Status);
        // Cleanup is still invoked (repository implementation handles null IDs gracefully).
        _sessionRepo.Verify(r => r.CompleteSessionPaymentCleanupAsync(sessionId), Times.Once);
    }

    [Fact]
    public async Task ConfirmManualPaymentAsync_SessionValid_CleanupRunsEvenOnRepeatedCalls()
    {
        // Idempotency: repeated manual confirms must not throw — cleanup is repository-managed.
        var sessionId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            Status = GroupSessionStatus.Unpaid
        };

        _sessionRepo.Setup(r => r.GetByIdWithMembersAsync(sessionId)).ReturnsAsync(session);
        _transactionRepo.Setup(r => r.AddAsync(It.IsAny<Transaction>(), default))
            .ReturnsAsync((Transaction t, CancellationToken _) => t);

        var svc = CreateService();

        // First call succeeds.
        await svc.ConfirmManualPaymentAsync(
            new ManualPaymentConfirmRequestDto
            {
                PaymentType = "Session",
                OrderId = sessionId,
                Amount = 50000m,
                PaymentMethod = "CASH"
            },
            staffId);

        // Cleanup called once.
        _sessionRepo.Verify(r => r.CompleteSessionPaymentCleanupAsync(sessionId), Times.Once);
    }
}
