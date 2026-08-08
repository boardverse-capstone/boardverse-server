using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using BoardVerse.Services.Services.Payments;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

public class SePayAccountServiceTests
{
    private readonly Mock<ISePayAccountRepository> _mockRepo;
    private readonly Mock<ICafeRepository> _mockCafeRepo;
    private readonly Mock<ILogger<SePayAccountService>> _mockLogger;
    private readonly Mock<ICurrentUserService> _mockCurrentUser;
    private readonly Mock<IVietQrClient> _mockVietQr;
    private readonly SePayAccountService _service;

    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestCafeId = Guid.NewGuid();

    public SePayAccountServiceTests()
    {
        _mockRepo = new Mock<ISePayAccountRepository>();
        _mockCafeRepo = new Mock<ICafeRepository>();
        _mockLogger = new Mock<ILogger<SePayAccountService>>();
        _mockCurrentUser = new Mock<ICurrentUserService>();
        _mockVietQr = new Mock<IVietQrClient>();

        _mockCurrentUser.Setup(x => x.GetCurrentUserId()).Returns(TestUserId);

        _service = new SePayAccountService(
            _mockRepo.Object,
            _mockCafeRepo.Object,
            _mockLogger.Object,
            _mockCurrentUser.Object,
            _mockVietQr.Object);
    }

    #region GetByIdAsync

    [Fact]
    public async Task GetByIdAsync_ExistingAccount_ReturnsDto()
    {
        var account = CreateTestAccount(TestCafeId);
        _mockRepo.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);

        var result = await _service.GetByIdAsync(account.Id);

        Assert.NotNull(result);
        Assert.Equal(account.Id, result.Id);
        Assert.Equal(account.AccountType, result.AccountType);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        _mockRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((SePayAccount?)null);

        var result = await _service.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    #endregion

    #region GetAllAsync

    [Fact]
    public async Task GetAllAsync_WithQuery_FiltersCorrectly()
    {
        var masterAccount = CreateTestAccount(null, SePayAccountType.Master);
        _mockRepo.Setup(r => r.GetAllAsync(It.IsAny<SePayAccountQuery?>()))
            .ReturnsAsync(new List<SePayAccount> { masterAccount });

        var query = new SePayAccountQuery { AccountType = SePayAccountType.Master };
        var result = await _service.GetAllAsync(query);

        Assert.Single(result);
        Assert.Equal(SePayAccountType.Master, result[0].AccountType);
    }

    #endregion

    #region CreateAsync - Master Account

