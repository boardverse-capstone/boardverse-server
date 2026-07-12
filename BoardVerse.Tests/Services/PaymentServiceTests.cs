using BoardVerse.Core.DTOs.Payment;
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

public class PaymentServiceTests
{
    private readonly Mock<IBookingDepositService> _mockDepositService;
    private readonly Mock<ICafeRepository> _mockCafeRepo;
    private readonly Mock<ICafeSettlementRepository> _mockSettlementRepo;
    private readonly Mock<IPaymentMasterAccountRepository> _mockMasterAccountRepo;
    private readonly Mock<ISePayClient> _mockSePayClient;
    private readonly Mock<ILogger<PaymentService>> _mockLogger;
    private readonly PaymentService _service;

    public PaymentServiceTests()
    {
        _mockDepositService = new Mock<IBookingDepositService>();
        _mockCafeRepo = new Mock<ICafeRepository>();
        _mockSettlementRepo = new Mock<ICafeSettlementRepository>();
        _mockMasterAccountRepo = new Mock<IPaymentMasterAccountRepository>();
        _mockSePayClient = new Mock<ISePayClient>();
        _mockLogger = new Mock<ILogger<PaymentService>>();

        _service = new PaymentService(
            _mockDepositService.Object,
            _mockCafeRepo.Object,
            _mockSettlementRepo.Object,
            _mockMasterAccountRepo.Object,
            _mockSePayClient.Object,
            _mockLogger.Object);
    }

    #region CreateDepositPaymentAsync

    [Fact]
    public async Task CreateDepositPayment_DepositNotFound_ThrowsNotFoundException()
    {
        var request = new CreatePaymentRequestDto { DepositId = Guid.NewGuid() };
        _mockDepositService.Setup(s => s.GetByIdAsync(request.DepositId)).ReturnsAsync((BookingDeposit?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateDepositPaymentAsync(request, Guid.NewGuid()));

        Assert.Contains("deposit", ex.Message.ToLower());
    }

    [Fact]
    public async Task CreateDepositPayment_DepositAlreadyPaid_ThrowsConflictException()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Paid);
        var request = new CreatePaymentRequestDto { DepositId = depositId, Amount = 50_000m };

