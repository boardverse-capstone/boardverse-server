using System.Text.Json.Serialization;

namespace BoardVerse.Core.DTOs.Payment;

/// <summary>
/// Webhook payload từ SePay (BankAPINotify).
/// SePay docs dùng snake_case (transfer_type, transfer_amount, content, ...) —
/// dùng custom converter (SnakeOrCamelConverter) để chấp nhận cả snake_case
/// lẫn camelCase trong cùng 1 payload.
///
/// Field thật của SePay webhook theo
/// https://docs.sepay.vn/tao-don-sepay/webhook.html :
///   id                — ID SePay giao dịch (string)
///   gateway           — Tên ngân hàng (VD: "MBBank")
///   transactionDate   — Thời gian giao dịch
///   accountNumber     — Số tài khoản nhận (master account)
///   subAccount        — Tài khoản ảo (nếu có)
///   code              — Mã thanh toán
///   content           — Nội dung chuyển khoản (chứa OrderId dạng BV.../BVC-...)
///   transferType      — "in" hoặc "out"
///   description       — Mô tả đầy đủ từ ngân hàng
///   transferAmount    — Số tiền (VND)
///   referenceCode     — Mã tham chiếu (FT...)
///   accumulated       — Số dư lũy kế
///
/// Field legacy OrderId/Status/Amount/GatewayTransactionId được derive tự động
/// từ các field thật qua <see cref="DeriveSePayFields"/> để handler downstream
/// không phải thay đổi logic routing cũ.
/// </summary>
[JsonConverter(typeof(SnakeOrCamelConverter<SePayWebhookDto>))]
public class SePayWebhookDto
{
    // === SePay BankAPINotify fields (raw) ===

    public string? Id { get; set; }

    public string? Gateway { get; set; }

    public DateTime? TransactionDate { get; set; }

    public string? AccountNumber { get; set; }

    public string? SubAccount { get; set; }

    public string? Code { get; set; }

    /// <summary>Nội dung CK chứa OrderId của BoardVerse (VD: "...BVCTOPUP364BED39...").</summary>
    public string? Content { get; set; }

    /// <summary>Mô tả đầy đủ từ ngân hàng, mirror Content.</summary>
    public string? Description { get; set; }

    /// <summary>"in" = tiền vào, "out" = tiền ra.</summary>
    public string? TransferType { get; set; }

    /// <summary>Số tiền giao dịch (VND).</summary>
    public decimal TransferAmount { get; set; }

    public string? ReferenceCode { get; set; }

    public decimal Accumulated { get; set; }

    // === Derived fields (legacy) ===

    public string Currency { get; set; } = "VND";

    public string? Note { get; set; }

    public string? Signature { get; set; }

    public Guid? SessionId { get; set; }

    // === Backward-compat: nếu caller gửi trực tiếp OrderId/Status/Amount/GatewayTransactionId
    //     (ví dụ mock) thì ưu tiên giữ nguyên. Ngược lại sẽ derive trong Normalize(). ===

    public string OrderId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string? GatewayTransactionId { get; set; }

    /// <summary>
    /// Derive các field legacy từ payload thật của SePay (BankAPINotify).
    /// Idempotent — gọi nhiều lần cho cùng payload không thay đổi kết quả.
    ///
    /// Quy tắc:
    /// - Status: "success" nếu transferType == "in" hoặc không có; "out" → "failed".
    /// - Amount: ưu tiên TransferAmount; fallback Amount (cho mock).
    /// - OrderId: ưu tiên tự gán; nếu rỗng → extract từ Content bằng regex
    ///   (BV[A-Z0-9]{8,16} cho deposit/session, BVC-[A-Z0-9]{8,16} cho top-up).
    /// - GatewayTransactionId: ưu tiên ReferenceCode; fallback Id.
    /// - PaidAt: lấy từ TransactionDate.
    /// </summary>
    public void Normalize()
    {
        // Status từ transferType
        if (string.IsNullOrWhiteSpace(Status))
        {
            Status = string.Equals(TransferType, "out", StringComparison.OrdinalIgnoreCase)
                ? "failed"
                : "success";
        }

        // Amount
        if (Amount <= 0 && TransferAmount > 0)
        {
            Amount = TransferAmount;
        }

        // PaidAt từ TransactionDate
        if (TransactionDate.HasValue)
        {
            PaidAt = TransactionDate.Value;
        }

        // OrderId từ Content nếu chưa có
        if (string.IsNullOrWhiteSpace(OrderId) && !string.IsNullOrWhiteSpace(Content))
        {
            OrderId = ExtractOrderId(Content) ?? string.Empty;
        }

        // GatewayTransactionId từ ReferenceCode hoặc Id
        if (string.IsNullOrWhiteSpace(GatewayTransactionId))
        {
            GatewayTransactionId = !string.IsNullOrWhiteSpace(ReferenceCode)
                ? ReferenceCode
                : Id;
        }
    }

    private static string? ExtractOrderId(string content)
    {
        // Tìm pattern BVC-XXXXXXXX (top-up) hoặc BVXXXXXXXX (deposit/session)
        var match = System.Text.RegularExpressions.Regex.Match(
            content,
            @"(BVC-[A-Z0-9]{6,16}|BV[A-Z0-9]{6,16})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    // === Backward-compat để handler cũ dùng PaidAt ===

    public DateTime PaidAt { get; set; }
}
