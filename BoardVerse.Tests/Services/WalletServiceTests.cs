using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.DTOs.Wallet;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using BoardVerse.Services.Services.Payments;
using BoardVerse.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

public class WalletServiceTests
{
    private readonly Mock<IWalletRepository> _mockWalletRepo;
    private readonly Mock<IBvcLedgerEntryRepository> _mockLedgerRepo;
    private readonly Mock<IBvcTopUpRequestRepository> _mockTopUpRepo;
    private readonly Mock<IUserManagementRepository> _mockUserRepo;
    private readonly Mock<IPaymentGatewayService> _mockGateway;
    private readonly Mock<ISePayAccountService> _mockSePayAccount;
    private readonly Mock<ILogger<WalletService>> _mockLogger;
    private readonly BoardVerseDbContext DbContext;
    private readonly WalletService _service;

    public WalletServiceTests()
    {
        _mockWalletRepo = new Mock<IWalletRepository>();
        _mockLedgerRepo = new Mock<IBvcLedgerEntryRepository>();
        _mockTopUpRepo = new Mock<IBvcTopUpRequestRepository>();
        _mockUserRepo = new Mock<IUserManagementRepository>();
        _mockGateway = new Mock<IPaymentGatewayService>();
        _mockSePayAccount = new Mock<ISePayAccountService>();
        _mockLogger = new Mock<ILogger<WalletService>>();

        var fakeDbContext = new FakeDbContext();

        _service = new WalletService(
            _mockWalletRepo.Object,
            _mockLedgerRepo.Object,
            _mockTopUpRepo.Object,
            _mockUserRepo.Object,
            _mockGateway.Object,
            _mockSePayAccount.Object,
            _mockLogger.Object,
            DbContext);
    }

    #region GetOrCreateWalletAsync

    [Fact]
    public async Task GetOrCreateWalletAsync_WalletExists_ReturnsExistingWithoutCreating()
    {
        var userId = Guid.NewGuid();
        var existing = new Wallet
        {
            UserId = userId,
            AvailableBalance = 250,
            HeldBalance = 50,
            RiskLevel = RiskLevel.Low,
            AccountStatus = AccountStatus.Active
        };

        _mockWalletRepo.Setup(r => r.GetByUserIdAsync(userId)).ReturnsAsync(existing);

        var dto = await _service.GetOrCreateWalletAsync(userId, includeHeld: true);

        Assert.Equal(250, dto.AvailableBalance);
        Assert.Equal(50, dto.HeldBalance);
        _mockWalletRepo.Verify(r => r.AddAsync(It.IsAny<Wallet>()), Times.Never);
        _mockWalletRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task GetOrCreateWalletAsync_NewWallet_AutoCreatesEmpty()
    {
        var userId = Guid.NewGuid();
        _mockWalletRepo.Setup(r => r.GetByUserIdAsync(userId)).ReturnsAsync((Wallet?)null);
        _mockUserRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(new User
        {
            Id = userId,
            Username = "u1",
            Email = "u1@test.com"
        });

        var dto = await _service.GetOrCreateWalletAsync(userId, includeHeld: false);

        Assert.Equal(0, dto.AvailableBalance);
        Assert.Null(dto.HeldBalance);
        Assert.Equal(RiskLevel.Low, dto.RiskLevel);
        Assert.Equal(AccountStatus.Active, dto.AccountStatus);
        _mockWalletRepo.Verify(r => r.AddAsync(It.IsAny<Wallet>()), Times.Once);
        _mockWalletRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task GetOrCreateWalletAsync_UserNotFound_ThrowsNotFound()
    {
        var userId = Guid.NewGuid();
        _mockWalletRepo.Setup(r => r.GetByUserIdAsync(userId)).ReturnsAsync((Wallet?)null);
        _mockUserRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.GetOrCreateWalletAsync(userId, includeHeld: false));
    }

    #endregion

    #region CreateTopUpAsync — validation

    [Fact]
    public async Task CreateTopUpAsync_BelowMinimumVnd_ThrowsBadRequest()
    {
        var userId = Guid.NewGuid();
        var req = new TopUpRequestDto { AmountVnd = 5_000, IdempotencyKey = "key-12345" };

        SetupUserWithoutWallet(userId);

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateTopUpAsync(userId, req));

        Assert.Contains("10.000", ex.Message);
    }

