using System.Security.Cryptography;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services.Payments;

/// <summary>
/// Input cho webhook verification.
/// Hỗ trợ 3 mode theo <see cref="SePayWebhookAuthType"/>:
/// - None: dev/test only, luôn trả về true.
/// - ApiKey: so sánh trực tiếp <see cref="Signature"/> với WebhookToken (Authorization header).
/// - HmacSha256: reconstruct sha256=HMAC(secret, "{Timestamp}.{RawBody}") và so sánh
///   với <see cref="Signature"/> qua constant-time comparison.
/// </summary>
public sealed record SePayWebhookVerificationRequest(
    string? Signature,
    string? Timestamp,
    string RawBody);

/// <summary>
/// SePay client đọc credentials từ Database (SePayAccount Master).
/// Không dùng appsettings.json cho credentials.
/// </summary>
public interface ISePayClient
{
    /// <summary>Chuyển tiền từ tài khoản trung tâm BoardVerse sang cafe (settlement).</summary>
    Task<SePayTransferResponse> CreateTransferAsync(CreateTransferRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xác minh webhook signature từ SePay. Backward-compat: luôn dùng Master account.
    /// </summary>
    Task<bool> VerifyWebhookAsync(SePayWebhookVerificationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xác minh webhook với routing per-cafe (SePay Personal):
    /// - Nếu gateway+accountNumber khớp 1 cafe SePay account đang cấu hình auth
    ///   (WebhookAuthType != None) → verify bằng credentials của cafe đó.
    /// - Ngược lại (không match hoặc cafe dùng None) → fallback về Master company.
    /// </summary>
    Task<bool> VerifyWebhookAsync(
        SePayWebhookVerificationRequest request,
        string? gateway,
        string? accountNumber,
        CancellationToken cancellationToken = default);
}

public class SePayClient : ISePayClient
{
    private readonly ISePayAccountRepository _sepayAccountRepository;
    private readonly HttpClient _httpClient;
    private readonly ILogger<SePayClient> _logger;

    public SePayClient(
        ISePayAccountRepository sepayAccountRepository,
        HttpClient httpClient,
        ILogger<SePayClient> logger)
    {
        _sepayAccountRepository = sepayAccountRepository;
        _httpClient = httpClient;
        _logger = logger;
    }

    private async Task<SePayAccount> GetMasterAccountAsync(CancellationToken cancellationToken = default)
    {
        var account = await _sepayAccountRepository.GetMasterAccountAsync(cancellationToken);
        if (account == null)
        {
            throw new PaymentException(ApiErrorMessages.Payment.SePayMasterAccountNotFound);
        }
        if (!account.IsActive)
        {
            throw new PaymentException(ApiErrorMessages.Payment.SePayMasterAccountInactive);
        }
        return account;
    }

    public async Task<SePayTransferResponse> CreateTransferAsync(CreateTransferRequest request, CancellationToken cancellationToken = default)
    {
        var masterAccount = await GetMasterAccountAsync();

        if (string.IsNullOrWhiteSpace(masterAccount.MerchantId))
        {
            throw new PaymentException(ApiErrorMessages.Payment.SePayMerchantIdMissing);
        }

        var payload = new
        {
            merchant_id = masterAccount.MerchantId,
            to_bank_account = request.ToBankAccount,
            to_account_number = request.ToAccountNumber,
            amount = request.Amount,
            currency = request.Currency ?? "VND",
            description = request.Description,
            reference_id = request.ReferenceId
        };

        var baseUrl = (masterAccount.ApiBaseUrl ?? "https://pgapi.sepay.vn").Trim().TrimEnd('/');
        var uri = new Uri($"{baseUrl}/v1/transfer/init");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        var secretKey = masterAccount.SecretKey ?? string.Empty;
        var basicAuth = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{masterAccount.MerchantId}:{secretKey}"));
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

    public async Task<bool> VerifyWebhookAsync(SePayWebhookVerificationRequest request, CancellationToken cancellationToken = default)
    {
        // Backward-compat: legacy callers (chỉ truyền request) luôn dùng Master account.
        var masterAccount = await GetMasterAccountAsync(cancellationToken);
        return await VerifyInternalAsync(masterAccount, request, accountLabel: "Master");
    }

    public async Task<bool> VerifyWebhookAsync(
        SePayWebhookVerificationRequest request,
        string? gateway,
        string? accountNumber,
        CancellationToken cancellationToken = default)
    {
        // 1. Nếu có gateway + accountNumber, tìm cafe account match
        if (!string.IsNullOrWhiteSpace(gateway) && !string.IsNullOrWhiteSpace(accountNumber))
        {
            var cafeAccount = await _sepayAccountRepository.GetCafeAccountByBankInfoAsync(
                gateway, accountNumber, cancellationToken);

            if (cafeAccount != null
                && cafeAccount.WebhookAuthType != SePayWebhookAuthType.None
                && !string.IsNullOrEmpty(cafeAccount.SecretKey ?? cafeAccount.WebhookToken))
            {
                _logger.LogDebug(
                    "SePay webhook routed to cafe account. CafeId={CafeId}, AccountId={AccountId}, AuthType={AuthType}",
                    cafeAccount.CafeId, cafeAccount.Id, cafeAccount.WebhookAuthType);

                return await VerifyInternalAsync(
                    cafeAccount, request, accountLabel: $"Cafe:{cafeAccount.CafeId}");
            }

            if (cafeAccount != null)
            {
                _logger.LogDebug(
                    "SePay webhook: cafe {CafeId} matched bank info but WebhookAuthType=None — falling back to Master.",
                    cafeAccount.CafeId);
            }
        }

        // 2. Fallback về Master account (SePay Company mode cũ)
        var masterAccount = await GetMasterAccountAsync(cancellationToken);
        return await VerifyInternalAsync(masterAccount, request, accountLabel: "Master");
    }

    private async Task<bool> VerifyInternalAsync(
        SePayAccount account,
        SePayWebhookVerificationRequest request,
        string accountLabel)
    {
        // Mode None: dev/test only — luôn pass (trừ production).
        if (account.WebhookAuthType == SePayWebhookAuthType.None)
        {
            if (!IsProductionLikeEnvironment())
            {
                _logger.LogWarning(
                    "SePay webhook verification SKIPPED ({Label}, WebhookAuthType=None). Chỉ dùng cho dev/test. Production BẮT BUỘC set ApiKey hoặc HmacSha256.",
                    accountLabel);
                return true;
            }

            _logger.LogError(
                "SePay webhook rejected in production ({Label}, WebhookAuthType=None). Set WebhookAuthType=ApiKey hoặc HmacSha256.",
                accountLabel);
            return false;
        }

        return account.WebhookAuthType switch
        {
            SePayWebhookAuthType.ApiKey => VerifyApiKey(account, request, accountLabel),
            SePayWebhookAuthType.HmacSha256 => VerifyHmacSha256(account, request, accountLabel),
            _ => false
        };
    }

    /// <summary>
    /// API Key mode: so sánh trực tiếp header <c>Authorization: Apikey &lt;WebhookToken&gt;</c>
    /// (SePay gửi nguyên WebhookToken, KHÔNG qua Base64).
    /// Caller truyền <c>request.Signature</c> = phần sau "Apikey ".
    /// </summary>
    private bool VerifyApiKey(SePayAccount account, SePayWebhookVerificationRequest request, string accountLabel = "Master")
    {
        if (string.IsNullOrWhiteSpace(account.WebhookToken))
        {
            _logger.LogWarning("SePay webhook (ApiKey mode, {Label}) rejected: WebhookToken is empty.", accountLabel);
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Signature))
        {
            _logger.LogWarning("SePay webhook (ApiKey mode, {Label}) rejected: signature header missing.", accountLabel);
            return false;
        }

        var provided = request.Signature.Trim();
        var expected = account.WebhookToken.Trim();

        // Constant-time comparison để chống timing attack.
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var isValid = CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);

        if (!isValid)
        {
            _logger.LogWarning("SePay webhook (ApiKey mode, {Label}) signature mismatch.", accountLabel);
        }

        return isValid;
    }

    /// <summary>
    /// HMAC-SHA256 mode (SePay khuyến nghị):
    ///   1. Header <c>X-SePay-Timestamp</c> phải có và trong khoảng ±300s của server time.
    ///   2. Reconstruct <c>expected = "sha256=" + HMAC-SHA256(SecretKey, "{timestamp}.{rawBody}")</c>.
    ///   3. So sánh với header <c>X-SePay-Signature</c> qua constant-time.
    /// </summary>
    private bool VerifyHmacSha256(SePayAccount account, SePayWebhookVerificationRequest request, string accountLabel = "Master")
    {
        if (string.IsNullOrWhiteSpace(account.SecretKey))
        {
            _logger.LogWarning("SePay webhook (HMAC-SHA256 mode, {Label}) rejected: SecretKey is empty.", accountLabel);
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Signature))
        {
            _logger.LogWarning("SePay webhook (HMAC-SHA256 mode, {Label}) rejected: X-SePay-Signature header missing.", accountLabel);
            return false;
        }

        // Timestamp phải lấy từ header (X-SePay-Timestamp). SePay không đặt timestamp
        // trong body JSON — fallback TimestampFromBody đã bị xoá vì không có callsite dùng.
        var timestampValue = request.Timestamp;

        if (string.IsNullOrWhiteSpace(timestampValue) || !long.TryParse(timestampValue, out var unixSeconds))
        {
            _logger.LogWarning("SePay webhook (HMAC-SHA256 mode, {Label}) rejected: X-SePay-Timestamp header missing or invalid.", accountLabel);
            return false;
        }

        // Anti-replay: timestamp phải trong ±300s.
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(nowUnix - unixSeconds) > 300)
        {
            _logger.LogWarning(
                "SePay webhook (HMAC-SHA256 mode, {Label}) rejected: timestamp skew too large. Now={Now}, Provided={Provided}",
                accountLabel, nowUnix, unixSeconds);
            return false;
        }

        // Reconstruct signature.
        var messageBytes = Encoding.UTF8.GetBytes($"{unixSeconds}.{request.RawBody}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(account.SecretKey));
        var computedHash = hmac.ComputeHash(messageBytes);
        var expectedSignature = "sha256=" + Convert.ToHexString(computedHash).ToLowerInvariant();

        var providedSignature = request.Signature.Trim();

        // Constant-time comparison.
        var providedBytes = Encoding.UTF8.GetBytes(providedSignature);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);
        var isValid = providedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);

        if (!isValid)
        {
            _logger.LogWarning("SePay webhook (HMAC-SHA256 mode, {Label}) signature mismatch.", accountLabel);
        }

        return isValid;
    }

    /// <summary>
    /// True khi chạy trong Production/Staging. False khi Development.
    /// </summary>
    private bool IsProductionLikeEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase)
            || string.Equals(env, "Staging", StringComparison.OrdinalIgnoreCase);
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
