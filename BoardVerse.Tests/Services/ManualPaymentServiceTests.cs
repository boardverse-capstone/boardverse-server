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
    private readonly Mock<ICafeRepository> _cafeRepo = new();
    private readonly Mock<ILogger<ManualPaymentService>> _logger = new();

    private ManualPaymentService CreateService() => new(
        _transactionRepo.Object, _depositRepo.Object, _sessionRepo.Object, _cafeRepo.Object, _logger.Object);

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
        _sessionRepo.Setup(r => r.GetByIdWithMembersAsync(sessionId)).ReturnsAsync((ActiveSession?)null);

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
        _sessionRepo.Setup(r => r.GetByIdWithMembersAsync(sessionId)).ReturnsAsync(new ActiveSession
        {
            Id = sessionId,
            CafeId = Guid.NewGuid(),
            Status = GroupSessionStatus.Unpaid,
            TotalAmount = 200000m
        });

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

        _sessionRepo.Setup(r => r.GetByIdWithMembersAsync(sessionId)).ReturnsAsync(new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            TotalAmount = 50000m
        });
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync(new Cafe { Id = cafeId, Name = "X", Address = "Y", ManagerId = otherManagerId });
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, callerId)).ReturnsAsync(false);

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
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            TotalAmount = 50000m
        };

        _sessionRepo.Setup(r => r.GetByIdWithMembersAsync(sessionId)).ReturnsAsync(session);
        _transactionRepo.Setup(r => r.AddAsync(It.IsAny<Transaction>(), default))
            .ReturnsAsync((Transaction t, CancellationToken _) => t);

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

        Assert.Equal(GroupSessionStatus.Paid, session.Status);
        Assert.NotNull(session.PaidAt);
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

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            TotalAmount = 150000m
        };

        _sessionRepo.Setup(r => r.GetByIdWithMembersAsync(sessionId)).ReturnsAsync(session);
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync(new Cafe { Id = cafeId, Name = "X", Address = "Y", ManagerId = staffId });
        _transactionRepo.Setup(r => r.AddAsync(It.IsAny<Transaction>(), default))
            .ReturnsAsync((Transaction t, CancellationToken _) => t);

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

        Assert.Equal(GroupSessionStatus.Paid, session.Status);
        Assert.NotNull(session.PaidAt);
        _sessionRepo.Verify(r => r.UpdateAsync(session), Times.Once);
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
            CafeInventoryBoxId = boxId
        };

        _sessionRepo.Setup(r => r.GetByIdWithMembersAsync(sessionId)).ReturnsAsync(session);
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync(new Cafe { Id = cafeId, Name = "X", Address = "Y", ManagerId = staffId });
        _transactionRepo.Setup(r => r.AddAsync(It.IsAny<Transaction>(), default))
            .ReturnsAsync((Transaction t, CancellationToken _) => t);

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
        var cafeId = Guid.NewGuid();

        // Walk-in session: no table, no box
        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            TotalAmount = 50000m,
            CafeTableId = null,
            CafeInventoryBoxId = null
        };

        _sessionRepo.Setup(r => r.GetByIdWithMembersAsync(sessionId)).ReturnsAsync(session);
        _cafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId)).ReturnsAsync(new Cafe { Id = cafeId, Name = "X", Address = "Y", ManagerId = staffId });
        _transactionRepo.Setup(r => r.AddAsync(It.IsAny<Transaction>(), default))
            .ReturnsAsync((Transaction t, CancellationToken _) => t);

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

        Assert.Equal(GroupSessionStatus.Paid, session.Status);
        _sessionRepo.Verify(r => r.CompleteSessionPaymentCleanupAsync(sessionId), Times.Once);
    }
}
