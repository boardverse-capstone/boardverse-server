using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services.Payments;

public interface IVietQrClient
{
    /// <summary>
    /// Generate VietQR image URL using SePay's vietqr.app/img format.
    /// </summary>
    /// <param name="bankCode">Ngân hàng nhận (VD: MBBank, VietinBank, ...)</param>
    /// <param name="accountNumber">Số tài khoản thụ hưởng</param>
    /// <param name="amount">Số tiền chuyển khoản</param>
    /// <param name="description">Nội dung chuyển khoản (tự động URL-encoded)</param>
    /// <param name="accountHolder">Tên chủ tài khoản (hiển thị trên QR)</param>
    /// <param name="template">Kiểu hiển thị: compact, qr-only, detail</param>
    /// <param name="showInfo">Hiển thị thông tin tài khoản trên ảnh QR</param>
    string GenerateQrUrl(
        string bankCode,
        string accountNumber,
        decimal amount,
        string? description = null,
        string? accountHolder = null,
        string template = "compact",
        bool showInfo = true);
}

public class VietQrClient : IVietQrClient
{
    private readonly ILogger<VietQrClient> _logger;
    private const string BaseUrl = "https://vietqr.app/img";

    public VietQrClient(ILogger<VietQrClient> logger)
    {
        _logger = logger;
    }

    public string GenerateQrUrl(
        string bankCode,
        string accountNumber,
        decimal amount,
        string? description = null,
        string? accountHolder = null,
        string template = "compact",
        bool showInfo = true)
    {
        if (string.IsNullOrWhiteSpace(bankCode))
            throw new ArgumentException("Bank code is required", nameof(bankCode));
        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new ArgumentException("Account number is required", nameof(accountNumber));
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than 0", nameof(amount));

        var parts = new List<string>
        {
            $"bank={Uri.EscapeDataString(bankCode)}",
            $"acc={Uri.EscapeDataString(accountNumber)}",
            $"template={Uri.EscapeDataString(template)}",
            $"amount={((int)amount)}",
            $"showinfo={(showInfo ? "true" : "false")}",
            $"fullacc=true"
        };

        if (!string.IsNullOrWhiteSpace(description))
            parts.Add($"des={Uri.EscapeDataString(description)}");

        if (!string.IsNullOrWhiteSpace(accountHolder))
            parts.Add($"holder={Uri.EscapeDataString(accountHolder)}");

        var url = $"{BaseUrl}?{string.Join("&", parts)}";

        _logger.LogInformation(
            "VietQR generated. Bank={Bank}, Account={Account}, Amount={Amount}, Template={Template}",
            bankCode, accountNumber, amount, template);

        return url;
    }
}
