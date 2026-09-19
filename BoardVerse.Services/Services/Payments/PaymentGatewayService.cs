using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BoardVerse.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.Services.Services.Payments;

public interface IPaymentGatewayService
{
    Task<PaymentGatewayResult> CreatePaymentAsync(
        PaymentGatewayRequest request,
        CancellationToken cancellationToken = default);
    string BuildQrImageUrl(PaymentGatewayRequest request);
}

public class PaymentGatewayRequest
{
    public required string OrderId { get; init; }
    public required decimal Amount { get; init; }
    public string? CustomerEmail { get; init; }
    public string? Description { get; init; }
    public IReadOnlyDictionary<string, string?>? Metadata { get; init; }
    public required string BankCode { get; init; }
    public required string AccountNumber { get; init; }
    public string? AccountName { get; init; }
}

public class PaymentGatewayResult
{
    public bool IsSuccess { get; init; }
    public PaymentGateway Gateway { get; init; }
    public string? PaymentUrl { get; init; }
    public string? QrImageUrl { get; init; }
    public string? OrderId { get; init; }
    public decimal Amount { get; init; }
    public string? ErrorMessage { get; init; }
    public bool RequiresManualConfirmation { get; init; }
    public string? Message { get; init; }
}

public enum PaymentGateway { VietQr, SePay, Manual }

/// <summary>
/// Gateway thanh toán theo spec: sepay-payment-flow.mdc §VII.
///
/// Luồng:
/// 1. Thử SePay checkout API với retry exponential backoff (transient errors).
/// 2. SePay fail hoàn toàn → fallback sang VietQR static QR.
/// 3. Trả về <see cref="PaymentGatewayResult"/> với Gateway = SePay / VietQr.
///
/// Retry policy:
/// - Transient: HttpRequestException, TaskCanceledException, HTTP 502/503/504, message chứa "timeout"/"connection".
/// - Non-transient: HTTP 4xx (không retry).
/// - Exponential backoff: baseDelayMs × 2^(attempt-1).
/// </summary>
public class PaymentGatewayService : IPaymentGatewayService
{
    private readonly HttpClient _httpClient;
    private readonly IVietQrClient _vietQrClient;
    private readonly PaymentGatewaySettings _settings;
    private readonly ILogger<PaymentGatewayService> _logger;

    public PaymentGatewayService(
        HttpClient httpClient,
        IVietQrClient vietQrClient,
        IOptions<PaymentGatewaySettings> settings,
        ILogger<PaymentGatewayService> logger)
    {
        _httpClient = httpClient;
        _vietQrClient = vietQrClient;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Tạo thanh toán: thử SePay checkout → retry → VietQR fallback.
    /// </summary>
    public async Task<PaymentGatewayResult> CreatePaymentAsync(
        PaymentGatewayRequest request,
        CancellationToken cancellationToken = default)
    {
        // Phase 1: Thử SePay checkout API
        var sepayResult = await TrySePayCheckoutAsync(request, cancellationToken);

        if (sepayResult != null)
        {
            _logger.LogInformation(
                "Payment created via SePay. OrderId={OrderId}, Gateway={Gateway}, QrUrl={QrUrl}",
                request.OrderId, sepayResult.Gateway, sepayResult.QrImageUrl);
            return sepayResult;
        }

        // Phase 2: SePay fail hoàn toàn → VietQR fallback
        if (!_settings.EnableVietQrFallback)
        {
            _logger.LogError(
                "Payment gateway failed completely (SePay) and VietQR fallback is disabled. OrderId={OrderId}",
                request.OrderId);
            return new PaymentGatewayResult
            {
                IsSuccess = false,
                Gateway = PaymentGateway.Manual,
                OrderId = request.OrderId,
                Amount = request.Amount,
                RequiresManualConfirmation = true,
                ErrorMessage = "SePay không khả dụng và VietQR fallback bị tắt."
            };
        }

        _logger.LogWarning(
            "SePay checkout failed all retries, falling back to VietQR static. OrderId={OrderId}",
            request.OrderId);

        var vietQrResult = BuildVietQrFallback(request);
        _logger.LogInformation(
            "Payment fallback to VietQR. OrderId={OrderId}, Gateway=VietQr, QrUrl={QrUrl}",
            request.OrderId, vietQrResult.QrImageUrl);
        return vietQrResult;
    }

    /// <summary>
    /// Thử SePay checkout API với retry exponential backoff cho transient errors.
    /// Trả về null nếu tất cả retry fail hoặc error là non-transient.
    /// </summary>
    private async Task<PaymentGatewayResult?> TrySePayCheckoutAsync(
        PaymentGatewayRequest request,
        CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(1, _settings.SePayMaxRetries);
        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var result = await CallSePayCheckoutApiAsync(request, attempt, cancellationToken);
                if (result != null)
                {
                    return result;
                }
                // result == null có nghĩa là non-transient error (4xx), không retry
                return null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout của chính request — coi là transient, retry
                lastException = new TaskCanceledException($"SePay checkout timeout on attempt {attempt}.");
                _logger.LogWarning(
                    "SePay checkout attempt {Attempt}/{MaxRetries} timed out. OrderId={OrderId}",
                    attempt, maxRetries, request.OrderId);
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex,
                    "SePay checkout attempt {Attempt}/{MaxRetries} failed. OrderId={OrderId}",
                    attempt, maxRetries, request.OrderId);
            }
            catch (Exception ex) when (IsTransientError(ex))
            {
                lastException = ex;
                _logger.LogWarning(ex,
                    "SePay checkout attempt {Attempt}/{MaxRetries} transient error. OrderId={OrderId}",
                    attempt, maxRetries, request.OrderId);
            }

