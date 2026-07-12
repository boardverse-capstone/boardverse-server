using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using BoardVerse.Services.Services.Payments;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

public class SettlementServiceTests
{
    private readonly Mock<IBookingDepositRepository> _mockDepositRepo;
    private readonly Mock<ICafeSettlementRepository> _mockSettlementRepo;
    private readonly Mock<ICafeRepository> _mockCafeRepo;
    private readonly Mock<IPaymentMasterAccountRepository> _mockMasterAccountRepo;
    private readonly Mock<IActiveSessionRepository> _mockSessionRepo;
    private readonly Mock<ISePayClient> _mockSePayClient;
    private readonly Mock<ILogger<SettlementService>> _mockLogger;
    private readonly SettlementService _service;

    public SettlementServiceTests()
    {
        _mockDepositRepo = new Mock<IBookingDepositRepository>();
        _mockSettlementRepo = new Mock<ICafeSettlementRepository>();
        _mockCafeRepo = new Mock<ICafeRepository>();
        _mockMasterAccountRepo = new Mock<IPaymentMasterAccountRepository>();
        _mockSessionRepo = new Mock<IActiveSessionRepository>();
        _mockSePayClient = new Mock<ISePayClient>();
        _mockLogger = new Mock<ILogger<SettlementService>>();

        _service = new SettlementService(
            _mockDepositRepo.Object,
            _mockSettlementRepo.Object,
            _mockCafeRepo.Object,
            _mockMasterAccountRepo.Object,
            _mockSessionRepo.Object,
            _mockSePayClient.Object,
            _mockLogger.Object);
    }

    #region ReleaseSessionDepositAsync

