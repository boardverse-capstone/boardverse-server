using System.Web;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services.Payments;

public interface IVietQrClient
{
    /// <summary>
    /// Generate VietQR image URL for direct QR display.
    /// Fallback khi SePay không hoạt động.
    /// </summary>
    string GenerateQrUrl(string bankCode, string accountNumber, decimal amount, string? accountName = null);

    /// <summary>
    /// Generate VietQR với custom template.
    /// Templates: compact, qr-only, detail
    /// </summary>
    string GenerateQrUrl(string bankCode, string accountNumber, decimal amount, string template, string? accountName = null);
}

public class VietQrClient : IVietQrClient
{
    private readonly ILogger<VietQrClient> _logger;
    private const string VietQrBaseUrl = "https://img.vietqr.io";

    public VietQrClient(ILogger<VietQrClient> logger)
    {
        _logger = logger;
    }

    public string GenerateQrUrl(string bankCode, string accountNumber, decimal amount, string? accountName = null)
    {
        return GenerateQrUrl(bankCode, accountNumber, amount, "compact", accountName);
    }

    public string GenerateQrUrl(string bankCode, string accountNumber, decimal amount, string template, string? accountName = null)
    {
        if (string.IsNullOrWhiteSpace(bankCode))
        {
            throw new ArgumentException("Bank code is required", nameof(bankCode));
        }

        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            throw new ArgumentException("Account number is required", nameof(accountNumber));
        }

        if (amount <= 0)
        {
            throw new ArgumentException("Amount must be greater than 0", nameof(amount));
        }

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["bank"] = bankCode;
        query["acc"] = accountNumber;
        query["template"] = template.ToLowerInvariant();
        query["amount"] = ((int)amount).ToString();
        query["addInfo"] = $"Thanh toan BoardVerse";

        if (!string.IsNullOrWhiteSpace(accountName))
        {
            query["accountName"] = accountName;
        }

        var qrUrl = $"{VietQrBaseUrl}/image?{query}";

        _logger.LogInformation(
            "VietQR generated. Bank={Bank}, Account={Account}, Amount={Amount}, Template={Template}",
            bankCode, accountNumber, amount, template);

        return qrUrl;
    }
}
