namespace BoardVerse.Core.DTOs.PaymentMasterAccount;

public class CreatePaymentMasterAccountRequestDto
{
    public string Provider { get; set; } = string.Empty;
    public string AccountHolder { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string MaskedAccountNumber { get; set; } = string.Empty;
    public string? VirtualAccountNumber { get; set; }
    public string? QrContent { get; set; }
    public string? WebhookSecret { get; set; }
}

public class UpdatePaymentMasterAccountRequestDto
{
    public string Provider { get; set; } = string.Empty;
    public string AccountHolder { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string MaskedAccountNumber { get; set; } = string.Empty;
    public string? VirtualAccountNumber { get; set; }
    public string? QrContent { get; set; }
    public bool IsActive { get; set; }
}

public class PaymentMasterAccountDto
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string AccountHolder { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string MaskedAccountNumber { get; set; } = string.Empty;
    public string? VirtualAccountNumber { get; set; }
    public string? QrContent { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PaymentMasterAccountResponseDto
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string AccountHolder { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string MaskedAccountNumber { get; set; } = string.Empty;
    public string? VirtualAccountNumber { get; set; }
    public string? QrContent { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
