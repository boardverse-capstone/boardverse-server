using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

public class SePayAccountService : ISePayAccountService
{
    private readonly ISePayAccountRepository _repository;
    private readonly ICafeRepository _cafeRepository;
    private readonly ILogger<SePayAccountService> _logger;
    private readonly ICurrentUserService _currentUserService;

    public SePayAccountService(
        ISePayAccountRepository repository,
        ICafeRepository cafeRepository,
        ILogger<SePayAccountService> logger,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _cafeRepository = cafeRepository;
        _logger = logger;
        _currentUserService = currentUserService;
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
                throw new ArgumentException("CafeId is required for Cafe account type.");
            }

            var existing = await _repository.GetByCafeIdAsync(request.CafeId.Value);
            if (existing != null)
            {
                throw new InvalidOperationException($"Cafe '{request.CafeId}' already has a SePay account.");
            }
        }
        else if (request.AccountType == SePayAccountType.Master)
        {
            var existingMaster = await _repository.GetMasterAccountAsync();
            if (existingMaster != null)
            {
                throw new InvalidOperationException("Master account already exists.");
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
            ?? throw new KeyNotFoundException($"SePay account not found: {id}");

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
            throw new ArgumentException($"Invalid environment. Must be 'Test' or 'Production'. Got: '{environment}'");
        }

        var account = await _repository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"SePay account not found: {id}");

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
            ?? throw new KeyNotFoundException($"SePay account not found: {id}");

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

    public async Task<SePayAccountDto> UpdateByManagerCafeAsync(UpdateSePayAccountRequestDto request)
    {
        var cafeId = await GetCurrentUserCafeIdAsync()
            ?? throw new KeyNotFoundException("Bạn không quản lý cafe nào.");

        var account = await _repository.GetByCafeIdAsync(cafeId)
            ?? throw new KeyNotFoundException($"Cafe của bạn chưa được cấu hình SePay.");

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
            throw new ArgumentException($"Invalid environment. Must be 'Test' or 'Production'. Got: '{environment}'");
        }

        var cafeId = await GetCurrentUserCafeIdAsync()
            ?? throw new KeyNotFoundException("Bạn không quản lý cafe nào.");

        var account = await _repository.GetByCafeIdAsync(cafeId)
            ?? throw new KeyNotFoundException($"Cafe của bạn chưa được cấu hình SePay.");

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