        _mockDepositService.Setup(s => s.GetByIdAsync(depositId)).ReturnsAsync(deposit);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateDepositPaymentAsync(request, Guid.NewGuid()));

        Assert.Contains("đã được xử lý", ex.Message);
    }

    [Fact]
    public async Task CreateDepositPayment_ValidRequest_ReturnsPaymentUrl()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Pending);
        deposit.OrderId = string.Empty;

        var request = new CreatePaymentRequestDto { DepositId = depositId, Amount = 40_000m };
        var paymentUrl = "https://sepay.vn/pay/BV12345678";

        _mockDepositService.Setup(s => s.GetByIdAsync(depositId)).ReturnsAsync(deposit);
        _mockSePayClient.Setup(c => c.CreatePaymentAsync(It.IsAny<CreatePaymentRequest>(), default))
            .ReturnsAsync(paymentUrl);

        var result = await _service.CreateDepositPaymentAsync(request, Guid.NewGuid());

        Assert.Equal(paymentUrl, result.PaymentUrl);
        Assert.StartsWith("BV", result.OrderId);
        _mockSePayClient.Verify(c => c.CreatePaymentAsync(
            It.Is<CreatePaymentRequest>(p => p.OrderId.StartsWith("BV")),
            default), Times.Once);
    }

    [Fact]
    public async Task CreateDepositPayment_CallsSePayWithCorrectMetadata()
    {
        var depositId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Pending);
        deposit.OrderId = string.Empty;
        deposit.ActiveSessionId = Guid.NewGuid();

        var request = new CreatePaymentRequestDto
        {
            DepositId = depositId,
            Amount = 40_000m,
            CustomerEmail = "test@example.com"
        };

        _mockDepositService.Setup(s => s.GetByIdAsync(depositId)).ReturnsAsync(deposit);
        _mockSePayClient.Setup(c => c.CreatePaymentAsync(It.IsAny<CreatePaymentRequest>(), default))
            .ReturnsAsync("https://sepay.vn/pay/BV12345678");

        await _service.CreateDepositPaymentAsync(request, userId);

        _mockSePayClient.Verify(c => c.CreatePaymentAsync(
            It.Is<CreatePaymentRequest>(p =>
                p.Metadata["depositId"] == depositId.ToString() &&
                p.Metadata["activeSessionId"] == deposit.ActiveSessionId.ToString() &&
                p.Metadata["userId"] == userId.ToString()),
            default), Times.Once);
    }

    #endregion

    #region HandleSePayWebhookAsync

    [Fact]
    public async Task HandleSePayWebhook_MissingBothIds_DoesNotProcess()
    {
        var webhook = new SePayWebhookDto
        {
            OrderId = string.Empty,
            GatewayTransactionId = string.Empty,
            Status = "success"
        };

        await _service.HandleSePayWebhookAsync(webhook);

        _mockDepositService.Verify(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>()), Times.Never);
        _mockDepositService.Verify(s => s.GetByOrderIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleSePayWebhook_InvalidSignature_DoesNotProcess()
    {
        var webhook = new SePayWebhookDto
        {
            OrderId = "BV00001",
            GatewayTransactionId = "TXN001",
            Status = "success",
            Amount = 50_000m,
            Signature = "invalid-signature"
        };

        _mockSePayClient.Setup(c => c.VerifyWebhookAsync("invalid-signature", It.IsAny<string>()))
            .ReturnsAsync(false);

        await _service.HandleSePayWebhookAsync(webhook);

        _mockDepositService.Verify(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>()), Times.Never);
        _mockDepositService.Verify(s => s.GetByOrderIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleSePayWebhook_DepositNotFound_DoesNotProcess()
    {
        var webhook = new SePayWebhookDto
        {
            OrderId = "BV99999999",
            Status = "success",
            Amount = 50_000m
        };

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(It.IsAny<string>()))
            .ReturnsAsync((BookingDeposit?)null);

        await _service.HandleSePayWebhookAsync(webhook);

        _mockDepositService.Verify(s => s.MarkAsPaidAsync(It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task HandleSePayWebhook_SuccessWithAmountMismatch_DoesNotUpdateDeposit()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Pending);
        deposit.Amount = 50_000m;

        var webhook = new SePayWebhookDto
        {
            OrderId = deposit.OrderId,
            Status = "success",
            Amount = 25_000m // mismatch!
        };

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(deposit.OrderId)).ReturnsAsync(deposit);

        await _service.HandleSePayWebhookAsync(webhook);

        _mockDepositService.Verify(s => s.MarkAsPaidAsync(It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
        Assert.Equal(BookingDepositStatus.Pending, deposit.Status);
    }

    [Fact]
    public async Task HandleSePayWebhook_SuccessWithValidAmount_CallsMarkAsPaid()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Pending);
        deposit.Amount = 50_000m;

        var webhook = new SePayWebhookDto
        {
            OrderId = deposit.OrderId,
            GatewayTransactionId = "TXN-REAL-001",
            Status = "success",
            Amount = 50_000m
        };

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(deposit.OrderId)).ReturnsAsync(deposit);

        await _service.HandleSePayWebhookAsync(webhook);

        _mockDepositService.Verify(s => s.MarkAsPaidAsync(depositId, "TXN-REAL-001"), Times.Once);
    }

    [Fact]
    public async Task HandleSePayWebhook_SuccessIdempotent_SkipsAlreadyPaid()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Paid);

        var webhook = new SePayWebhookDto
        {
            OrderId = deposit.OrderId,
            Status = "success",
            Amount = deposit.Amount
        };

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(deposit.OrderId)).ReturnsAsync(deposit);

        await _service.HandleSePayWebhookAsync(webhook);

        _mockDepositService.Verify(s => s.MarkAsPaidAsync(It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task HandleSePayWebhook_FailedCancelsPendingDeposit_CallsMarkAsRefunded()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Pending);

        var webhook = new SePayWebhookDto
        {
            OrderId = deposit.OrderId,
            Status = "failed",
            Amount = deposit.Amount
        };

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(deposit.OrderId)).ReturnsAsync(deposit);

        await _service.HandleSePayWebhookAsync(webhook);

        _mockDepositService.Verify(s => s.MarkAsRefundedAsync(depositId), Times.Once);
    }

    [Fact]
    public async Task HandleSePayWebhook_FailedIdempotent_SkipsNonPendingDeposit()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Released);

        var webhook = new SePayWebhookDto
        {
            OrderId = deposit.OrderId,
            Status = "failed",
            Amount = deposit.Amount
        };

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(deposit.OrderId)).ReturnsAsync(deposit);

        await _service.HandleSePayWebhookAsync(webhook);

        _mockDepositService.Verify(s => s.MarkAsRefundedAsync(It.IsAny<Guid>()), Times.Never);
    }

    #endregion

    #region RefundDepositAsync

    [Fact]
    public async Task RefundDeposit_DepositNotFound_ThrowsNotFoundException()
    {
        _mockDepositService.Setup(s => s.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((BookingDeposit?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.RefundDepositAsync(Guid.NewGuid(), "test"));

        Assert.Contains("deposit", ex.Message.ToLower());
    }

    [Fact]
    public async Task RefundDeposit_NotPaid_ThrowsConflictException()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Pending);

        _mockDepositService.Setup(s => s.GetByIdAsync(depositId)).ReturnsAsync(deposit);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.RefundDepositAsync(depositId, "test"));

        Assert.Contains("'Pending'", ex.Message);
    }

    [Fact]
    public async Task RefundDeposit_PolicyNone_CallsForfeitAsync()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Paid);
        deposit.RefundPolicy = DepositRefundPolicy.None;

        _mockDepositService.Setup(s => s.GetByIdAsync(depositId)).ReturnsAsync(deposit);
        _mockDepositService.Setup(s => s.ForfeitAsync(depositId)).ReturnsAsync(deposit);

        var result = await _service.RefundDepositAsync(depositId, "Customer no-showed");

        _mockDepositService.Verify(s => s.ForfeitAsync(depositId), Times.Once);
        _mockDepositService.Verify(s => s.MarkAsRefundedAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task RefundDeposit_PolicyFullOrPartial_CallsMarkAsRefunded()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Paid);
        deposit.RefundPolicy = DepositRefundPolicy.Full;
        deposit.Amount = 50_000m;

        _mockDepositService.Setup(s => s.GetByIdAsync(depositId)).ReturnsAsync(deposit);
        _mockDepositService.Setup(s => s.MarkAsRefundedAsync(depositId)).ReturnsAsync(deposit);

        var result = await _service.RefundDepositAsync(depositId, "Cancelled by manager");

        _mockDepositService.Verify(s => s.MarkAsRefundedAsync(depositId), Times.Once);
        _mockDepositService.Verify(s => s.ForfeitAsync(It.IsAny<Guid>()), Times.Never);
    }

    #endregion

    #region ProcessExpiredDepositsAsync

    [Fact]
    public async Task ProcessExpiredDepositsAsync_DelegatesToDepositService()
    {
        _mockDepositService.Setup(s => s.ProcessExpiredDepositsAsync()).Returns(Task.CompletedTask);

        await _service.ProcessExpiredDepositsAsync();

        _mockDepositService.Verify(s => s.ProcessExpiredDepositsAsync(), Times.Once);
    }

    #endregion

    #region Helpers

    private static BookingDeposit CreateTestDeposit(Guid? id = null, BookingDepositStatus status = BookingDepositStatus.Pending, Guid? cafeId = null)
    {
        return new BookingDeposit
        {
            Id = id ?? Guid.NewGuid(),
            OrderId = $"BV{DateTime.UtcNow.Ticks % 100_000_000:D8}",
            ActiveSessionId = Guid.NewGuid(),
            CafeId = cafeId ?? Guid.NewGuid(),
            CafeManagerId = Guid.NewGuid(),
            MasterAccountId = Guid.NewGuid(),
            Amount = 50_000m,
            RefundPolicy = DepositRefundPolicy.Full,
            Status = status,
            TransferContent = "Transfer content",
            SePayTransactionId = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
            Cafe = new Cafe
            {
                Id = cafeId ?? Guid.NewGuid(),
                Name = "Test Cafe",
                Address = "123 Test St",
                BasePrice = 100_000m,
                IsActive = true,
                BillingModel = CafePartnerBillingModel.TimeBased,
                TieredBlockMinutes = 15
            }
        };
    }

    private static Cafe CreateTestCafe(Guid id, decimal basePrice)
    {
        return new Cafe
        {
            Id = id,
            Name = "Test Cafe",
            Address = "123 Test St",
            BasePrice = basePrice,
            IsActive = true,
            BillingModel = CafePartnerBillingModel.TimeBased,
            TieredBlockMinutes = 15,
            TieredBlockRate = 10_000m
        };
    }

    #endregion
}
