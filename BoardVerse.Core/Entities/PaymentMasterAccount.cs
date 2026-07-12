namespace BoardVerse.Core.Entities;

/// <summary>
/// Tài khoản master của BoardVerse dùng để nhận và tạm giữ tiền cọc.
/// Chỉ phục vụ nghiệp vụ settlement, không dùng cho auth/role.
/// </summary>
public class PaymentMasterAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = string.Empty;
    public string AccountHolder { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string MaskedAccountNumber { get; set; } = string.Empty;
    public string? VirtualAccountNumber { get; set; }
    public string? QrContent { get; set; }
    public string? WebhookSecret { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
