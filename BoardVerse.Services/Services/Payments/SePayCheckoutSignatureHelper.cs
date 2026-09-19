using System.Security.Cryptography;
using System.Text;

namespace BoardVerse.Services.Services.Payments;

/// <summary>
/// Shared helper cho SePay checkout signature.
/// Dùng chung bởi <see cref="PaymentGatewayService"/> và <c>DebugSePayController</c>.
/// Spec: sepay-payment-flow.mdc §VI.1.
/// </summary>
public static class SePayCheckoutSignatureHelper
{
    /// <summary>
    /// Build signing string theo field order cố định của SePay checkout API.
    /// Skip fields rỗng. Giữ nguyên field order — không reorder.
    /// </summary>
    public static string BuildSigningString(
        decimal amount,
        string merchant,
        string description,
        string orderInvoiceNumber,
        string customerId,
        string successUrl,
        string errorUrl,
        string cancelUrl)
    {
        // Field order BẮT BUỘC theo spec §VI.1 — không thay đổi.
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(orderInvoiceNumber))
            parts.Add($"order_invoice_number={orderInvoiceNumber}");
        if (!string.IsNullOrWhiteSpace(merchant))
            parts.Add($"merchant={merchant}");
        if (!string.IsNullOrWhiteSpace(customerId))
            parts.Add($"customer_id={customerId}");
        parts.Add($"order_amount={((int)amount)}");
        if (!string.IsNullOrWhiteSpace(description))
            parts.Add($"order_description={description}");
        parts.Add($"payment_method=qr_vietqr");
        parts.Add($"currency=VND");
        parts.Add($"operation=payment");
        if (!string.IsNullOrWhiteSpace(successUrl))
            parts.Add($"success_url={successUrl}");
        if (!string.IsNullOrWhiteSpace(errorUrl))
            parts.Add($"error_url={errorUrl}");
        if (!string.IsNullOrWhiteSpace(cancelUrl))
            parts.Add($"cancel_url={cancelUrl}");

        return string.Join("&", parts);
    }

    /// <summary>
    /// Compute HMAC-SHA256: Base64(HMAC-SHA256(secretKey, signingString)).
    /// </summary>
    public static string ComputeSignature(string secretKey, string signingString)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        var messageBytes = Encoding.UTF8.GetBytes(signingString);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(messageBytes);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Convenience: build signing string + compute signature trong 1 lời gọi.
    /// </summary>
    public static string SignCheckout(
        decimal amount,
        string merchant,
        string description,
        string orderInvoiceNumber,
        string customerId,
        string successUrl,
        string errorUrl,
        string cancelUrl,
        string secretKey)
    {
        var signingString = BuildSigningString(
            amount, merchant, description, orderInvoiceNumber,
            customerId, successUrl, errorUrl, cancelUrl);
        return ComputeSignature(secretKey, signingString);
    }
}
