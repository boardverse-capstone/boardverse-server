using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.Services.Services.Payments;

public interface IPaymentGatewayService
{
    /// <summary>
    /// Tạo payment với fallback chain: SePay -> VietQR
    /// Retry với exponential backoff khi SePay lỗi tạm thời.
    /// </summary>
    Task<PaymentGatewayResult> CreatePaymentAsync(
        PaymentGatewayRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fallback sang VietQR khi SePay không hoạt động.
    /// </summary>
    Task<PaymentGatewayResult> CreateVietQrFallbackAsync(
        PaymentGatewayRequest request,
        CancellationToken cancellationToken = default);
}

public class PaymentGatewayService : IPaymentGatewayService
{
    private readonly ISePayClient _sePayClient;
    private readonly IVietQrClient _vietQrClient;
    private readonly PaymentGatewaySettings _settings;
    private readonly ILogger<PaymentGatewayService> _logger;

    public PaymentGatewayService(
        ISePayClient sePayClient,
        IVietQrClient vietQrClient,
        IOptions<PaymentGatewaySettings> settings,
        ILogger<PaymentGatewayService> logger)
    {
        _sePayClient = sePayClient;
        _vietQrClient = vietQrClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PaymentGatewayResult> CreatePaymentAsync(
        PaymentGatewayRequest request,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Thử SePay với retry
        var sePayResult = await TrySePayWithRetryAsync(request, cancellationToken);

        if (sePayResult.IsSuccess)
        {
            _logger.LogInformation(
                "SePay payment created successfully. OrderId={OrderId}, Gateway=SePay",
                request.OrderId);

            return sePayResult;
        }

        // Step 2: SePay thất bại, fallback sang VietQR
        _logger.LogWarning(
            "SePay failed after {RetryCount} retries. Falling back to VietQR. OrderId={OrderId}, Error={Error}",
            sePayResult.RetryCount, request.OrderId, sePayResult.ErrorMessage);

        var vietQrResult = await CreateVietQrFallbackAsync(request, cancellationToken);
        return vietQrResult;
    }

    private async Task<PaymentGatewayResult> TrySePayWithRetryAsync(
        PaymentGatewayRequest request,
        CancellationToken cancellationToken)
    {
        var retryCount = 0;
        var maxRetries = _settings.SePayMaxRetries;
        var baseDelayMs = _settings.SePayRetryDelayMs;

        while (retryCount <= maxRetries)
        {
            try
            {
                var paymentUrl = await _sePayClient.CreatePaymentAsync(
                    new CreatePaymentRequest(
                        OrderId: request.OrderId,
                        Amount: request.Amount,
                        CustomerEmail: request.CustomerEmail,
                        Description: request.Description,
                        Metadata: request.Metadata),
                    cancellationToken);

                return new PaymentGatewayResult
                {
                    IsSuccess = true,
                    Gateway = PaymentGateway.SePay,
                    PaymentUrl = paymentUrl,
                    QrImageUrl = paymentUrl,
                    OrderId = request.OrderId,
                    Amount = request.Amount,
                    RetryCount = retryCount
                };
            }
            catch (Exception ex) when (IsTransientError(ex))
            {
                retryCount++;

                if (retryCount > maxRetries)
                {
                    _logger.LogError(ex,
                        "SePay transient error after {RetryCount} retries. OrderId={OrderId}",
                        retryCount, request.OrderId);

                    return new PaymentGatewayResult
                    {
                        IsSuccess = false,
                        Gateway = PaymentGateway.SePay,
                        ErrorMessage = ex.Message,
                        RetryCount = retryCount,
                        ShouldFallback = true
                    };
                }

                // Exponential backoff
                var delayMs = (int)(baseDelayMs * Math.Pow(2, retryCount - 1));
                _logger.LogWarning(
                    "SePay transient error, retrying in {DelayMs}ms. Attempt={Attempt}/{MaxRetries}, OrderId={OrderId}",
                    delayMs, retryCount, maxRetries, request.OrderId);

                await Task.Delay(delayMs, cancellationToken);
            }
            catch (Exception ex)
            {
                // Non-transient error, fallback immediately
                _logger.LogError(ex,
                    "SePay non-transient error. Falling back to VietQR. OrderId={OrderId}",
                    request.OrderId);

                return new PaymentGatewayResult
                {
                    IsSuccess = false,
                    Gateway = PaymentGateway.SePay,
                    ErrorMessage = ex.Message,
                    RetryCount = retryCount,
                    ShouldFallback = true
                };
            }
        }

        return new PaymentGatewayResult
        {
            IsSuccess = false,
            Gateway = PaymentGateway.SePay,
            ErrorMessage = "Max retries exceeded",
            RetryCount = retryCount,
            ShouldFallback = true
        };
    }

    public async Task<PaymentGatewayResult> CreateVietQrFallbackAsync(
        PaymentGatewayRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var qrUrl = _vietQrClient.GenerateQrUrl(
                request.BankCode,
                request.AccountNumber,
                request.Amount,
                request.AccountName);

            // VietQR không cần webhook, mark là pending confirmation
            var result = new PaymentGatewayResult
            {
                IsSuccess = true,
                Gateway = PaymentGateway.VietQr,
                PaymentUrl = qrUrl,
                QrImageUrl = qrUrl,
                OrderId = request.OrderId,
                Amount = request.Amount,
                RequiresManualConfirmation = true,
                Message = "VietQR fallback - vui lòng xác nhận thanh toán thủ công nếu cần"
            };

            _logger.LogInformation(
                "VietQR fallback created. OrderId={OrderId}, QrUrl={QrUrl}",
                request.OrderId, qrUrl);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "VietQR fallback also failed. No payment gateway available. OrderId={OrderId}",
                request.OrderId);

            return new PaymentGatewayResult
            {
                IsSuccess = false,
                Gateway = PaymentGateway.VietQr,
                ErrorMessage = $"All payment gateways failed. SePay: unavailable, VietQR: {ex.Message}",
                RequiresManualConfirmation = true
            };
        }
    }

    private static bool IsTransientError(Exception ex)
    {
        // Network errors, timeout, 5xx errors are transient
        return ex is HttpRequestException
            || ex is TaskCanceledException
            || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("503", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("502", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("504", StringComparison.OrdinalIgnoreCase);
    }
}

public class PaymentGatewayRequest
{
    public required string OrderId { get; init; }
    public required decimal Amount { get; init; }
    public string? CustomerEmail { get; init; }
    public string? Description { get; init; }
    public IReadOnlyDictionary<string, string?>? Metadata { get; init; }

    // VietQR params
    public string BankCode { get; init; } = "MBBank";
    public string AccountNumber { get; init; } = "0000000001";
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
    public int RetryCount { get; init; }
    public bool ShouldFallback { get; init; }
    public bool RequiresManualConfirmation { get; init; }
    public string? Message { get; init; }
}

public enum PaymentGateway
{
    SePay,
    VietQr,
    Manual
}

public class PaymentGatewaySettings
{
    public const string SectionName = "PaymentGateway";

    /// <summary>
    /// Số lần retry tối đa khi SePay lỗi tạm thời.
    /// </summary>
    public int SePayMaxRetries { get; set; } = 3;

    /// <summary>
    /// Độ trễ ban đầu (ms) trước khi retry. Dùng exponential backoff.
    /// </summary>
    public int SePayRetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Bật fallback sang VietQR khi SePay lỗi.
    /// </summary>
    public bool EnableVietQrFallback { get; set; } = true;
}