    [Fact]
    public async Task CreateTopUpAsync_AmountNotMultipleOfThousand_ThrowsBadRequest()
    {
        var userId = Guid.NewGuid();
        var req = new TopUpRequestDto { AmountVnd = 15_500, IdempotencyKey = "key-12345" };

        SetupUserWithoutWallet(userId);

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateTopUpAsync(userId, req));

        Assert.Contains("bội số", ex.Message);
    }

    [Fact]
    public async Task CreateTopUpAsync_SuspendedAccount_ThrowsForbidden()
    {
        var userId = Guid.NewGuid();
        var req = new TopUpRequestDto { AmountVnd = 100_000, IdempotencyKey = "key-12345" };

        _mockWalletRepo.Setup(r => r.GetByUserIdAsync(userId)).ReturnsAsync(new Wallet
        {
            UserId = userId,
            AccountStatus = AccountStatus.Suspended
        });

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreateTopUpAsync(userId, req));
    }

    [Fact]
    public async Task CreateTopUpAsync_BannedAccount_ThrowsForbidden()
    {
        var userId = Guid.NewGuid();
        var req = new TopUpRequestDto { AmountVnd = 100_000, IdempotencyKey = "key-12345" };

        _mockWalletRepo.Setup(r => r.GetByUserIdAsync(userId)).ReturnsAsync(new Wallet
        {
            UserId = userId,
            AccountStatus = AccountStatus.Banned
        });

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreateTopUpAsync(userId, req));
    }

    #endregion

    #region CreateTopUpAsync — happy path

    [Fact]
    public async Task CreateTopUpAsync_ValidAmount_ReturnsExpectedBvcAndGatewayUrl()
    {
        var userId = Guid.NewGuid();
        var req = new TopUpRequestDto
        {
            AmountVnd = 100_000,
            IdempotencyKey = "new-idem-key-12345"
        };

        SetupUserWithoutWallet(userId);
        SetupLedgerNoExistingKey(req.IdempotencyKey);

        _mockSePayAccount
            .Setup(s => s.GetRawMasterAccountAsync())
            .ReturnsAsync(new SePayAccount
            {
                IsActive = true,
                BankCode = "MBBank",
                AccountNumber = "0123456789",
                AccountHolder = "BOARDVERSE MASTER"
            });

        _mockGateway
            .Setup(g => g.CreatePaymentAsync(It.IsAny<PaymentGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayResult
            {
                IsSuccess = true,
                PaymentUrl = "https://pay.sepay.vn/abc",
                QrImageUrl = "https://qr.sepay.vn/abc.png",
                Gateway = PaymentGateway.SePay
            });

        var dto = await _service.CreateTopUpAsync(userId, req);

        Assert.Equal(100, dto.ExpectedBvc); // 100.000 / 1.000
        Assert.StartsWith("https://pay.sepay.vn/", dto.PaymentUrl);
        Assert.False(string.IsNullOrWhiteSpace(dto.OrderId));
        Assert.True(dto.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task CreateTopUpAsync_NoMasterAccountConfigured_ThrowsPayment()
    {
        var userId = Guid.NewGuid();
        var req = new TopUpRequestDto { AmountVnd = 100_000, IdempotencyKey = "key-12345" };

        SetupUserWithoutWallet(userId);
        SetupLedgerNoExistingKey(req.IdempotencyKey);
        _mockSePayAccount
            .Setup(s => s.GetRawMasterAccountAsync())
            .ReturnsAsync((SePayAccount?)null);

        await Assert.ThrowsAsync<PaymentException>(
            () => _service.CreateTopUpAsync(userId, req));
    }

    [Fact]
    public async Task CreateTopUpAsync_GatewayFails_ThrowsPayment()
    {
        var userId = Guid.NewGuid();
        var req = new TopUpRequestDto { AmountVnd = 100_000, IdempotencyKey = "key-12345" };

        SetupUserWithoutWallet(userId);
        SetupLedgerNoExistingKey(req.IdempotencyKey);
        _mockSePayAccount.Setup(s => s.GetRawMasterAccountAsync()).ReturnsAsync(new SePayAccount
        {
            IsActive = true,
            BankCode = "MBBank",
            AccountNumber = "0123456789"
        });
        _mockGateway
            .Setup(g => g.CreatePaymentAsync(It.IsAny<PaymentGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayResult
            {
                IsSuccess = false,
                ErrorMessage = "SePay timeout"
            });

        var ex = await Assert.ThrowsAsync<PaymentException>(
            () => _service.CreateTopUpAsync(userId, req));
        Assert.Contains("SePay", ex.Message);
    }

    [Fact]
    public async Task CreateTopUpAsync_IdempotencyKeyAlreadyUsed_ReturnsSameQuoteWithoutCreatingNewOrder()
    {
        var userId = Guid.NewGuid();
        var key = "unique-key-1234";
        var req = new TopUpRequestDto { AmountVnd = 100_000, IdempotencyKey = key };

        SetupUserWithoutWallet(userId);

        // Code ưu tiên lookup BvcTopUpRequest trước (track OrderId + Status).
        var existingTopUp = new BvcTopUpRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrderId = "BVC-OLD-ORDER",
            AmountVnd = 100_000,
            ExpectedBvc = 100,
            IdempotencyKey = key,
            Status = BvcTopUpStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTime.UtcNow.AddMinutes(29)
        };
        _mockTopUpRepo.Setup(r => r.GetByIdempotencyKeyAsync(key, CancellationToken.None))
            .ReturnsAsync(existingTopUp);

        var dto = await _service.CreateTopUpAsync(userId, req);

        Assert.Equal("BVC-OLD-ORDER", dto.OrderId);
        Assert.Equal(100, dto.ExpectedBvc);
        _mockGateway.Verify(
            g => g.CreatePaymentAsync(It.IsAny<PaymentGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region CancelTopUpAsync

    [Fact]
    public async Task CancelTopUpAsync_TopUpNotFound_ThrowsNotFound()
    {
        var topUpId = Guid.NewGuid();
        _mockTopUpRepo.Setup(r => r.GetByIdAsync(topUpId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BvcTopUpRequest?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CancelTopUpAsync(topUpId, Guid.NewGuid()));

        _mockTopUpRepo.Verify(r => r.UpdateAsync(It.IsAny<BvcTopUpRequest>()), Times.Never);
        _mockTopUpRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelTopUpAsync_DifferentOwner_ThrowsForbidden()
    {
        var topUpId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var attackerId = Guid.NewGuid();
        var topUp = new BvcTopUpRequest
        {
            Id = topUpId,
            UserId = ownerId,
            OrderId = "BVC-OWNED",
            AmountVnd = 50_000,
            ExpectedBvc = 50,
            IdempotencyKey = "key-owned-1234",
            Status = BvcTopUpStatus.Pending
        };
        _mockTopUpRepo.Setup(r => r.GetByIdAsync(topUpId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(topUp);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CancelTopUpAsync(topUpId, attackerId));

        _mockTopUpRepo.Verify(r => r.UpdateAsync(It.IsAny<BvcTopUpRequest>()), Times.Never);
    }

    [Theory]
    [InlineData(BvcTopUpStatus.Paid)]
    [InlineData(BvcTopUpStatus.Failed)]
    [InlineData(BvcTopUpStatus.Expired)]
    [InlineData(BvcTopUpStatus.Cancelled)]
    public async Task CancelTopUpAsync_TerminalStatus_ThrowsConflict(BvcTopUpStatus status)
    {
        var userId = Guid.NewGuid();
        var topUpId = Guid.NewGuid();
        var topUp = new BvcTopUpRequest
        {
            Id = topUpId,
            UserId = userId,
            OrderId = "BVC-TERMINAL",
            AmountVnd = 50_000,
            ExpectedBvc = 50,
            IdempotencyKey = "key-terminal-1234",
            Status = status
        };
        _mockTopUpRepo.Setup(r => r.GetByIdAsync(topUpId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(topUp);

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CancelTopUpAsync(topUpId, userId));

        _mockTopUpRepo.Verify(r => r.UpdateAsync(It.IsAny<BvcTopUpRequest>()), Times.Never);
    }

    [Fact]
    public async Task CancelTopUpAsync_Pending_SetsStatusCancelledAndSaves()
    {
        var userId = Guid.NewGuid();
        var topUpId = Guid.NewGuid();
        var topUp = new BvcTopUpRequest
        {
            Id = topUpId,
            UserId = userId,
            OrderId = "BVC-CANCEL-OK",
            AmountVnd = 50_000,
            ExpectedBvc = 50,
            IdempotencyKey = "key-pending-1234",
            Status = BvcTopUpStatus.Pending
        };
        _mockTopUpRepo.Setup(r => r.GetByIdAsync(topUpId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(topUp);
        BvcTopUpRequest? captured = null;
        _mockTopUpRepo.Setup(r => r.UpdateAsync(It.IsAny<BvcTopUpRequest>()))
            .Callback<BvcTopUpRequest>(r => captured = r)
            .Returns(Task.CompletedTask);

        await _service.CancelTopUpAsync(topUpId, userId);

        Assert.NotNull(captured);
        Assert.Equal(BvcTopUpStatus.Cancelled, captured!.Status);
        Assert.NotNull(captured.UpdatedAt);
        _mockTopUpRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region UpdateTopUpAmountAsync

    [Fact]
    public async Task UpdateTopUpAmountAsync_AmountBelowMinimum_ThrowsBadRequest()
    {
        var topUpId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var req = new UpdateTopUpRequestDto { AmountVnd = 5_000, IdempotencyKey = "new-key-12345" };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.UpdateTopUpAmountAsync(topUpId, userId, req));

        _mockTopUpRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateTopUpAmountAsync_AmountNotMultipleOfThousand_ThrowsBadRequest()
    {
        var topUpId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var req = new UpdateTopUpRequestDto { AmountVnd = 50_500, IdempotencyKey = "new-key-12345" };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.UpdateTopUpAmountAsync(topUpId, userId, req));
    }

    [Fact]
    public async Task UpdateTopUpAmountAsync_EmptyIdempotencyKey_ThrowsBadRequest()
    {
        var topUpId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var req = new UpdateTopUpRequestDto { AmountVnd = 50_000, IdempotencyKey = "" };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.UpdateTopUpAmountAsync(topUpId, userId, req));
    }

    [Fact]
    public async Task UpdateTopUpAmountAsync_TopUpNotFound_ThrowsNotFound()
    {
        var topUpId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var req = new UpdateTopUpRequestDto { AmountVnd = 50_000, IdempotencyKey = "new-key-12345" };

        _mockTopUpRepo.Setup(r => r.GetByIdAsync(topUpId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BvcTopUpRequest?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.UpdateTopUpAmountAsync(topUpId, userId, req));
    }

    [Fact]
    public async Task UpdateTopUpAmountAsync_DifferentOwner_ThrowsForbidden()
    {
        var topUpId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var attackerId = Guid.NewGuid();
        var req = new UpdateTopUpRequestDto { AmountVnd = 50_000, IdempotencyKey = "new-key-12345" };
        var topUp = new BvcTopUpRequest
        {
            Id = topUpId,
            UserId = ownerId,
            OrderId = "BVC-OWNED",
            AmountVnd = 20_000,
            ExpectedBvc = 20,
            IdempotencyKey = "old-key-1234",
            Status = BvcTopUpStatus.Pending
        };
        _mockTopUpRepo.Setup(r => r.GetByIdAsync(topUpId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(topUp);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.UpdateTopUpAmountAsync(topUpId, attackerId, req));

        _mockTopUpRepo.Verify(r => r.UpdateAsync(It.IsAny<BvcTopUpRequest>()), Times.Never);
        _mockGateway.Verify(
            g => g.CreatePaymentAsync(It.IsAny<PaymentGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(BvcTopUpStatus.Paid)]
    [InlineData(BvcTopUpStatus.Failed)]
    [InlineData(BvcTopUpStatus.Expired)]
    [InlineData(BvcTopUpStatus.Cancelled)]
    public async Task UpdateTopUpAmountAsync_TerminalStatus_ThrowsConflict(BvcTopUpStatus status)
    {
        var topUpId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var req = new UpdateTopUpRequestDto { AmountVnd = 50_000, IdempotencyKey = "new-key-12345" };
        var topUp = new BvcTopUpRequest
        {
            Id = topUpId,
            UserId = userId,
            OrderId = "BVC-TERMINAL",
            AmountVnd = 20_000,
            ExpectedBvc = 20,
            IdempotencyKey = "old-key-1234",
            Status = status
        };
        _mockTopUpRepo.Setup(r => r.GetByIdAsync(topUpId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(topUp);

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.UpdateTopUpAmountAsync(topUpId, userId, req));

        _mockGateway.Verify(
            g => g.CreatePaymentAsync(It.IsAny<PaymentGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateTopUpAmountAsync_IdempotencyKeyConflictsWithAnotherTopUp_ThrowsConflict()
    {
        var topUpId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var req = new UpdateTopUpRequestDto { AmountVnd = 50_000, IdempotencyKey = "conflicting-key-1234" };
        var existing = new BvcTopUpRequest
        {
            Id = topUpId,
            UserId = userId,
            OrderId = "BVC-EXISTING",
            AmountVnd = 20_000,
            ExpectedBvc = 20,
            IdempotencyKey = "old-key-1234",
            Status = BvcTopUpStatus.Pending
        };
        var conflictingOther = new BvcTopUpRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrderId = "BVC-OTHER",
            AmountVnd = 30_000,
            ExpectedBvc = 30,
            IdempotencyKey = "conflicting-key-1234",
            Status = BvcTopUpStatus.Pending
        };
        _mockTopUpRepo.Setup(r => r.GetByIdAsync(topUpId, CancellationToken.None))
            .ReturnsAsync(existing);
        _mockTopUpRepo.Setup(r => r.GetByIdempotencyKeyAsync("conflicting-key-1234", CancellationToken.None))
            .ReturnsAsync(conflictingOther);

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.UpdateTopUpAmountAsync(topUpId, userId, req));

        _mockGateway.Verify(
            g => g.CreatePaymentAsync(It.IsAny<PaymentGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateTopUpAmountAsync_NoMasterAccountConfigured_ThrowsPayment()
    {
        var topUpId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var req = new UpdateTopUpRequestDto { AmountVnd = 50_000, IdempotencyKey = "new-key-12345" };
        var existing = new BvcTopUpRequest
        {
            Id = topUpId,
            UserId = userId,
            OrderId = "BVC-EXISTING",
            AmountVnd = 20_000,
            ExpectedBvc = 20,
            IdempotencyKey = "old-key-1234",
            Status = BvcTopUpStatus.Pending
        };
        _mockTopUpRepo.Setup(r => r.GetByIdAsync(topUpId, CancellationToken.None))
            .ReturnsAsync(existing);
        _mockTopUpRepo.Setup(r => r.GetByIdempotencyKeyAsync("new-key-12345", CancellationToken.None))
            .ReturnsAsync((BvcTopUpRequest?)null);
        _mockSePayAccount.Setup(s => s.GetRawMasterAccountAsync())
            .ReturnsAsync((SePayAccount?)null);

        await Assert.ThrowsAsync<PaymentException>(
            () => _service.UpdateTopUpAmountAsync(topUpId, userId, req));

        // Đơn cũ đã được đánh dấu Cancelled (line 304-307 service) trước khi gọi SePay,
        // nhưng SaveChangesAsync chưa chạy vì throw ngay sau — caller vẫn thấy Status cũ trong DB.
        // (Idempotent retry sẽ thấy Status=Pending nếu SaveChangesAsync chưa commit.)
        Assert.Equal(BvcTopUpStatus.Cancelled, existing.Status);
        _mockTopUpRepo.Verify(r => r.UpdateAsync(It.IsAny<BvcTopUpRequest>()), Times.Once);
        _mockTopUpRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        // Không tạo đơn mới.
        _mockTopUpRepo.Verify(r => r.AddAsync(It.IsAny<BvcTopUpRequest>()), Times.Never);
    }

    [Fact]
    public async Task UpdateTopUpAmountAsync_HappyPath_CancelsOldAndCreatesNew()
    {
        var topUpId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var req = new UpdateTopUpRequestDto { AmountVnd = 50_000, IdempotencyKey = "new-key-12345" };
        var existing = new BvcTopUpRequest
        {
            Id = topUpId,
            UserId = userId,
            OrderId = "BVC-OLD",
            AmountVnd = 20_000,
            ExpectedBvc = 20,
            IdempotencyKey = "old-key-1234",
            Status = BvcTopUpStatus.Pending
        };
        _mockTopUpRepo.Setup(r => r.GetByIdAsync(topUpId, CancellationToken.None))
            .ReturnsAsync(existing);
        _mockTopUpRepo.Setup(r => r.GetByIdempotencyKeyAsync("new-key-12345", CancellationToken.None))
            .ReturnsAsync((BvcTopUpRequest?)null);
        _mockSePayAccount.Setup(s => s.GetRawMasterAccountAsync()).ReturnsAsync(new SePayAccount
        {
            IsActive = true,
            BankCode = "MBBank",
            AccountNumber = "0123456789",
            AccountHolder = "BV MASTER"
        });
        _mockGateway
            .Setup(g => g.CreatePaymentAsync(It.IsAny<PaymentGatewayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayResult
            {
                IsSuccess = true,
                PaymentUrl = "https://pay.sepay.vn/new",
                QrImageUrl = "https://qr.sepay.vn/new.png"
            });

        var dto = await _service.UpdateTopUpAmountAsync(topUpId, userId, req);

        // Old top-up bị set Cancelled.
        Assert.Equal(BvcTopUpStatus.Cancelled, existing.Status);
        Assert.NotNull(existing.UpdatedAt);

        // Response đúng số tiền mới.
        Assert.Equal(50, dto.ExpectedBvc);
        Assert.Equal("https://pay.sepay.vn/new", dto.PaymentUrl);
        Assert.Equal("new-key-12345", dto.IdempotencyKey);
        Assert.NotEqual("BVC-OLD", dto.OrderId);

        // Repository side-effects.
        _mockTopUpRepo.Verify(r => r.AddAsync(It.Is<BvcTopUpRequest>(
            t => t.UserId == userId
                 && t.AmountVnd == 50_000
                 && t.ExpectedBvc == 50
                 && t.IdempotencyKey == "new-key-12345"
                 && t.Status == BvcTopUpStatus.Pending)), Times.Once);
        _mockTopUpRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetTransactionsAsync

    [Fact]
    public async Task GetTransactionsAsync_NoWallet_ReturnsEmptyPage()
    {
        var userId = Guid.NewGuid();
        _mockWalletRepo.Setup(r => r.GetByUserIdAsync(userId)).ReturnsAsync((Wallet?)null);

        var page = await _service.GetTransactionsAsync(userId, 1, 20);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalItems);
        Assert.False(page.HasMore);
        _mockLedgerRepo.Verify(r => r.CountByUserAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task GetTransactionsAsync_ClampsPageSizeToMax()
    {
        var userId = Guid.NewGuid();
        _mockWalletRepo.Setup(r => r.GetByUserIdAsync(userId)).ReturnsAsync(new Wallet { UserId = userId });
        _mockLedgerRepo.Setup(r => r.CountByUserAsync(userId)).ReturnsAsync(0);
        _mockLedgerRepo.Setup(r => r.GetHistoryAsync(userId, 1, 100))
            .ReturnsAsync(new List<BvcLedgerEntry>());

        var page = await _service.GetTransactionsAsync(userId, page: 1, pageSize: 9999);

        Assert.Equal(100, page.PageSize);
    }

    [Fact]
    public async Task GetTransactionsAsync_MapsLedgerEntriesCorrectly()
    {
        var userId = Guid.NewGuid();
        _mockWalletRepo.Setup(r => r.GetByUserIdAsync(userId)).ReturnsAsync(new Wallet { UserId = userId });
        _mockLedgerRepo.Setup(r => r.CountByUserAsync(userId)).ReturnsAsync(2);
        _mockLedgerRepo.Setup(r => r.GetHistoryAsync(userId, 1, 20))
            .ReturnsAsync(new List<BvcLedgerEntry>
            {
                new()
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    UserId = userId,
                    Type = LedgerEntryType.TopUp,
                    Amount = 100,
                    BalanceSnapshot = 100,
                    CreatedAt = DateTime.UtcNow
                },
                new()
                {
                    Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    UserId = userId,
                    Type = LedgerEntryType.TopUp,
                    Amount = 50,
                    BalanceSnapshot = 150,
                    RelatedPaymentRef = "BVC-ABC",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-5)
                }
            });

        var page = await _service.GetTransactionsAsync(userId, 1, 20);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.TotalItems);
        Assert.False(page.HasMore);
        Assert.Equal(LedgerEntryType.TopUp, page.Items[0].Type);
        Assert.Equal(100, page.Items[0].Amount);
        Assert.Equal(150, page.Items[1].BalanceSnapshot);
        Assert.Equal("BVC-ABC", page.Items[1].RelatedPaymentRef);
    }

    #endregion

    private void SetupUserWithoutWallet(Guid userId)
    {
        _mockWalletRepo.Setup(r => r.GetByUserIdAsync(userId))
            .ReturnsAsync((Wallet?)null);
        _mockUserRepo.Setup(r => r.GetByIdAsync(userId))
            .ReturnsAsync(new User
            {
                Id = userId,
                Username = "u1",
                Email = "u1@test.com"
            });
    }

    private void SetupLedgerNoExistingKey(string key)
    {
        _mockLedgerRepo.Setup(r => r.GetByIdempotencyKeyAsync(key))
            .ReturnsAsync((BvcLedgerEntry?)null);
    }
}
