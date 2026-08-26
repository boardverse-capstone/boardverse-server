using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.Entities;

/// <summary>
/// GAP-10 + Fix #1: Audit table cho mọi SePay webhook nhận được.
/// Lưu payload + kết quả xử lý để admin query lại khi cần debug/refund.
/// Hỗ trợ cả session payment và split bill per-member payment.
/// </summary>
public class PaymentWebhookAudit
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>Endpoint webhook được gọi.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>OrderId từ webhook (BV... / BVC-... / BV-MEMBER-...).</summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>Gateway transaction ID (FT... từ SePay).</summary>
    public string? GatewayTransactionId { get; set; }

    /// <summary>Session ID nếu match được (cho session payment).</summary>
    public Guid? SessionId { get; set; }

    /// <summary>Member ID nếu match được (cho split bill payment).</summary>
    public Guid? MemberId { get; set; }

    /// <summary>Số tiền nhận từ webhook.</summary>
    public decimal Amount { get; set; }

    /// <summary>Currency (VND).</summary>
    public string Currency { get; set; } = "VND";

    /// <summary>Status gốc từ webhook (success / paid / failed / cancelled).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Kết quả xử lý: success / amount_mismatch / currency_invalid / session_not_found / session_terminal / already_paid / failed_cancelled / unknown.</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>Mô tả chi tiết (lý do fail, session status nếu terminal, ...).</summary>
    public string? Detail { get; set; }

    /// <summary>Payload gốc (JSONB) — để debug.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>IP address của gateway (nếu có).</summary>
    public string? RemoteIp { get; set; }

    /// <summary>Thời điểm nhận webhook.</summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>Service/handler xử lý webhook.</summary>
    public string ProcessedBy { get; set; } = string.Empty;

    /// <summary>Webhook có xử lý thành công không.</summary>
    public bool IsSuccess { get; set; }

    /// <summary>Thông điệp lỗi nếu không thành công.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Ghi chú bổ sung cho audit.</summary>
    public string? Notes { get; set; }
}