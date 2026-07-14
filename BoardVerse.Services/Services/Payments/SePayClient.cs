using System.Security.Cryptography;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;
using BoardVerse.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.Services.Services.Payments;

/// <summary>
/// Chỉ dùng cho transfer (settlement) và webhook verification.
/// Checkout đã chuyển hoàn toàn sang VietQR tĩnh.
/// </summary>
public interface ISePayClient
{
    /// <summary>Chuyển tiền từ tài khoản trung tâm BoardVerse sang cafe (settlement).</summary>
    Task<SePayTransferResponse> CreateTransferAsync(CreateTransferRequest request, CancellationToken cancellationToken = default);

    /// <summary>Xác minh webhook signature từ SePay.</summary>
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

        var baseUrl = _settings.ApiBaseUrl.Trim().TrimEnd('/');
        var uri = new Uri($"{baseUrl}/v1/transfer/init");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        var basicAuth = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_settings.MerchantId}:{_settings.SecretKey}"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
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

        // VietQR tĩnh: webhook đến từ SePay khi có giao dịch vào tài khoản.
        // Signature là WebhookToken đã được Base64-encode bởi SePay.
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes(_settings.WebhookToken)).Trim();
        var normalizedSignature = signature?.Trim() ?? string.Empty;

        var isValid = string.Equals(expected, normalizedSignature, StringComparison.OrdinalIgnoreCase);
        if (!isValid)
        {
            _logger.LogWarning("SePay webhook signature invalid.");
        }

        return Task.FromResult(isValid);
    }
}

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