    [Fact]
    public async Task ReleaseSessionDepositAsync_SessionNotPaid_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Active,
            DepositAppliedAmount = 50_000m
        };

        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test Cafe", Address = "123 St" });
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.ReleaseSessionDepositAsync(cafeId, sessionId, sessionId));

        Assert.Contains("đã thanh toán", ex.Message);
    }

    [Fact]
    public async Task ReleaseSessionDepositAsync_NoDepositApplied_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Paid,
            DepositAppliedAmount = 0
        };

        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test Cafe", Address = "123 St" });
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.ReleaseSessionDepositAsync(cafeId, sessionId, sessionId));

        Assert.Contains("deposit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReleaseSessionDepositAsync_DepositNotFound_ThrowsNotFoundException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Paid,
            DepositAppliedAmount = 50_000m
        };

        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test Cafe", Address = "123 St", ManagerId = Guid.NewGuid() });
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);
        _mockMasterAccountRepo.Setup(r => r.GetActiveAsync())
            .ReturnsAsync(new PaymentMasterAccount { Id = Guid.NewGuid(), AccountHolder = "Test", IsActive = true });
        _mockDepositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId)).ReturnsAsync((BookingDeposit?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.ReleaseSessionDepositAsync(cafeId, sessionId, sessionId));
    }

    [Fact]
    public async Task ReleaseSessionDepositAsync_DepositNotPaid_ThrowsConflictException()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var depositId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Paid,
            DepositAppliedAmount = 50_000m
        };

        var deposit = new BookingDeposit
        {
            Id = depositId,
            ActiveSessionId = sessionId,
            Amount = 50_000m,
            Status = BookingDepositStatus.Pending
        };

        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test Cafe", Address = "123 St", ManagerId = Guid.NewGuid() });
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);
        _mockMasterAccountRepo.Setup(r => r.GetActiveAsync())
            .ReturnsAsync(new PaymentMasterAccount { Id = Guid.NewGuid(), AccountHolder = "Test", IsActive = true });
        _mockDepositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId)).ReturnsAsync(deposit);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.ReleaseSessionDepositAsync(cafeId, sessionId, sessionId));

        Assert.Contains("PAID", ex.Message);
    }

    [Fact]
    public async Task ReleaseSessionDepositAsync_TransferSucceeds_StatusSucceeded()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var depositId = Guid.NewGuid();
        var masterAccountId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Paid,
            DepositAppliedAmount = 50_000m
        };

        var deposit = new BookingDeposit
        {
            Id = depositId,
            ActiveSessionId = sessionId,
            Amount = 50_000m,
            Status = BookingDepositStatus.Paid
        };

        var masterAccount = new PaymentMasterAccount
        {
            Id = masterAccountId,
            AccountHolder = "BoardVerse Master",
            VirtualAccountNumber = "1234567890",
            IsActive = true
        };

        var transferResponse = new SePayTransferResponse
        {
            IsSuccess = true,
            TransferId = "TXN-TRANSFER-001"
        };

        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test Cafe", Address = "123 St", ManagerId = Guid.NewGuid() });
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);
        _mockDepositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId)).ReturnsAsync(deposit);
        _mockMasterAccountRepo.Setup(r => r.GetActiveAsync()).ReturnsAsync(masterAccount);
        _mockSePayClient.Setup(c => c.CreateTransferAsync(It.IsAny<CreateTransferRequest>(), default))
            .ReturnsAsync(transferResponse);

        var result = await _service.ReleaseSessionDepositAsync(cafeId, sessionId, sessionId);

        Assert.Equal(CafeSettlementStatus.Succeeded, result.Status);
        Assert.Equal("TXN-TRANSFER-001", result.SePayTransferId);
        Assert.Equal(50_000m, result.DepositAmount);
        Assert.Equal(50_000m, result.NetTransferAmount);
    }

    [Fact]
    public async Task ReleaseSessionDepositAsync_TransferFails_StatusFailedAndLogsError()
    {
        var cafeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var depositId = Guid.NewGuid();
        var masterAccountId = Guid.NewGuid();

        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Paid,
            DepositAppliedAmount = 50_000m
        };

        var deposit = new BookingDeposit
        {
            Id = depositId,
            ActiveSessionId = sessionId,
            Amount = 50_000m,
            Status = BookingDepositStatus.Paid
        };

        var masterAccount = new PaymentMasterAccount
        {
            Id = masterAccountId,
            AccountHolder = "BoardVerse Master",
            VirtualAccountNumber = "1234567890",
            IsActive = true
        };

        _mockCafeRepo.Setup(r => r.GetActiveByIdAsync(cafeId))
            .ReturnsAsync(new Cafe { Id = cafeId, Name = "Test Cafe", Address = "123 St", ManagerId = Guid.NewGuid() });
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId)).ReturnsAsync(session);
        _mockDepositRepo.Setup(r => r.GetByActiveSessionIdAsync(sessionId)).ReturnsAsync(deposit);
        _mockMasterAccountRepo.Setup(r => r.GetActiveAsync()).ReturnsAsync(masterAccount);
        _mockSePayClient.Setup(c => c.CreateTransferAsync(It.IsAny<CreateTransferRequest>(), default))
            .ThrowsAsync(new HttpRequestException("SePay API is down"));

        var result = await _service.ReleaseSessionDepositAsync(cafeId, sessionId, sessionId);

        Assert.Equal(CafeSettlementStatus.Failed, result.Status);
        Assert.NotNull(result.FailureReason);
        _mockSettlementRepo.Verify(r => r.SaveChangesAsync(), Times.Exactly(2));
    }

    #endregion

    #region GetPendingSettlementsAsync

    [Fact]
    public async Task GetPendingSettlementsAsync_ReturnsSettlements()
    {
        var cafeId = Guid.NewGuid();

        var settlements = new List<CafeSettlement>
        {
            new CafeSettlement
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                Status = CafeSettlementStatus.Pending,
                DepositAmount = 50_000m
            }
        };

        _mockSettlementRepo.Setup(r => r.GetPendingAsync(cafeId)).ReturnsAsync(settlements);

        var result = await _service.GetPendingSettlementsAsync(cafeId);

        Assert.Single(result);
        Assert.Equal(CafeSettlementStatus.Pending, result[0].Status);
    }

    #endregion
}