    [Fact]
    public async Task CreateAsync_MasterAccount_SetsCreatedByUserId()
    {
        _mockRepo.Setup(r => r.GetMasterAccountAsync()).ReturnsAsync((SePayAccount?)null);
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<SePayAccount>())).Returns(Task.CompletedTask);

        var request = new CreateSePayAccountRequestDto
        {
            AccountType = SePayAccountType.Master,
            MerchantId = "MERCHANT-001",
            ApiKey = "API-KEY-001",
            SecretKey = "SECRET-KEY-001",
            BankCode = "MBBank",
            AccountNumber = "0855199924",
            AccountHolder = "Test Holder"
        };

        var result = await _service.CreateAsync(request);

        _mockRepo.Verify(r => r.AddAsync(It.Is<SePayAccount>(a =>
            a.CreatedByUserId == TestUserId &&
            a.AccountType == SePayAccountType.Master)), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_MasterAccount_ThrowsIfAlreadyExists()
    {
        var existingMaster = CreateTestAccount(null, SePayAccountType.Master);
        _mockRepo.Setup(r => r.GetMasterAccountAsync()).ReturnsAsync(existingMaster);

        var request = new CreateSePayAccountRequestDto { AccountType = SePayAccountType.Master };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_MasterAccount_LogsCreation()
    {
        _mockRepo.Setup(r => r.GetMasterAccountAsync()).ReturnsAsync((SePayAccount?)null);
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<SePayAccount>())).Returns(Task.CompletedTask);

        var request = new CreateSePayAccountRequestDto
        {
            AccountType = SePayAccountType.Master,
            MerchantId = "MERCHANT-001"
        };

        await _service.CreateAsync(request);

        _mockLogger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SePayAccount created")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region CreateAsync - Cafe Account

    [Fact]
    public async Task CreateAsync_CafeAccount_RequiresCafeId()
    {
        var request = new CreateSePayAccountRequestDto
        {
            AccountType = SePayAccountType.Cafe,
            CafeId = null
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_CafeAccount_ThrowsIfCafeAlreadyHasAccount()
    {
        var existingCafeAccount = CreateTestAccount(TestCafeId, SePayAccountType.Cafe);
        _mockRepo.Setup(r => r.GetByCafeIdAsync(TestCafeId)).ReturnsAsync(existingCafeAccount);

        var request = new CreateSePayAccountRequestDto
        {
            AccountType = SePayAccountType.Cafe,
            CafeId = TestCafeId
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_CafeAccount_SetsCreatedByUserIdAndCafeId()
    {
        _mockRepo.Setup(r => r.GetByCafeIdAsync(TestCafeId)).ReturnsAsync((SePayAccount?)null);
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<SePayAccount>())).Returns(Task.CompletedTask);

        var request = new CreateSePayAccountRequestDto
        {
            AccountType = SePayAccountType.Cafe,
            CafeId = TestCafeId,
            MerchantId = "MERCHANT-CAFE-001"
        };

        var result = await _service.CreateAsync(request);

        _mockRepo.Verify(r => r.AddAsync(It.Is<SePayAccount>(a =>
            a.CreatedByUserId == TestUserId &&
            a.CafeId == TestCafeId &&
            a.AccountType == SePayAccountType.Cafe)), Times.Once);
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_ExistingAccount_SetsUpdatedByUserId()
    {
        var account = CreateTestAccount(TestCafeId, SePayAccountType.Cafe);
        _mockRepo.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);

        var request = new UpdateSePayAccountRequestDto
        {
            MerchantId = "NEW-MERCHANT-001",
            BankCode = "Vietinbank"
        };

        var result = await _service.UpdateAsync(account.Id, request);

        _mockRepo.Verify(r => r.UpdateAsync(It.Is<SePayAccount>(a =>
            a.UpdatedByUserId == TestUserId &&
            a.UpdatedAt.HasValue &&
            a.MerchantId == "NEW-MERCHANT-001" &&
            a.BankCode == "Vietinbank")), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ThrowsKeyNotFoundException()
    {
        _mockRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((SePayAccount?)null);

        var request = new UpdateSePayAccountRequestDto { MerchantId = "NEW-001" };

        await Assert.ThrowsAsync<NotFoundException>(() => _service.UpdateAsync(Guid.NewGuid(), request));
    }

    [Fact]
    public async Task UpdateAsync_PartialUpdate_PreservesOtherFields()
    {
        var account = CreateTestAccount(TestCafeId, SePayAccountType.Cafe);
        account.ApiKey = "ORIGINAL-API-KEY";
        account.SecretKey = "ORIGINAL-SECRET-KEY";
        _mockRepo.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);

        var request = new UpdateSePayAccountRequestDto
        {
            BankCode = "VPBank"
        };

        var result = await _service.UpdateAsync(account.Id, request);

        Assert.Equal("ORIGINAL-API-KEY", account.ApiKey);
        Assert.Equal("ORIGINAL-SECRET-KEY", account.SecretKey);
        Assert.Equal("VPBank", account.BankCode);
    }

    [Fact]
    public async Task UpdateAsync_LogsUpdate()
    {
        var account = CreateTestAccount(TestCafeId, SePayAccountType.Cafe);
        _mockRepo.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);

        var request = new UpdateSePayAccountRequestDto { MerchantId = "NEW-001" };

        await _service.UpdateAsync(account.Id, request);

        _mockLogger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SePayAccount updated")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region SetEnvironmentAsync

    [Fact]
    public async Task SetEnvironmentAsync_ValidEnvironment_SetsUpdatedByUserId()
    {
        var account = CreateTestAccount(TestCafeId, SePayAccountType.Cafe);
        account.Environment = "Test";
        _mockRepo.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);

        var result = await _service.SetEnvironmentAsync(account.Id, "Production");

        _mockRepo.Verify(r => r.UpdateAsync(It.Is<SePayAccount>(a =>
            a.UpdatedByUserId == TestUserId &&
            a.Environment == "Production")), Times.Once);
    }

    [Fact]
    public async Task SetEnvironmentAsync_InvalidEnvironment_ThrowsArgumentException()
    {
        var account = CreateTestAccount(TestCafeId, SePayAccountType.Cafe);
        _mockRepo.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SetEnvironmentAsync(account.Id, "InvalidEnv"));
    }

    [Fact]
    public async Task SetEnvironmentAsync_LogsEnvironmentChange()
    {
        var account = CreateTestAccount(TestCafeId, SePayAccountType.Cafe);
        account.Environment = "Test";
        _mockRepo.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);

        await _service.SetEnvironmentAsync(account.Id, "Production");

        _mockLogger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("environment changed: Test -> Production")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_ExistingAccount_DeletesAndLogs()
    {
        var account = CreateTestAccount(TestCafeId, SePayAccountType.Cafe);
        _mockRepo.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);

        await _service.DeleteAsync(account.Id);

        _mockRepo.Verify(r => r.DeleteAsync(account.Id), Times.Once);
        _mockLogger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SePayAccount deleted")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ThrowsNotFoundException()
    {
        _mockRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((SePayAccount?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.DeleteAsync(Guid.NewGuid()));
    }

    #endregion

    #region ToDto - Masked Account Number

    [Fact]
    public async Task GetByIdAsync_AccountNumberIsMasked()
    {
        var account = CreateTestAccount(TestCafeId, SePayAccountType.Cafe);
        account.AccountNumber = "0855199924"; // 10 digits -> 6 asterisks + 4 digits
        _mockRepo.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);

        var result = await _service.GetByIdAsync(account.Id);

        Assert.Equal("******9924", result!.MaskedAccountNumber);
    }

    [Fact]
    public async Task GetByIdAsync_ShortAccountNumber_NotMasked()
    {
        var account = CreateTestAccount(TestCafeId, SePayAccountType.Cafe);
        account.AccountNumber = "123";
        _mockRepo.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);

        var result = await _service.GetByIdAsync(account.Id);

        Assert.Equal("123", result!.MaskedAccountNumber);
    }

    #endregion

    #region Dto Audit Fields

    [Fact]
    public async Task CreateAsync_ResponseDto_ContainsAuditFields()
    {
        _mockRepo.Setup(r => r.GetMasterAccountAsync()).ReturnsAsync((SePayAccount?)null);
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<SePayAccount>())).Returns(Task.CompletedTask);

        var request = new CreateSePayAccountRequestDto { AccountType = SePayAccountType.Master };

        var result = await _service.CreateAsync(request);

        Assert.Equal(TestUserId, result.CreatedByUserId);
        Assert.Null(result.UpdatedByUserId);
        Assert.True(result.CreatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task UpdateAsync_ResponseDto_ContainsUpdatedByUserId()
    {
        var account = CreateTestAccount(TestCafeId, SePayAccountType.Cafe);
        account.CreatedByUserId = Guid.NewGuid();
        _mockRepo.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);

        var request = new UpdateSePayAccountRequestDto { BankCode = "NewBank" };

        var result = await _service.UpdateAsync(account.Id, request);

        Assert.Equal(TestUserId, result.UpdatedByUserId);
        Assert.True(result.UpdatedAt.HasValue);
    }

    #endregion

    #region CreateByManagerCafeAsync (Manager không cần đăng ký SePay)

    [Fact]
    public async Task CreateByManagerCafeAsync_HappyPath_CreatesCafeAccountWithoutSePayCredentials()
    {
        // Arrange: Manager có cafe, cafe chưa có payment account
        _mockCafeRepo.Setup(r => r.GetCafesByManagerIdAsync(TestUserId))
            .ReturnsAsync(new List<Cafe> { new Cafe { Id = TestCafeId, Name = "Cafe", Address = "1 Test St" } });
        _mockRepo.Setup(r => r.GetByCafeIdAsync(TestCafeId)).ReturnsAsync((SePayAccount?)null);
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<SePayAccount>())).Returns(Task.CompletedTask);

        var request = new CreateCafePaymentAccountRequestDto
        {
            BankCode = "MBBank",
            AccountNumber = "1234567890",
            AccountHolder = "NGUYEN VAN A"
        };

        // Act
        var result = await _service.CreateByManagerCafeAsync(request);

        // Assert: AccountType = Cafe, KHÔNG có SePay credentials
        Assert.Equal(SePayAccountType.Cafe, result.AccountType);
        Assert.Equal(TestCafeId, result.CafeId);
        Assert.Equal("MBBank", result.BankCode);
        Assert.Equal("NGUYEN VAN A", result.AccountHolder);
        Assert.Equal("******7890", result.MaskedAccountNumber);
        Assert.Null(result.MerchantId);
        Assert.True(result.IsActive);

        _mockRepo.Verify(r => r.AddAsync(It.Is<SePayAccount>(a =>
            a.AccountType == SePayAccountType.Cafe
            && a.CafeId == TestCafeId
            && a.MerchantId == null
            && a.ApiKey == null
            && a.SecretKey == null
            && a.WebhookToken == null
            && a.Environment == "Production"
        )), Times.Once);
    }

    [Fact]
    public async Task CreateByManagerCafeAsync_MissingBankCode_ThrowsArgumentException()
    {
        _mockCafeRepo.Setup(r => r.GetCafesByManagerIdAsync(TestUserId))
            .ReturnsAsync(new List<Cafe> { new Cafe { Id = TestCafeId, Name = "Cafe", Address = "1 Test St" } });

        var request = new CreateCafePaymentAccountRequestDto
        {
            BankCode = "",
            AccountNumber = "1234567890",
            AccountHolder = "NGUYEN VAN A"
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.CreateByManagerCafeAsync(request));
        Assert.Equal(ApiErrorMessages.Payment.CafePaymentAccountBankCodeRequired, ex.Message);
    }

    [Fact]
    public async Task CreateByManagerCafeAsync_MissingAccountNumber_ThrowsArgumentException()
    {
        _mockCafeRepo.Setup(r => r.GetCafesByManagerIdAsync(TestUserId))
            .ReturnsAsync(new List<Cafe> { new Cafe { Id = TestCafeId, Name = "Cafe", Address = "1 Test St" } });

        var request = new CreateCafePaymentAccountRequestDto
        {
            BankCode = "MBBank",
            AccountNumber = "",
            AccountHolder = "NGUYEN VAN A"
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.CreateByManagerCafeAsync(request));
        Assert.Equal(ApiErrorMessages.Payment.CafePaymentAccountAccountNumberRequired, ex.Message);
    }

    [Fact]
    public async Task CreateByManagerCafeAsync_MissingAccountHolder_ThrowsArgumentException()
    {
        _mockCafeRepo.Setup(r => r.GetCafesByManagerIdAsync(TestUserId))
            .ReturnsAsync(new List<Cafe> { new Cafe { Id = TestCafeId, Name = "Cafe", Address = "1 Test St" } });

        var request = new CreateCafePaymentAccountRequestDto
        {
            BankCode = "MBBank",
            AccountNumber = "1234567890",
            AccountHolder = ""
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.CreateByManagerCafeAsync(request));
        Assert.Equal(ApiErrorMessages.Payment.CafePaymentAccountAccountHolderRequired, ex.Message);
    }

    [Fact]
    public async Task CreateByManagerCafeAsync_ManagerHasNoCafe_ThrowsNotFoundException()
    {
        _mockCafeRepo.Setup(r => r.GetCafesByManagerIdAsync(TestUserId))
            .ReturnsAsync(new List<Cafe>());

        var request = new CreateCafePaymentAccountRequestDto
        {
            BankCode = "MBBank",
            AccountNumber = "1234567890",
            AccountHolder = "NGUYEN VAN A"
        };

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateByManagerCafeAsync(request));
    }

    [Fact]
    public async Task CreateByManagerCafeAsync_CafeAlreadyHasAccount_ThrowsInvalidOperation()
    {
        _mockCafeRepo.Setup(r => r.GetCafesByManagerIdAsync(TestUserId))
            .ReturnsAsync(new List<Cafe> { new Cafe { Id = TestCafeId, Name = "Cafe", Address = "1 Test St" } });
        _mockRepo.Setup(r => r.GetByCafeIdAsync(TestCafeId))
            .ReturnsAsync(CreateTestAccount(TestCafeId, SePayAccountType.Cafe));

        var request = new CreateCafePaymentAccountRequestDto
        {
            BankCode = "MBBank",
            AccountNumber = "1234567890",
            AccountHolder = "NGUYEN VAN A"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateByManagerCafeAsync(request));
    }

    [Fact]
    public async Task CreateByManagerCafeAsync_CustomEnvironment_NormalizedToProduction()
    {
        _mockCafeRepo.Setup(r => r.GetCafesByManagerIdAsync(TestUserId))
            .ReturnsAsync(new List<Cafe> { new Cafe { Id = TestCafeId, Name = "Cafe", Address = "1 Test St" } });
        _mockRepo.Setup(r => r.GetByCafeIdAsync(TestCafeId)).ReturnsAsync((SePayAccount?)null);
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<SePayAccount>())).Returns(Task.CompletedTask);

        var request = new CreateCafePaymentAccountRequestDto
        {
            BankCode = "MBBank",
            AccountNumber = "1234567890",
            AccountHolder = "NGUYEN VAN A",
            Environment = "PRODUCTION"
        };

        var result = await _service.CreateByManagerCafeAsync(request);

        Assert.Equal("Production", result.Environment);
    }

    [Fact]
    public async Task CreateByManagerCafeAsync_InvalidEnvironment_ThrowsArgumentException()
    {
        _mockCafeRepo.Setup(r => r.GetCafesByManagerIdAsync(TestUserId))
            .ReturnsAsync(new List<Cafe> { new Cafe { Id = TestCafeId, Name = "Cafe", Address = "1 Test St" } });

        var request = new CreateCafePaymentAccountRequestDto
        {
            BankCode = "MBBank",
            AccountNumber = "1234567890",
            AccountHolder = "NGUYEN VAN A",
            Environment = "Staging"
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.CreateByManagerCafeAsync(request));
    }

    #endregion

    #region GenerateTestQrByManagerCafeAsync

    [Fact]
    public async Task GenerateTestQrByManagerCafeAsync_HappyPath_ReturnsQrUrlWithTenThousandVnd()
    {
        var account = CreateTestAccount(TestCafeId, SePayAccountType.Cafe);
        account.BankCode = "MBBank";
        account.AccountNumber = "1234567890";
        account.AccountHolder = "NGUYEN VAN A";

        _mockCafeRepo.Setup(r => r.GetCafesByManagerIdAsync(TestUserId))
            .ReturnsAsync(new List<Cafe> { new Cafe { Id = TestCafeId, Name = "Cafe", Address = "1 Test" } });
        _mockRepo.Setup(r => r.GetByCafeIdAsync(TestCafeId)).ReturnsAsync(account);
        _mockVietQr.Setup(q => q.GenerateQrUrl(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((string bank, string acc, decimal amount, string? des, string? holder, string tmpl, bool show)
                => $"https://vietqr.app/img?bank={bank}&acc={acc}&amount={(int)amount}&des={des}&holder={holder}");

        var result = await _service.GenerateTestQrByManagerCafeAsync();

        Assert.NotNull(result);
        Assert.Equal(10_000m, result.TestAmount);
        Assert.Equal("MBBank", result.BankCode);
        Assert.Equal("NGUYEN VAN A", result.AccountHolder);
        Assert.Equal("******7890", result.MaskedAccountNumber);
        Assert.StartsWith("https://vietqr.app/img?", result.QrUrl);
        Assert.Contains("bank=MBBank", result.QrUrl);
        Assert.Contains("acc=1234567890", result.QrUrl);
        Assert.Contains("amount=10000", result.QrUrl);
        Assert.Contains("holder=NGUYEN", result.QrUrl);
        Assert.StartsWith("BV-TEST-", result.TestTransferContent);
        Assert.NotEmpty(result.Instructions);
    }

    [Fact]
    public async Task GenerateTestQrByManagerCafeAsync_ManagerHasNoCafe_ThrowsNotFoundException()
    {
        _mockCafeRepo.Setup(r => r.GetCafesByManagerIdAsync(TestUserId))
            .ReturnsAsync(new List<Cafe>());

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.GenerateTestQrByManagerCafeAsync());
    }

    [Fact]
    public async Task GenerateTestQrByManagerCafeAsync_CafeNoAccount_ThrowsNotFoundException()
    {
        _mockCafeRepo.Setup(r => r.GetCafesByManagerIdAsync(TestUserId))
            .ReturnsAsync(new List<Cafe> { new Cafe { Id = TestCafeId, Name = "Cafe", Address = "1 Test" } });
        _mockRepo.Setup(r => r.GetByCafeIdAsync(TestCafeId)).ReturnsAsync((SePayAccount?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.GenerateTestQrByManagerCafeAsync());
    }

    [Fact]
    public async Task GenerateTestQrByManagerCafeAsync_BankInfoIncomplete_ThrowsInvalidOperation()
    {
        var account = CreateTestAccount(TestCafeId, SePayAccountType.Cafe);
        account.BankCode = null; // simulate data corruption

        _mockCafeRepo.Setup(r => r.GetCafesByManagerIdAsync(TestUserId))
            .ReturnsAsync(new List<Cafe> { new Cafe { Id = TestCafeId, Name = "Cafe", Address = "1 Test" } });
        _mockRepo.Setup(r => r.GetByCafeIdAsync(TestCafeId)).ReturnsAsync(account);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.GenerateTestQrByManagerCafeAsync());
        Assert.Equal(ApiErrorMessages.Payment.SePayBankInfoIncomplete, ex.Message);
    }

    [Fact]
    public async Task GenerateTestQrByManagerCafeAsync_CalledMultipleTimes_GeneratesUniqueTransferContent()
    {
        var account = CreateTestAccount(TestCafeId, SePayAccountType.Cafe);
        _mockCafeRepo.Setup(r => r.GetCafesByManagerIdAsync(TestUserId))
            .ReturnsAsync(new List<Cafe> { new Cafe { Id = TestCafeId, Name = "Cafe", Address = "1 Test" } });
        _mockRepo.Setup(r => r.GetByCafeIdAsync(TestCafeId)).ReturnsAsync(account);

        var result1 = await _service.GenerateTestQrByManagerCafeAsync();
        var result2 = await _service.GenerateTestQrByManagerCafeAsync();

        Assert.NotEqual(result1.TestTransferContent, result2.TestTransferContent);
    }

    #endregion

    #region Helpers

    private static SePayAccount CreateTestAccount(Guid? cafeId, SePayAccountType? accountType = null)
    {
        return new SePayAccount
        {
            Id = Guid.NewGuid(),
            AccountType = accountType ?? (cafeId.HasValue ? SePayAccountType.Cafe : SePayAccountType.Master),
            CafeId = cafeId,
            MerchantId = "MERCHANT-TEST",
            ApiKey = "API-KEY-TEST",
            SecretKey = "SECRET-TEST",
            WebhookToken = "WEBHOOK-TEST",
            ApiBaseUrl = "https://api.sepay.vn",
            BankCode = "MBBank",
            AccountNumber = "0855199924",
            AccountHolder = "Test Holder",
            ReturnUrl = "https://example.com/return",
            Environment = "Test",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            Cafe = cafeId.HasValue ? new Cafe
            {
                Id = cafeId.Value,
                Name = "Test Cafe",
                Address = "123 Test St"
            } : null
        };
    }

    #endregion
}
