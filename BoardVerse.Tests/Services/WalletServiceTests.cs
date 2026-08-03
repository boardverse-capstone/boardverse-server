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
        _mockTopUpRepo.Setup(r => r.GetByIdempotencyKeyAsync(key))
            .ReturnsAsync(existingTopUp);

        var dto = await _service.CreateTopUpAsync(userId, req);

        Assert.Equal("BVC-OLD-ORDER", dto.OrderId);
        Assert.Equal(100, dto.ExpectedBvc);
        _mockGateway.Verify(
            g => g.CreatePaymentAsync(It.IsAny<PaymentGatewayRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
