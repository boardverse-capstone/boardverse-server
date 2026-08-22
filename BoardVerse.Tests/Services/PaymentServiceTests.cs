using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.DTOs.Session;
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
    private readonly Mock<IActiveSessionRepository> _mockSessionRepo;
    private readonly Mock<IPaymentGatewayService> _mockGateway;
    private readonly Mock<ISePayClient> _mockSePayClient;
    private readonly Mock<ISePayAccountService> _mockSePayAccountService;
    private readonly Mock<IWalletService> _mockWalletService;
    private readonly Mock<IActiveSessionService> _mockActiveSessionService;
    private readonly Mock<IPaymentWebhookAuditRepository> _mockWebhookAuditRepository;
    private readonly Mock<ILogger<PaymentService>> _mockLogger;
    private readonly PaymentService _service;

    public PaymentServiceTests()
    {
        _mockDepositService = new Mock<IBookingDepositService>();
        _mockCafeRepo = new Mock<ICafeRepository>();
        _mockSettlementRepo = new Mock<ICafeSettlementRepository>();
        _mockSessionRepo = new Mock<IActiveSessionRepository>();
        _mockSessionRepo.Setup(r => r.GetAllUnpaidAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActiveSession>());
        _mockGateway = new Mock<IPaymentGatewayService>();
        _mockSePayClient = new Mock<ISePayClient>();
        _mockSePayAccountService = new Mock<ISePayAccountService>();
        _mockWalletService = new Mock<IWalletService>();
        _mockActiveSessionService = new Mock<IActiveSessionService>();
        _mockWebhookAuditRepository = new Mock<IPaymentWebhookAuditRepository>();
        _mockLogger = new Mock<ILogger<PaymentService>>();

        // Setup mock Master Account từ DB
        _mockSePayAccountService.Setup(s => s.GetMasterAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SePayAccountDto
            {
                Id = Guid.NewGuid(),
                AccountType = SePayAccountType.Master,
                BankCode = "MBBank",
                MaskedAccountNumber = "0855199924",
                AccountHolder = "TEST HOLDER",
                IsActive = true
            });

        _mockSePayAccountService.Setup(s => s.GetRawMasterAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SePayAccount
            {
                Id = Guid.NewGuid(),
                BankCode = "MBBank",
                AccountNumber = "0855199924",
                AccountHolder = "TEST HOLDER",
                IsActive = true
            });

        _service = new PaymentService(
            _mockDepositService.Object,
            _mockCafeRepo.Object,
            _mockSettlementRepo.Object,
            _mockSessionRepo.Object,
            _mockGateway.Object,
            _mockSePayClient.Object,
            _mockSePayAccountService.Object,
            _mockWalletService.Object,
            _mockActiveSessionService.Object,
            _mockWebhookAuditRepository.Object,
            _mockLogger.Object);
    }

    #region CreateDepositPaymentAsync

    [Fact]
    public async Task CreateDepositPayment_DepositNotFound_ThrowsNotFoundException()
    {
        var request = new CreatePaymentRequestDto { DepositId = Guid.NewGuid() };
        _mockDepositService.Setup(s => s.GetByIdAsync(request.DepositId, It.IsAny<CancellationToken>())).ReturnsAsync((BookingDeposit?)null);

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

        _mockDepositService.Setup(s => s.GetByIdAsync(depositId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateDepositPaymentAsync(request, deposit.UserId));

        Assert.Contains("đã được xử lý", ex.Message);
    }

    [Fact]
    public async Task CreateDepositPayment_ValidRequest_ReturnsVietQrUrl()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Pending);
        deposit.OrderId = string.Empty;

        var request = new CreatePaymentRequestDto { DepositId = depositId, Amount = 40_000m };
        var qrUrl = "https://vietqr.app/img?bank=MBBank&acc=0855199924&amount=40000";

        _mockDepositService.Setup(s => s.GetByIdAsync(depositId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);
        _mockDepositService.Setup(s => s.UpdateQrInfoAsync(depositId, It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        _mockGateway.Setup(g => g.CreatePaymentAsync(It.IsAny<PaymentGatewayRequest>(), default))
            .ReturnsAsync(new PaymentGatewayResult
            {
                IsSuccess = true,
                Gateway = PaymentGateway.VietQr,
                PaymentUrl = qrUrl,
                QrImageUrl = qrUrl,
                OrderId = It.IsAny<string>(),
                Amount = 40_000m,
                RequiresManualConfirmation = true,
                Message = "Test message"
            });

        var result = await _service.CreateDepositPaymentAsync(request, deposit.UserId);

        Assert.Equal(qrUrl, result.PaymentUrl);
        Assert.Equal("VietQr", result.Gateway);
        Assert.StartsWith("BV", result.OrderId);
    }

    [Fact]
    public async Task CreateDepositPayment_CallsGatewayWithCorrectMetadata()
    {
        var depositId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Pending);
        deposit.OrderId = string.Empty;
        deposit.UserId = userId;
        deposit.ActiveSessionId = sessionId;

        var request = new CreatePaymentRequestDto
        {
            DepositId = depositId,
            Amount = 40_000m,
            CustomerEmail = "test@example.com"
        };

        _mockDepositService.Setup(s => s.GetByIdAsync(depositId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);
        _mockDepositService.Setup(s => s.UpdateQrInfoAsync(depositId, It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        _mockGateway.Setup(g => g.CreatePaymentAsync(It.IsAny<PaymentGatewayRequest>(), default))
            .ReturnsAsync(new PaymentGatewayResult
            {
                IsSuccess = true,
                Gateway = PaymentGateway.VietQr,
                PaymentUrl = "https://vietqr.app/img",
                QrImageUrl = "https://vietqr.app/img",
                OrderId = It.IsAny<string>(),
                Amount = 40_000m,
                RequiresManualConfirmation = true
            });

        await _service.CreateDepositPaymentAsync(request, userId);

        _mockGateway.Verify(g => g.CreatePaymentAsync(
            It.Is<PaymentGatewayRequest>(p =>
                p.Metadata["depositId"] == depositId.ToString() &&
                p.Metadata["activeSessionId"] == sessionId.ToString() &&
                p.Metadata["userId"] == userId.ToString() &&
                p.BankCode == "MBBank" &&
                p.AccountNumber == "0855199924"),
            default), Times.Once);
    }

    [Fact]
    public async Task CreateDepositPayment_GatewayFails_ThrowsPaymentException()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Pending);
        deposit.OrderId = string.Empty;

        var request = new CreatePaymentRequestDto { DepositId = depositId, Amount = 40_000m };

        _mockDepositService.Setup(s => s.GetByIdAsync(depositId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);
        _mockGateway.Setup(g => g.CreatePaymentAsync(It.IsAny<PaymentGatewayRequest>(), default))
            .ReturnsAsync(new PaymentGatewayResult
            {
                IsSuccess = false,
                Gateway = PaymentGateway.VietQr,
                ErrorMessage = "Gateway failed"
            });

        await Assert.ThrowsAsync<PaymentException>(
            () => _service.CreateDepositPaymentAsync(request, deposit.UserId));
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

        _mockDepositService.Verify(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockDepositService.Verify(s => s.GetByOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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

        _mockDepositService.Verify(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockDepositService.Verify(s => s.GetByOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(deposit.OrderId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);

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

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(deposit.OrderId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);

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

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(deposit.OrderId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);

        await _service.HandleSePayWebhookAsync(webhook);

        _mockDepositService.Verify(s => s.MarkAsPaidAsync(It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task HandleSePayWebhook_FailedCancelsPendingDeposit_CallsExpireAsync()
    {
        // BUGFIX (subagent audit #6): failed/cancelled webhook on Pending deposit
        // now calls ExpireAsync (Pending → Refunded) instead of MarkAsRefundedAsync
        // (which only accepts Paid).
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Pending);

        var webhook = new SePayWebhookDto
        {
            OrderId = deposit.OrderId,
            Status = "failed",
            Amount = deposit.Amount
        };

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(deposit.OrderId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);

        await _service.HandleSePayWebhookAsync(webhook);

        _mockDepositService.Verify(s => s.ExpireAsync(depositId), Times.Once);
        _mockDepositService.Verify(s => s.MarkAsRefundedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(deposit.OrderId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);

        await _service.HandleSePayWebhookAsync(webhook);

        _mockDepositService.Verify(s => s.MarkAsRefundedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleSePayWebhook_SessionSuccess_RunsLifecycleCleanup()
    {
        // Regression: webhook phải delegate PaySessionCoreAsync thay vì tự xử lý.
        // Trước refactor: webhook gọi TryUpdateStatusAsync + ReleaseMembersAndCloseLobbyAsync +
        // ReleaseSessionTableAndBoxAsync trực tiếp (THIẾU capture BVC + WalkInWindow + invoices).
        // Sau refactor: webhook delegate sang ActiveSessionService.PaySessionCoreAsync →
        //   side-effects đầy đủ (capture BVC, WalkInWindow, member invoices, release table/box,
        //   close lobby) đều chạy bên trong Core.

        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            TotalAmount = 85_000m,
            OrderId = "BV00000999"
        };

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var webhook = new SePayWebhookDto
        {
            SessionId = sessionId,
            OrderId = session.OrderId,
            Status = "success",
            Amount = session.TotalAmount
        };

        await _service.HandleSePayWebhookAsync(webhook);

        // Verify webhook delegate PaySessionCoreAsync với trigger = SePayWebhook.
        // Lifecycle cleanup (close lobby, release table/box) giờ chạy bên trong Core —
        // không verify trực tiếp trên _mockSessionRepo nữa.
        _mockActiveSessionService.Verify(
            s => s.PaySessionCoreAsync(
                cafeId, sessionId,
                It.IsAny<PaySessionRequestDto>(),
                PayTrigger.SePayWebhook,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify webhook KHÔNG tự gọi lifecycle cleanup (đã delegate).
        _mockSessionRepo.Verify(
            r => r.TryUpdateStatusAsync(It.IsAny<Guid>(), It.IsAny<GroupSessionStatus>(), It.IsAny<GroupSessionStatus>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockSessionRepo.Verify(
            r => r.ReleaseMembersAndCloseLobbyAsync(It.IsAny<Guid>()),
            Times.Never);
        _mockSessionRepo.Verify(
            r => r.ReleaseSessionTableAndBoxAsync(It.IsAny<Guid>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleSePayWebhook_SessionAmountMismatch_DoesNotUpdateOrCleanup()
    {
        // Regression: amount check must run BEFORE TryUpdateStatusAsync so we never half-commit.
        var sessionId = Guid.NewGuid();
        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = Guid.NewGuid(),
            Status = GroupSessionStatus.Unpaid,
            TotalAmount = 85_000m,
            OrderId = "BV00000998"
        };

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var webhook = new SePayWebhookDto
        {
            SessionId = sessionId,
            OrderId = session.OrderId,
            Status = "success",
            Amount = 50_000m // mismatch!
        };

        await _service.HandleSePayWebhookAsync(webhook);

        _mockSessionRepo.Verify(r => r.TryUpdateStatusAsync(It.IsAny<Guid>(), It.IsAny<GroupSessionStatus>(), It.IsAny<GroupSessionStatus>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockSessionRepo.Verify(r => r.ReleaseMembersAndCloseLobbyAsync(It.IsAny<Guid>()), Times.Never);
        _mockSessionRepo.Verify(r => r.ReleaseSessionTableAndBoxAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task HandleSePayWebhook_SessionAlreadyPaid_DoesNotDoubleCleanup()
    {
        // Race condition: another webhook already paid the session.
        // Sau refactor: PaySessionCoreAsync throw ConflictException(SessionMustBeUnpaidForPayment)
        // → webhook swallow exception, KHÔNG double-cleanup.
        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Paid, // đã Paid bởi webhook khác
            TotalAmount = 85_000m,
            OrderId = "BV00000997"
        };

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        // Mock PaySessionCoreAsync throw ConflictException (đã pay từ webhook trước).
        _mockActiveSessionService
            .Setup(s => s.PaySessionCoreAsync(
                cafeId, sessionId,
                It.IsAny<PaySessionRequestDto>(),
                PayTrigger.SePayWebhook,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException(
                "Phiên chơi phải ở trạng thái chờ thanh toán (UNPAID) để thanh toán."));

        var webhook = new SePayWebhookDto
        {
            SessionId = sessionId,
            OrderId = session.OrderId,
            Status = "success",
            Amount = session.TotalAmount
        };

        // KHÔNG throw ra ngoài (idempotent skip).
        await _service.HandleSePayWebhookAsync(webhook);

        // Verify KHÔNG trực tiếp cleanup (delegate đã throw, webhook swallow).
        _mockSessionRepo.Verify(r => r.ReleaseMembersAndCloseLobbyAsync(It.IsAny<Guid>()), Times.Never);
        _mockSessionRepo.Verify(r => r.ReleaseSessionTableAndBoxAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task HandleSePayWebhook_SessionNotMatched_DoesNotCleanup()
    {
        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockSessionRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((ActiveSession?)null);
        _mockSessionRepo.Setup(r => r.GetAllUnpaidAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActiveSession>());

        var webhook = new SePayWebhookDto
        {
            OrderId = "BV-UNKNOWN",
            Status = "success",
            Amount = 10_000m
        };

        await _service.HandleSePayWebhookAsync(webhook);

        _mockSessionRepo.Verify(r => r.ReleaseMembersAndCloseLobbyAsync(It.IsAny<Guid>()), Times.Never);
        _mockSessionRepo.Verify(r => r.ReleaseSessionTableAndBoxAsync(It.IsAny<Guid>()), Times.Never);
    }

    #endregion

    #region RefundDepositAsync

    [Fact]
    public async Task RefundDeposit_DepositNotFound_ThrowsNotFoundException()
    {
        _mockDepositService.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((BookingDeposit?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.RefundDepositAsync(Guid.NewGuid(), "test", Guid.NewGuid(), "Manager"));

        Assert.Contains("deposit", ex.Message.ToLower());
    }

    [Fact]
    public async Task RefundDeposit_NotPaid_ThrowsConflictException()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Pending);

        _mockDepositService.Setup(s => s.GetByIdAsync(depositId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _service.RefundDepositAsync(depositId, "test", deposit.CafeManagerId, "Manager"));

        Assert.Contains("'Pending'", ex.Message);
    }

    [Fact]
    public async Task RefundDeposit_PolicyNone_CallsForfeitAsync()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Paid);
        deposit.RefundPolicy = DepositRefundPolicy.None;

        _mockDepositService.Setup(s => s.GetByIdAsync(depositId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);
        _mockDepositService.Setup(s => s.ForfeitAsync(depositId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);

        var result = await _service.RefundDepositAsync(depositId, "Customer no-showed", deposit.CafeManagerId, "Manager");

        _mockDepositService.Verify(s => s.ForfeitAsync(depositId, It.IsAny<CancellationToken>()), Times.Once);
        _mockDepositService.Verify(s => s.MarkAsRefundedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefundDeposit_PolicyFullOrPartial_CallsMarkAsRefunded()
    {
        var depositId = Guid.NewGuid();
        var deposit = CreateTestDeposit(depositId, BookingDepositStatus.Paid);
        deposit.RefundPolicy = DepositRefundPolicy.Full;
        deposit.Amount = 50_000m;

        _mockDepositService.Setup(s => s.GetByIdAsync(depositId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);
        _mockDepositService.Setup(s => s.MarkAsRefundedAsync(depositId, It.IsAny<CancellationToken>())).ReturnsAsync(deposit);

        var result = await _service.RefundDepositAsync(depositId, "Cancelled by manager", deposit.CafeManagerId, "Manager");

        _mockDepositService.Verify(s => s.MarkAsRefundedAsync(depositId, It.IsAny<CancellationToken>()), Times.Once);
        _mockDepositService.Verify(s => s.ForfeitAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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

    #region CreateSessionPaymentAsync — OrderId/TransferContent sync (bugfix)

    [Fact]
    public async Task CreateSessionPayment_OrderIdAndTransferContent_AreSynced()
    {
        // BUGFIX regression: trước fix, OrderId = "BV88596434" (8 hex hash từ session.Id)
        // còn TransferContent = "BV-{Guid:N}" (32 hex) → webhook SePay parse TransferContent
        // ra OrderId 18 hex không khớp → log "session payment not matched", session mãi Unpaid.
        // Sau fix: cả hai phải bằng nhau để webhook lookup thẳng vào DB.

        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            TotalAmount = 85_000m,
            OrderId = string.Empty, // chưa có — sẽ được sinh
            TransferContent = null
        };
        var cafe = CreateTestCafe(cafeId, 100_000m);
        cafe.SePayAccountId = Guid.NewGuid();
        cafe.ManagerId = Guid.NewGuid();

        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _mockCafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);
        _mockSePayAccountService.Setup(s => s.GetRawByCafeIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SePayAccount
            {
                Id = cafe.SePayAccountId.Value,
                BankCode = "MBBank",
                AccountNumber = "0855199924",
                AccountHolder = "TEST HOLDER",
                IsActive = true
            });
        _mockGateway.Setup(g => g.CreatePaymentAsync(It.IsAny<PaymentGatewayRequest>(), default))
            .ReturnsAsync(new PaymentGatewayResult
            {
                IsSuccess = true,
                Gateway = PaymentGateway.VietQr,
                QrImageUrl = "https://vietqr.app/img?x=1",
                RequiresManualConfirmation = true
            });
        _mockSessionRepo.Setup(r => r.UpdateAsync(It.IsAny<ActiveSession>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _mockSessionRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var request = new CreateSessionPaymentRequestDto { SessionId = sessionId, CustomerEmail = "x@y.z" };
        var actorUserId = cafe.ManagerId;
        var result = await _service.CreateSessionPaymentAsync(request, actorUserId, "Manager");

        // OrderId và TransferContent phải BẰNG NHAU (case-insensitive) để webhook match.
        Assert.False(string.IsNullOrWhiteSpace(result.OrderId));
        Assert.False(string.IsNullOrWhiteSpace(result.TransferContent));
        Assert.Equal(result.OrderId, result.TransferContent, ignoreCase: true);

        // Format phải là BV-{16 hex uppercase} để khớp regex `BV[A-Z0-9]{8,16}` trong
        // SePayWebhookDto.ExtractOrderId.
        var match = System.Text.RegularExpressions.Regex.Match(
            result.OrderId, @"^BV-[A-F0-9]{16}$");
        Assert.True(match.Success, $"OrderId '{result.OrderId}' không match format BV-XXXXXXXXXXXXXXXX");

        // OrderId mới cũng phải được persist xuống DB qua session object.
        Assert.Equal(result.OrderId, session.OrderId, ignoreCase: true);
        Assert.Equal(result.OrderId, session.TransferContent, ignoreCase: true);
        _mockSessionRepo.Verify(r => r.UpdateAsync(It.Is<ActiveSession>(s =>
            s.Id == sessionId
            && !string.IsNullOrWhiteSpace(s.OrderId)
            && string.Equals(s.OrderId, s.TransferContent, StringComparison.OrdinalIgnoreCase)
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateSessionPayment_LegacyRecord_SyncsTransferContentToExistingOrderId()
    {
        // Bugfix: legacy records (tạo trước fix) có OrderId != TransferContent.
        // Khi CreateSessionPaymentAsync chạy lại, TransferContent phải được đồng bộ
        // theo OrderId hiện tại (không đổi OrderId vì QR cũ đã được in cho khách).

        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        const string legacyOrderId = "BV-AB12CD34EF56AB78"; // format đúng 16 hex
        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            TotalAmount = 4_000m,
            OrderId = legacyOrderId,
            TransferContent = "BV-LEGACYDIFFERENT00000000000000" // lệch
        };
        var cafe = CreateTestCafe(cafeId, 100_000m);
        cafe.SePayAccountId = Guid.NewGuid();
        cafe.ManagerId = Guid.NewGuid();

        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _mockCafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>())).ReturnsAsync(cafe);
        _mockSePayAccountService.Setup(s => s.GetRawByCafeIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SePayAccount
            {
                Id = cafe.SePayAccountId.Value,
                BankCode = "MBBank",
                AccountNumber = "0855199924",
                AccountHolder = "TEST HOLDER",
                IsActive = true
            });
        _mockGateway.Setup(g => g.CreatePaymentAsync(It.IsAny<PaymentGatewayRequest>(), default))
            .ReturnsAsync(new PaymentGatewayResult
            {
                IsSuccess = true,
                Gateway = PaymentGateway.VietQr,
                QrImageUrl = "https://vietqr.app/img?x=1"
            });
        _mockSessionRepo.Setup(r => r.UpdateAsync(It.IsAny<ActiveSession>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _mockSessionRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var request = new CreateSessionPaymentRequestDto { SessionId = sessionId };
        var result = await _service.CreateSessionPaymentAsync(request, cafe.ManagerId, "Manager");

        // OrderId KHÔNG được đổi (giữ cho khách đã có QR cũ), TransferContent đồng bộ về.
        Assert.Equal(legacyOrderId, result.OrderId, ignoreCase: true);
        Assert.Equal(legacyOrderId, result.TransferContent, ignoreCase: true);
        Assert.Equal(legacyOrderId, session.OrderId, ignoreCase: true);
        Assert.Equal(legacyOrderId, session.TransferContent, ignoreCase: true);
    }

    [Fact]
    public async Task HandleSePayWebhook_SessionMatchedByOrderId_DelegatesToPaySessionCore()
    {
        // End-to-end: CreateSessionPayment tạo ra OrderId "BV-XXXXXXXXXXXXXXXX", sau đó
        // SePay webhook BankAPINotify gửi về content "BV-XXXXXXXXXXXXXXXX ..." → ExtractOrderId
        // parse ra OrderId "BVXXXXXXXXXXXXXXXX" → match với session.OrderId (case-insensitive
        // trong DB index). Phải delegate sang PaySessionCoreAsync (PayTrigger=SePayWebhook).
        // Trước đây test này verify TryUpdateStatusAsync; sau refactor, verify delegate
        // thay vì tự xử lý status update.

        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        const string orderId = "BV-AB12CD34EF56AB78";
        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            TotalAmount = 85_000m,
            OrderId = orderId,
            TransferContent = orderId
        };

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _mockSessionRepo.Setup(r => r.GetByOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(session);

        // Webhook BankAPINotify: content chứa TransferContent (case-insensitive — bank có thể uppercase).
        // Controller gọi Normalize() trước khi pass vào service; unit test phải gọi thủ công.
        var webhook = new SePayWebhookDto
        {
            Content = $"chuyen tien {orderId.ToLowerInvariant()} thanh toan",
            ReferenceCode = "TXN-PROD-001",
            TransferType = "in",
            TransferAmount = 85_000m
        };
        webhook.Normalize();

        await _service.HandleSePayWebhookAsync(webhook);

        // Verify webhook đã parse ra OrderId đúng format.
        Assert.Equal(orderId.ToUpperInvariant(), webhook.OrderId, ignoreCase: true);

        // Verify PaySessionCoreAsync được gọi với trigger = SePayWebhook.
        _mockActiveSessionService.Verify(
            s => s.PaySessionCoreAsync(
                cafeId,
                sessionId,
                It.IsAny<PaySessionRequestDto>(),
                PayTrigger.SePayWebhook,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleSePayWebhook_SessionAlreadyPaid_IdempotentSkip()
    {
        // Regression: webhook trùng (session đã Paid từ staff manual) → PaySessionCoreAsync
        // throw ConflictException(SessionMustBeUnpaidForPayment) → webhook swallow + log + return.
        // KHÔNG double-pay, KHÔNG throw ra ngoài.

        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        const string orderId = "BV-IDEMPOTENT123456";
        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            TotalAmount = 50_000m,
            OrderId = orderId,
            TransferContent = orderId
        };

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _mockSessionRepo.Setup(r => r.GetByOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(session);

        // Mock PaySessionCoreAsync throw ConflictException giả lập session đã Paid.
        _mockActiveSessionService
            .Setup(s => s.PaySessionCoreAsync(
                cafeId, sessionId,
                It.IsAny<PaySessionRequestDto>(),
                PayTrigger.SePayWebhook,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException(
                "Phiên chơi phải ở trạng thái chờ thanh toán (UNPAID) để thanh toán."));

        var webhook = new SePayWebhookDto
        {
            OrderId = orderId,
            GatewayTransactionId = "TXN-IDEMPOTENT",
            Amount = 50_000m,
            Status = "success",
            SessionId = sessionId,
            ReferenceCode = "REF-IDEMPOTENT"
        };

        // KHÔNG throw ra ngoài (idempotent skip).
        await _service.HandleSePayWebhookAsync(webhook);

        // Verify PaySessionCoreAsync được gọi 1 lần (không retry).
        _mockActiveSessionService.Verify(
            s => s.PaySessionCoreAsync(
                cafeId, sessionId,
                It.IsAny<PaySessionRequestDto>(),
                PayTrigger.SePayWebhook,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleSePayWebhook_SessionAmountMismatch_DoesNotDelegate()
    {
        // Regression: SePay gửi amount khác TotalAmount → webhook skip KHÔNG delegate.
        // Trước đây có thể update status trước khi validate amount.

        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        const string orderId = "BV-MISMATCH9876543";
        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            TotalAmount = 100_000m, // expect 100k
            OrderId = orderId,
            TransferContent = orderId
        };

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _mockSessionRepo.Setup(r => r.GetByOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var webhook = new SePayWebhookDto
        {
            OrderId = orderId,
            GatewayTransactionId = "TXN-MISMATCH",
            Amount = 50_000m, // receive 50k — mismatch!
            Status = "success",
            SessionId = sessionId
        };

        await _service.HandleSePayWebhookAsync(webhook);

        // Verify KHÔNG delegate PaySessionCoreAsync (amount mismatch → early return).
        _mockActiveSessionService.Verify(
            s => s.PaySessionCoreAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<PaySessionRequestDto>(),
                It.IsAny<PayTrigger>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleSePayWebhook_SessionCancelled_OnlyLogs()
    {
        // Regression: SePay webhook "failed" / "cancelled" cho session payment → chỉ log,
        // KHÔNG delegate PaySessionCoreAsync (vì không có amount hợp lệ để close session).
        // Staff sẽ xử lý riêng trên POS.

        var sessionId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        const string orderId = "BV-CANCELLED12345";
        var session = new ActiveSession
        {
            Id = sessionId,
            CafeId = cafeId,
            Status = GroupSessionStatus.Unpaid,
            TotalAmount = 80_000m,
            OrderId = orderId,
            TransferContent = orderId
        };

        _mockDepositService.Setup(s => s.GetBySePayTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockDepositService.Setup(s => s.GetByOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingDeposit?)null);
        _mockSessionRepo.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _mockSessionRepo.Setup(r => r.GetByOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var webhook = new SePayWebhookDto
        {
            OrderId = orderId,
            GatewayTransactionId = "TXN-CANCELLED",
            Amount = 80_000m,
            Status = "cancelled", // failed/cancelled → chỉ log
            SessionId = sessionId
        };

        await _service.HandleSePayWebhookAsync(webhook);

        // Verify KHÔNG delegate PaySessionCoreAsync.
        _mockActiveSessionService.Verify(
            s => s.PaySessionCoreAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<PaySessionRequestDto>(),
                It.IsAny<PayTrigger>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
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
            UserId = Guid.NewGuid(),
            CafeId = cafeId ?? Guid.NewGuid(),
            CafeManagerId = Guid.NewGuid(),
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
