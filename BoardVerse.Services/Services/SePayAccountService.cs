using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services.Payments;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

public class SePayAccountService : ISePayAccountService
{
    private readonly ISePayAccountRepository _repository;
    private readonly ICafeRepository _cafeRepository;
    private readonly ILogger<SePayAccountService> _logger;
    private readonly ICurrentUserService _currentUserService;
    private readonly IVietQrClient _vietQrClient;

    public SePayAccountService(
        ISePayAccountRepository repository,
        ICafeRepository cafeRepository,
        ILogger<SePayAccountService> logger,
        ICurrentUserService currentUserService,
        IVietQrClient vietQrClient)
    {
        _repository = repository;
        _cafeRepository = cafeRepository;
        _logger = logger;
        _currentUserService = currentUserService;
        _vietQrClient = vietQrClient;
    }

    private Guid? GetCurrentUserId() => _currentUserService.GetCurrentUserId();

    private async Task<Guid?> GetCurrentUserCafeIdAsync()
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return null;

        var cafes = await _cafeRepository.GetCafesByManagerIdAsync(userId.Value);
        return cafes.FirstOrDefault()?.Id;
    }

    public async Task<SePayAccountDto?> GetByIdAsync(Guid id)
    {
        var account = await _repository.GetByIdAsync(id);
        return account == null ? null : ToDto(account);
    }

    public async Task<SePayAccountDto?> GetByCafeIdAsync(Guid cafeId)
    {
        var account = await _repository.GetByCafeIdAsync(cafeId);
        return account == null ? null : ToDto(account);
    }

    public async Task<SePayAccountDto?> GetMasterAccountAsync()
    {
        var account = await _repository.GetMasterAccountAsync();
        return account == null ? null : ToDto(account);
    }

    public Task<SePayAccount?> GetRawMasterAccountAsync()
    {
        return _repository.GetMasterAccountAsync();
    }

    public async Task<SePayAccount?> GetRawByCafeIdAsync(Guid cafeId)
    {
        return await _repository.GetByCafeIdAsync(cafeId);
    }

    public async Task<IReadOnlyList<SePayAccountDto>> GetAllAsync(SePayAccountQuery? query = null)
    {
        var accounts = await _repository.GetAllAsync(query);
        return accounts.Select(ToDto).ToList();
    }

    public async Task<SePayAccountDto> CreateAsync(CreateSePayAccountRequestDto request)
    {
        // Validate CafeId if AccountType is Cafe
if (request.AccountType == SePayAccountType.Cafe)
{
if (!request.CafeId.HasValue)
{
throw new ArgumentException(ApiErrorMessages.Payment.SePayCafeIdRequired);
}

var existing = await _repository.GetByCafeIdAsync(request.CafeId.Value);
if (existing != null)
{
throw new InvalidOperationException(ApiErrorMessages.Payment.SePayCafeAccountExists(request.CafeId.Value));
}
}
else if (request.AccountType == SePayAccountType.Master)
{
var existingMaster = await _repository.GetMasterAccountAsync();
if (existingMaster != null)
{
throw new InvalidOperationException(ApiErrorMessages.Payment.SePayMasterAccountExists);
}
}

        var account = new SePayAccount
        {
            AccountType = request.AccountType,
            CafeId = request.CafeId,
            MerchantId = request.MerchantId,
            ApiKey = request.ApiKey,
            SecretKey = request.SecretKey,
            WebhookToken = request.WebhookToken,
            ApiBaseUrl = request.ApiBaseUrl,
            BankCode = request.BankCode,
            AccountNumber = request.AccountNumber,
            AccountHolder = request.AccountHolder,
            ReturnUrl = request.ReturnUrl,
            Environment = request.Environment ?? "Production",
            IsActive = true,
            CreatedByUserId = GetCurrentUserId()
        };

        await _repository.AddAsync(account);
        await _repository.SaveChangesAsync();

        _logger.LogInformation("SePayAccount created. Id={Id}, Type={Type}, CafeId={CafeId}, ByUser={UserId}", 
            account.Id, account.AccountType, account.CafeId, account.CreatedByUserId);

        return ToDto(account);
    }

    public async Task<SePayAccountDto> UpdateAsync(Guid id, UpdateSePayAccountRequestDto request)
    {
        var account = await _repository.GetByIdAsync(id)
            ?? throw new NotFoundException(ApiErrorMessages.Payment.SePayAccountNotFound(id));

        if (request.MerchantId != null) account.MerchantId = request.MerchantId;
        if (request.ApiKey != null) account.ApiKey = request.ApiKey;
        if (request.SecretKey != null) account.SecretKey = request.SecretKey;
        if (request.WebhookToken != null) account.WebhookToken = request.WebhookToken;
        if (request.ApiBaseUrl != null) account.ApiBaseUrl = request.ApiBaseUrl;
        if (request.BankCode != null) account.BankCode = request.BankCode;
        if (request.AccountNumber != null) account.AccountNumber = request.AccountNumber;
        if (request.AccountHolder != null) account.AccountHolder = request.AccountHolder;
        if (request.ReturnUrl != null) account.ReturnUrl = request.ReturnUrl;
        if (request.Environment != null) account.Environment = request.Environment;
        if (request.IsActive.HasValue) account.IsActive = request.IsActive.Value;
        account.UpdatedByUserId = GetCurrentUserId();
        account.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(account);
        await _repository.SaveChangesAsync();

        _logger.LogInformation("SePayAccount updated. Id={Id}, ByUser={UserId}", id, account.UpdatedByUserId);

        return ToDto(account);
    }

    public async Task<SePayAccountDto> SetEnvironmentAsync(Guid id, string environment)
    {
        var validEnvironments = new[] { "Test", "Production" };
        var normalizedEnv = char.ToUpper(environment[0]) + environment[1..].ToLower();
        
        if (!validEnvironments.Contains(normalizedEnv))
        {
            throw new ArgumentException(ApiErrorMessages.Payment.SePayInvalidEnvironment(environment));
        }

        var account = await _repository.GetByIdAsync(id)
            ?? throw new NotFoundException(ApiErrorMessages.Payment.SePayAccountNotFound(id));

        var oldEnv = account.Environment;
        account.Environment = normalizedEnv;
        account.UpdatedByUserId = GetCurrentUserId();
        account.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(account);
        await _repository.SaveChangesAsync();

        _logger.LogInformation(
            "SePayAccount {Id} environment changed: {OldEnv} -> {NewEnv}, ByUser={UserId}", 
            id, oldEnv, normalizedEnv, account.UpdatedByUserId);

        return ToDto(account);
    }

    public async Task DeleteAsync(Guid id)
    {
        var account = await _repository.GetByIdAsync(id)
            ?? throw new NotFoundException(ApiErrorMessages.Payment.SePayAccountNotFound(id));

        await _repository.DeleteAsync(id);
        await _repository.SaveChangesAsync();

        _logger.LogInformation("SePayAccount deleted. Id={Id}", id);
    }

    public async Task<SePayAccountDto?> GetByManagerCafeAsync()
    {
        var cafeId = await GetCurrentUserCafeIdAsync();
        if (!cafeId.HasValue) return null;

        var account = await _repository.GetByCafeIdAsync(cafeId.Value);
        return account == null ? null : ToDto(account);
    }

    public async Task<SePayAccountDto> CreateByManagerCafeAsync(CreateCafePaymentAccountRequestDto request)
    {
        // 1. Lấy cafe của Manager hiện tại
        var cafeId = await GetCurrentUserCafeIdAsync()
            ?? throw new NotFoundException(ApiErrorMessages.Payment.ManagerHasNoCafe);

        // 2. Validate 4 field bắt buộc — fail-fast với message rõ ràng
        if (string.IsNullOrWhiteSpace(request.BankCode))
            throw new ArgumentException(ApiErrorMessages.Payment.CafePaymentAccountBankCodeRequired);
        if (string.IsNullOrWhiteSpace(request.AccountNumber))
            throw new ArgumentException(ApiErrorMessages.Payment.CafePaymentAccountAccountNumberRequired);
        if (string.IsNullOrWhiteSpace(request.AccountHolder))
            throw new ArgumentException(ApiErrorMessages.Payment.CafePaymentAccountAccountHolderRequired);

        // 3. Kiểm tra cafe chưa có payment account (mỗi cafe chỉ có 1)
        var existing = await _repository.GetByCafeIdAsync(cafeId);
        if (existing != null)
            throw new InvalidOperationException(ApiErrorMessages.Payment.CafePaymentAccountAlreadyExists(cafeId));

        // 4. Tạo SePayAccount với AccountType = Cafe, KHÔNG đụng SePay credentials
        var account = new SePayAccount
        {
            AccountType = SePayAccountType.Cafe,
            CafeId = cafeId,
            // KHÔNG set MerchantId/ApiKey/SecretKey/WebhookToken — Manager không cần đăng ký SePay.
            // Bank info là đủ để VietQR sinh QR và SePay detect giao dịch (bank_mode=all).
            BankCode = request.BankCode.Trim(),
            AccountNumber = request.AccountNumber.Trim(),
            AccountHolder = request.AccountHolder.Trim(),
            Environment = string.IsNullOrWhiteSpace(request.Environment) ? "Production" : NormalizeEnvironment(request.Environment!),
            IsActive = true,
            CreatedByUserId = GetCurrentUserId()
        };

        await _repository.AddAsync(account);
        await _repository.SaveChangesAsync();

        // 5. BUGFIX: Link Cafe.SePayAccountId → SePayAccount vừa tạo.
        // Trước đây thiếu bước này khiến CreateSessionPaymentAsync ở PaymentService
        // luôn check cafe.SePayAccountId.HasValue() == false → throw
        // PaymentCafeNotConfiguredSePay ngay cả khi SePayAccount đã tồn tại.
        var cafe = await _cafeRepository.GetByIdAsync(cafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.CafeRecordNotFound(cafeId));
        cafe.SePayAccountId = account.Id;
        cafe.UpdatedAt = DateTime.UtcNow;
        await _cafeRepository.SaveChangesAsync();

        _logger.LogInformation(
            "SePayAccount for cafe {CafeId} created by manager. Id={Id}, BankCode={BankCode}, ByUser={UserId}; Cafe.SePayAccountId linked.",
            cafeId, account.Id, account.BankCode, account.CreatedByUserId);

        return ToDto(account);
    }

    private static string NormalizeEnvironment(string environment)
    {
        var validEnvironments = new[] { "Test", "Production" };
        var normalizedEnv = char.ToUpper(environment[0]) + environment[1..].ToLower();
        if (!validEnvironments.Contains(normalizedEnv))
            throw new ArgumentException(ApiErrorMessages.Payment.SePayInvalidEnvironment(environment));
        return normalizedEnv;
    }

    public async Task<CafePaymentQrPreviewDto> GenerateTestQrByManagerCafeAsync()
    {
        // 1. Lấy cafe + payment account của Manager
        var cafeId = await GetCurrentUserCafeIdAsync()
            ?? throw new NotFoundException(ApiErrorMessages.Payment.ManagerHasNoCafe);

        var account = await _repository.GetByCafeIdAsync(cafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Payment.CafeSePayAccountNotConfigured);

        // 2. Validate bank info có đủ để gen QR
        if (string.IsNullOrWhiteSpace(account.BankCode)
            || string.IsNullOrWhiteSpace(account.AccountNumber)
            || string.IsNullOrWhiteSpace(account.AccountHolder))
        {
            throw new InvalidOperationException(ApiErrorMessages.Payment.SePayBankInfoIncomplete);
        }

        // 3. Sinh transfer content unique để Manager dễ identify giao dịch test
        var testContent = $"BV-TEST-{Guid.NewGuid():N}".Substring(0, 20);

        // 4. Gen VietQR URL — số tiền test 10.000 VND
        const decimal testAmount = 10_000m;
        var qrUrl = _vietQrClient.GenerateQrUrl(
            bankCode: account.BankCode,
            accountNumber: account.AccountNumber,
            amount: testAmount,
            description: testContent,
            accountHolder: account.AccountHolder,
            template: "compact",
            showInfo: true);

        _logger.LogInformation(
            "Test QR generated for cafe {CafeId}. Amount={Amount}, Content={Content}",
            cafeId, testAmount, testContent);

        return new CafePaymentQrPreviewDto
        {
            QrUrl = qrUrl,
            TestAmount = testAmount,
            TestTransferContent = testContent,
            BankCode = account.BankCode,
            MaskedAccountNumber = MaskAccountNumber(account.AccountNumber) ?? account.AccountNumber,
            AccountHolder = account.AccountHolder,
            Instructions =
                "1. Mở app ngân hàng và quét QR trên.\n" +
                "2. Xác nhận số tiền 10.000 VND và nội dung CK đúng như hiển thị.\n" +
                "3. Sau khi CK thành công, SePay sẽ gửi webhook về BoardVerse trong vòng 1-2 phút.\n" +
                "4. Nếu SePay KHÔNG detect được (không thấy log webhook), liên hệ admin để kiểm tra TK đã được link vào SePay company chưa."
        };
    }

    public async Task<SePayAccountDto> UpdateByManagerCafeAsync(UpdateSePayAccountRequestDto request)
    {
        var cafeId = await GetCurrentUserCafeIdAsync()
            ?? throw new NotFoundException(ApiErrorMessages.Payment.ManagerHasNoCafe);

        var account = await _repository.GetByCafeIdAsync(cafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Payment.CafeSePayAccountNotConfigured);

        if (request.MerchantId != null) account.MerchantId = request.MerchantId;
        if (request.ApiKey != null) account.ApiKey = request.ApiKey;
        if (request.SecretKey != null) account.SecretKey = request.SecretKey;
        if (request.WebhookToken != null) account.WebhookToken = request.WebhookToken;
        if (request.ApiBaseUrl != null) account.ApiBaseUrl = request.ApiBaseUrl;
        if (request.BankCode != null) account.BankCode = request.BankCode;
        if (request.AccountNumber != null) account.AccountNumber = request.AccountNumber;
        if (request.AccountHolder != null) account.AccountHolder = request.AccountHolder;
        if (request.ReturnUrl != null) account.ReturnUrl = request.ReturnUrl;
        if (request.Environment != null) account.Environment = request.Environment;
        if (request.IsActive.HasValue) account.IsActive = request.IsActive.Value;
        account.UpdatedByUserId = GetCurrentUserId();
        account.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(account);
        await _repository.SaveChangesAsync();

        _logger.LogInformation("SePayAccount for cafe {CafeId} updated by manager. Id={Id}", cafeId, account.Id);

        return ToDto(account);
    }

    public async Task<SePayAccountDto> SetEnvironmentByManagerCafeAsync(string environment)
    {
        var validEnvironments = new[] { "Test", "Production" };
        var normalizedEnv = char.ToUpper(environment[0]) + environment[1..].ToLower();

        if (!validEnvironments.Contains(normalizedEnv))
        {
            throw new ArgumentException(ApiErrorMessages.Payment.SePayInvalidEnvironment(environment));
        }

        var cafeId = await GetCurrentUserCafeIdAsync()
            ?? throw new NotFoundException(ApiErrorMessages.Payment.ManagerHasNoCafe);

        var account = await _repository.GetByCafeIdAsync(cafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Payment.CafeSePayAccountNotConfigured);

        var oldEnv = account.Environment;
        account.Environment = normalizedEnv;
        account.UpdatedByUserId = GetCurrentUserId();
        account.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(account);
        await _repository.SaveChangesAsync();

        _logger.LogInformation(
            "SePayAccount for cafe {CafeId} environment changed: {OldEnv} -> {NewEnv}, ByUser={UserId}",
            cafeId, oldEnv, normalizedEnv, account.UpdatedByUserId);

        return ToDto(account);
    }

    private static SePayAccountDto ToDto(SePayAccount account)
    {
        return new SePayAccountDto
        {
            Id = account.Id,
            AccountType = account.AccountType,
            CafeId = account.CafeId,
            CafeName = account.Cafe?.Name,
            MerchantId = account.MerchantId,
            ApiBaseUrl = account.ApiBaseUrl,
            BankCode = account.BankCode,
            MaskedAccountNumber = MaskAccountNumber(account.AccountNumber),
            AccountHolder = account.AccountHolder,
            ReturnUrl = account.ReturnUrl,
            Environment = account.Environment,
            IsActive = account.IsActive,
            CreatedByUserId = account.CreatedByUserId,
            UpdatedByUserId = account.UpdatedByUserId,
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt
        };
    }

    private static string? MaskAccountNumber(string? accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber) || accountNumber.Length <= 4)
            return accountNumber;

        return new string('*', accountNumber.Length - 4) + accountNumber[^4..];
    }
}
