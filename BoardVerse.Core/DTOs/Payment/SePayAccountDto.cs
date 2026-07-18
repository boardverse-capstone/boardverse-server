using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Payment;

public class SePayAccountDto
{
    public Guid Id { get; set; }
    public SePayAccountType AccountType { get; set; }
    public Guid? CafeId { get; set; }
    public string? CafeName { get; set; }
    public string? MerchantId { get; set; }
    public string? ApiBaseUrl { get; set; }
    public string? BankCode { get; set; }
    public string? MaskedAccountNumber { get; set; }
    public string? AccountHolder { get; set; }
    public string? ReturnUrl { get; set; }
    public string? Environment { get; set; }
    public bool IsActive { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateSePayAccountRequestDto
{
    public SePayAccountType AccountType { get; set; }
    public Guid? CafeId { get; set; }
    public string? MerchantId { get; set; }
    public string? ApiKey { get; set; }
    public string? SecretKey { get; set; }
    public string? WebhookToken { get; set; }
    public string? ApiBaseUrl { get; set; }
    public string? BankCode { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountHolder { get; set; }
    public string? ReturnUrl { get; set; }
    public string? Environment { get; set; }
}

public class UpdateSePayAccountRequestDto
{
    public string? MerchantId { get; set; }
    public string? ApiKey { get; set; }
    public string? SecretKey { get; set; }
    public string? WebhookToken { get; set; }
    public string? ApiBaseUrl { get; set; }
    public string? BankCode { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountHolder { get; set; }
    public string? ReturnUrl { get; set; }
    public string? Environment { get; set; }
    public bool? IsActive { get; set; }
}

public class SePayAccountQuery
{
    public SePayAccountType? AccountType { get; set; }
    public Guid? CafeId { get; set; }
    public bool? IsActive { get; set; }
}

public class SetEnvironmentRequestDto
{
    public string Environment { get; set; } = null!;
}
