using BoardVerse.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoardVerse.Services.Services.Payments;

/// <summary>
/// Tạo QR thanh toán qua VietQR tĩnh.
/// Mỗi QR chứa đúng số tiền và nội dung chuyển khoản động.
/// SePay webhook tự động nhận khi tiền về tài khoản (nội dung chuyển khoản chứa OrderId).
/// </summary>
public interface IPaymentGatewayService
{
    /// <summary>
    /// Tạo VietQR thanh toán.
    /// Luôn dùng VietQR tĩnh — số tiền và nội dung CK động theo request.
    /// </summary>
    Task<PaymentGatewayResult> CreatePaymentAsync(
        PaymentGatewayRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sinh VietQR image URL từ thông tin thanh toán.
    /// </summary>
    string BuildQrImageUrl(PaymentGatewayRequest request);
}

public class PaymentGatewayService : IPaymentGatewayService
{
    private readonly IVietQrClient _vietQrClient;
    private readonly ILogger<PaymentGatewayService> _logger;

    public PaymentGatewayService(
        IVietQrClient vietQrClient,
        ILogger<PaymentGatewayService> logger)
    {
        _vietQrClient = vietQrClient;
        _logger = logger;
    }

    public Task<PaymentGatewayResult> CreatePaymentAsync(
        PaymentGatewayRequest request,
        CancellationToken cancellationToken = default)
    {
        // VietQR tĩnh: luôn tạo thành công, không cần retry
        var qrUrl = _vietQrClient.GenerateQrUrl(
            request.BankCode,
            request.AccountNumber,
            request.Amount,
            description: request.Description,
            accountHolder: request.AccountName);

        _logger.LogInformation(
            "VietQR payment created. OrderId={OrderId}, Amount={Amount}, QrUrl={QrUrl}",
            request.OrderId, request.Amount, qrUrl);

        return Task.FromResult(new PaymentGatewayResult
        {
            IsSuccess = true,
            Gateway = PaymentGateway.VietQr,
            PaymentUrl = qrUrl,
            QrImageUrl = qrUrl,
            OrderId = request.OrderId,
            Amount = request.Amount,
            RequiresManualConfirmation = true,
            Message = "Quét mã QR để thanh toán. Hệ thống tự động xác nhận khi nhận được tiền."
        });
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

public class PaymentGatewayRequest
{
    public required string OrderId { get; init; }
    public required decimal Amount { get; init; }
    public string? CustomerEmail { get; init; }
    public string? Description { get; init; }
    public IReadOnlyDictionary<string, string?>? Metadata { get; init; }

    /// <summary>
    /// Mã ngân hàng (VD: MBBank, VietinBank).
    /// </summary>
    public required string BankCode { get; init; }

    /// <summary>
    /// Số tài khoản thụ hưởng.
    /// </summary>
    public required string AccountNumber { get; init; }

    /// <summary>
    /// Tên chủ tài khoản (hiển thị trên QR).
    /// </summary>
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

public enum PaymentGateway
{
    VietQr,
    SePay,
    Manual
}