            if (attempt < maxRetries)
            {
                var delayMs = _settings.SePayRetryDelayMs * (1 << (attempt - 1)); // ×2^(attempt-1)
                _logger.LogInformation(
                    "SePay retry {Attempt}/{MaxRetries} in {DelayMs}ms. OrderId={OrderId}",
                    attempt + 1, maxRetries, delayMs, request.OrderId);
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        _logger.LogError(
            lastException,
            "SePay checkout failed all {MaxRetries} retries. OrderId={OrderId}. Falling back to VietQR.",
            maxRetries, request.OrderId);
        return null;
    }

    /// <summary>
    /// Gọi SePay checkout API. Trả về null nếu non-transient error (không retry nữa).
    /// Throw nếu transient error (retry).
    /// </summary>
    private async Task<PaymentGatewayResult?> CallSePayCheckoutApiAsync(
        PaymentGatewayRequest request,
        int attempt,
        CancellationToken cancellationToken)
    {
        // SePay checkout URL (central — dùng cho deposit payment).
        // Cafe session payment có thể dùng pgapi.sepay.vn/v1/checkout/init (tương lai).
        var baseUrl = "https://pay.sepay.vn".TrimEnd('/');
        var checkoutUrl = $"{baseUrl}/v1/checkout/init";

        // Build signing string theo field order cố định (spec §VI.1).
        // Format: order_amount=...,merchant=...,currency=...,operation=...,
        //         order_description=...,order_invoice_number=...,customer_id=...,
        //         payment_method=...,success_url=...,error_url=...,cancel_url=...
        // Bỏ qua field rỗng. Dùng giá trị mặc định cho optional fields.
        var successUrl = string.IsNullOrWhiteSpace(request.Metadata?.GetValueOrDefault("success_url"))
            ? "https://boardverse.app/payment/success"
            : request.Metadata["success_url"];
        var errorUrl = string.IsNullOrWhiteSpace(request.Metadata?.GetValueOrDefault("error_url"))
            ? "https://boardverse.app/payment/error"
            : request.Metadata["error_url"];
        var cancelUrl = string.IsNullOrWhiteSpace(request.Metadata?.GetValueOrDefault("cancel_url"))
            ? "https://boardverse.app/payment/cancel"
            : request.Metadata["cancel_url"];

        var customerId = request.Metadata?.GetValueOrDefault("userId") ?? string.Empty;

        var signingString = SePayCheckoutSignatureHelper.BuildSigningString(
            request.Amount,
            request.Metadata?.GetValueOrDefault("merchant_id") ?? string.Empty,
            request.Description ?? string.Empty,
            request.OrderId,
            customerId,
            successUrl,
            errorUrl,
            cancelUrl);

        // Sign với SecretKey từ appsettings.json → SePaySettings.SecretKey
        // Nếu không có secret key, dùng fallback VietQR ngay (SePay API không khả dụng).
        var secretKey = request.Metadata?.GetValueOrDefault("sepay_secret_key") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            _logger.LogWarning(
                "SePay SecretKey not provided in request metadata. Falling back to VietQR. OrderId={OrderId}",
                request.OrderId);
            return null;
        }

        var signature = SePayCheckoutSignatureHelper.ComputeSignature(secretKey, signingString);

        // Build query string theo SePay spec
        var queryParams = new Dictionary<string, string?>
        {
            ["order_amount"] = ((int)request.Amount).ToString(),
            ["merchant"] = request.Metadata?.GetValueOrDefault("merchant_id") ?? string.Empty,
            ["currency"] = "VND",
            ["operation"] = "payment",
            ["order_description"] = request.Description ?? string.Empty,
            ["order_invoice_number"] = request.OrderId,
            ["customer_id"] = customerId,
            ["payment_method"] = "qr_vietqr",
            ["success_url"] = successUrl,
            ["error_url"] = errorUrl,
            ["cancel_url"] = cancelUrl,
            ["signature"] = signature
        };

