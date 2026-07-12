using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.Services.Services.Payments;

public interface ISePayClient
{
    /// <summary>Dùng SePay của BoardVerse (cho booking deposit).</summary>
    Task<string> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Dùng SePay của cafe (cho session payment).</summary>
    Task<string> CreatePaymentAsync(CreatePaymentRequest request, CafeSePayConfig cafeConfig, CancellationToken cancellationToken = default);

    Task<SePayTransferResponse> CreateTransferAsync(CreateTransferRequest request, CancellationToken cancellationToken = default);

    Task<bool> VerifyWebhookAsync(string signature, string payload);
}

public class SePayClient : ISePayClient
{
    private readonly SePaySettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<SePayClient> _logger;

    public SePayClient(
        HttpClient httpClient,
        IOptions<SePaySettings> settings,
        ILogger<SePayClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.MerchantId))
        {
            throw new PaymentException(ApiErrorMessages.Payment.SePayMerchantIdMissing);
        }

        if (string.IsNullOrWhiteSpace(_settings.SecretKey))
        {
            throw new PaymentException("Thiếu SePay SecretKey.");
        }

        var orderInvoiceNumber = request.OrderId;
        var operation = "PURCHASE";
        var successUrl = _settings.ReturnUrl;
        var errorUrl = _settings.ReturnUrl;
        var cancelUrl = _settings.CancelUrl;

        var formValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["api_key"] = _settings.ApiKey,
            ["merchant"] = _settings.MerchantId,
            ["order_amount"] = ((int)request.Amount).ToString(),
            ["currency"] = request.Currency ?? "VND",
            ["operation"] = operation,
            ["order_description"] = string.IsNullOrWhiteSpace(request.Description) ? string.Empty : request.Description!,
            ["order_invoice_number"] = orderInvoiceNumber,
            ["customer_id"] = request.CustomerEmail ?? string.Empty,
            ["success_url"] = successUrl,
            ["error_url"] = errorUrl,
            ["cancel_url"] = cancelUrl,
            ["payment_method"] = "ALL"
        };

        var signature = GenerateSignature(formValues, _settings.SecretKey);
        formValues["signature"] = signature;

        var baseUrl = _settings.Environment.Equals("Sandbox", StringComparison.OrdinalIgnoreCase)
            ? "https://pay-sandbox.sepay.vn"
            : "https://pay.sepay.vn";

        var queryString = string.Join("&", formValues.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var paymentUrl = $"{baseUrl}/v1/checkout/init?{queryString}";

        _logger.LogInformation("SePay CreatePayment URL generated. PaymentUrl={PaymentUrl}", paymentUrl);
        return paymentUrl;
    }

    public async Task<string> CreatePaymentAsync(CreatePaymentRequest request, CafeSePayConfig cafeConfig, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cafeConfig.MerchantId))
        {
            throw new PaymentException("Thiếu SePay MerchantId của cafe.");
        }

        if (string.IsNullOrWhiteSpace(cafeConfig.ApiKey))
        {
            throw new PaymentException("Thiếu SePay ApiKey của cafe.");
        }

        if (string.IsNullOrWhiteSpace(cafeConfig.SecretKey))
        {
            throw new PaymentException("Thiếu SePay SecretKey của cafe.");
        }

        var formValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["api_key"] = cafeConfig.ApiKey,
            ["merchant"] = cafeConfig.MerchantId,
            ["order_amount"] = ((int)request.Amount).ToString(),
            ["currency"] = request.Currency ?? "VND",
            ["operation"] = "PURCHASE",
            ["order_description"] = string.IsNullOrWhiteSpace(request.Description) ? string.Empty : request.Description!,
            ["order_invoice_number"] = request.OrderId,
            ["customer_id"] = request.CustomerEmail ?? string.Empty,
            ["success_url"] = cafeConfig.ReturnUrl,
            ["error_url"] = cafeConfig.ReturnUrl,
            ["cancel_url"] = cafeConfig.ReturnUrl,
            ["payment_method"] = "ALL"
        };

        var signature = GenerateSignature(formValues, cafeConfig.SecretKey);
        formValues["signature"] = signature;

        // Dùng sandbox cho test, production nên dùng pay.sepay.vn
        var baseUrl = "https://pay-sandbox.sepay.vn";

        var queryString = string.Join("&", formValues.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var paymentUrl = $"{baseUrl}/v1/checkout/init?{queryString}";

        _logger.LogInformation(
            "SePay CreatePayment (Cafe) URL generated. CafeMerchantId={MerchantId}, PaymentUrl={PaymentUrl}",
            cafeConfig.MerchantId, paymentUrl);
        return paymentUrl;
    }

    public async Task<SePayTransferResponse> CreateTransferAsync(CreateTransferRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.MerchantId))
        {
            throw new PaymentException(ApiErrorMessages.Payment.SePayMerchantIdMissing);
        }

        var payload = new
        {
            merchant_id = _settings.MerchantId,
            to_bank_account = request.ToBankAccount,
            to_account_number = request.ToAccountNumber,
            amount = request.Amount,
            currency = request.Currency ?? "VND",
            description = request.Description,
            reference_id = request.ReferenceId
        };

        var client = _httpClient;
        var baseUrl = _settings.ApiBaseUrl.Trim().TrimEnd('/');
        var uri = new Uri($"{baseUrl}/v1/transfer/init");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("SePay transfer failed. Status={Status}, Body={Body}", (int)response.StatusCode, body);
            throw new PaymentException(ApiErrorMessages.Payment.SePayTransferFailed((int)response.StatusCode, body));
        }

        var transferResponse = JsonSerializer.Deserialize<SePayTransferResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new PaymentException(ApiErrorMessages.Payment.SePayResponseInvalid);

        if (!transferResponse.IsSuccess)
        {
            _logger.LogError("SePay transfer failed. Code={Code}, Message={Message}", transferResponse.Code, transferResponse.Message);
            throw new PaymentException(ApiErrorMessages.Payment.SePayTransferFailed(transferResponse.Code ?? "unknown", transferResponse.Message ?? body));
        }

        return transferResponse;
    }

    public Task<bool> VerifyWebhookAsync(string signature, string payload)
    {
        if (string.IsNullOrWhiteSpace(_settings.WebhookToken))
        {
            throw new PaymentException(ApiErrorMessages.Payment.SePayWebhookTokenMissing);
        }

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes(_settings.WebhookToken)).Trim();
        var normalizedSignature = signature?.Trim() ?? string.Empty;

        var isValid = string.Equals(expected, normalizedSignature, StringComparison.OrdinalIgnoreCase);
        if (!isValid)
        {
            _logger.LogWarning("SePay webhook signature invalid.");
        }

        return Task.FromResult(isValid);
    }

    private static string GenerateSignature(IReadOnlyDictionary<string, string> fields, string secretKey)
    {
        var allowedFields = new[]
        {
            "order_amount",
            "merchant",
            "currency",
            "operation",
            "order_description",
            "order_invoice_number",
            "customer_id",
            "payment_method",
            "success_url",
            "error_url",
            "cancel_url"
        };

        var signingParts = new List<string>();
        foreach (var field in allowedFields)
        {
            if (!fields.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            signingParts.Add($"{field}={value}");
        }

        var signingString = string.Join(",", signingParts);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingString));
        return Convert.ToBase64String(hash);
    }

}

public record CreatePaymentRequest(
    string OrderId,
    decimal Amount,
    string? CustomerEmail = null,
    string? Currency = null,
    string? Description = null,
    IReadOnlyDictionary<string, string?>? Metadata = null);

public record CreateTransferRequest(
    string ToBankAccount,
    string ToAccountNumber,
    decimal Amount,
    string? Currency = null,
    string? Description = null,
    string? ReferenceId = null);

public class SePayTransferResponse
{
    public bool IsSuccess { get; set; }
    public string? Code { get; set; }
    public string? Message { get; set; }
    public string? TransferId { get; set; }
    public string? Status { get; set; }
}

/// <summary>
/// Cấu hình SePay của từng cafe (dùng cho session payment).
/// </summary>
public record CafeSePayConfig(
    string MerchantId,
    string ApiKey,
    string SecretKey,
    string ReturnUrl);