        // SePay central dùng GET với query params (không POST body).
        var queryString = string.Join("&",
            queryParams
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));

        var uri = new Uri($"{checkoutUrl}?{queryString}");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(_settings.SePayTimeoutMs));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cts.Token);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException
            || cts.Token.IsCancellationRequested)
        {
            // Timeout → transient, retry
            throw new TaskCanceledException($"SePay checkout timed out on attempt {attempt}.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // Non-transient: HTTP 4xx → không retry
        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
        {
            _logger.LogWarning(
                "SePay checkout returned {StatusCode} (non-transient). OrderId={OrderId}, Body={Body}",
                (int)response.StatusCode, request.OrderId, body);
            return null;
        }

        // Transient: HTTP 5xx hoặc network error → retry
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"SePay checkout HTTP {(int)response.StatusCode}: {body}",
                null,
                response.StatusCode);
        }

        // Parse response
        SePayCheckoutResponseDto? checkoutResponse;
        try
        {
            checkoutResponse = JsonSerializer.Deserialize<SePayCheckoutResponseDto>(
                body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "SePay checkout response parse failed. Body={Body}", body);
            throw new HttpRequestException($"SePay checkout response parse failed: {body}");
        }

        if (checkoutResponse == null)
        {
            _logger.LogWarning("SePay checkout response is null. Body={Body}", body);
            throw new HttpRequestException("SePay checkout response is null.");
        }

        // SePay trả về URL thanh toán
        if (!string.IsNullOrWhiteSpace(checkoutResponse.PaymentUrl))
        {
            return new PaymentGatewayResult
            {
                IsSuccess = true,
                Gateway = PaymentGateway.SePay,
                PaymentUrl = checkoutResponse.PaymentUrl,
                QrImageUrl = checkoutResponse.QrImageUrl ?? checkoutResponse.PaymentUrl,
                OrderId = request.OrderId,
                Amount = request.Amount,
                RequiresManualConfirmation = false,
                Message = checkoutResponse.Message ?? "Thanh toán qua SePay."
            };
        }

        // Payment URL trống nhưng không phải lỗi → fallback
        _logger.LogWarning(
            "SePay checkout returned empty PaymentUrl. Body={Body}. Falling back to VietQR.",
            body);
        return null;
    }

    /// <summary>
    /// VietQR static fallback — không cần API call.
    /// </summary>
    private PaymentGatewayResult BuildVietQrFallback(PaymentGatewayRequest request)
    {
        var qrUrl = _vietQrClient.GenerateQrUrl(
            request.BankCode,
            request.AccountNumber,
            request.Amount,
            description: request.Description,
            accountHolder: request.AccountName);

        return new PaymentGatewayResult
        {
            IsSuccess = true,
            Gateway = PaymentGateway.VietQr,
            PaymentUrl = qrUrl,
            QrImageUrl = qrUrl,
            OrderId = request.OrderId,
            Amount = request.Amount,
            RequiresManualConfirmation = true,
            Message = "SePay không khả dụng. Quét mã QR VietQR để thanh toán. Hệ thống tự động xác nhận khi nhận được tiền."
        };
    }

    /// <summary>
    /// Kiểm tra error có phải transient (retry được) hay không.
    /// Transient: HttpRequestException, TaskCanceledException (timeout), HTTP 5xx, message chứa "timeout"/"connection".
    /// </summary>
    private static bool IsTransientError(Exception ex)
    {
        if (ex is HttpRequestException)
            return true;

        if (ex is TaskCanceledException tce && tce.InnerException != null)
            return IsTransientError(tce.InnerException);

        var msg = ex.Message;
        return msg.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("refused", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("reset", StringComparison.OrdinalIgnoreCase);
    }

    public string BuildQrImageUrl(PaymentGatewayRequest request)
    {
        return _vietQrClient.GenerateQrUrl(
            request.BankCode,
            request.AccountNumber,
            request.Amount,
            description: request.Description,
            accountHolder: request.AccountName);
    }
}

/// <summary>
/// Response DTO từ SePay checkout API.
/// </summary>
internal class SePayCheckoutResponseDto
{
    public string? PaymentUrl { get; set; }
    public string? QrImageUrl { get; set; }
    public string? QrCode { get; set; }
    public string? TransactionId { get; set; }
    public string? Message { get; set; }
    public string? Status { get; set; }
}
