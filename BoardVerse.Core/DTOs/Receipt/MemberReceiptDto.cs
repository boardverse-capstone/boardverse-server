namespace BoardVerse.Core.DTOs.Receipt;

/// <summary>
/// M2/C2.15 — Receipt cho một thành viên cụ thể trong phiên chơi đã thanh toán (Gap #32).
/// Mở rộng từ <see cref="MemberReceiptItemDto"/> với đầy đủ thông tin để player/staff in/tải về.
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.15.
/// (2026-10-01)
/// </summary>
public class MemberReceiptDto
{
    public Guid SessionId { get; set; }
    public Guid MemberId { get; set; }
    public Guid? UserId { get; set; }

    /// <summary>Tên hiển thị (Username hoặc GuestDisplayName).</summary>
    public string DisplayName { get; set; } = string.Empty;

    public bool IsHost { get; set; }
    public bool IsGuestSlot { get; set; }

    // === Session context ===
    public string CafeName { get; set; } = string.Empty;
    public string CafeAddress { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string? TableName { get; set; }

    /// <summary>Thời điểm member bắt đầu chơi (JoinedAt).</summary>
    public DateTime SessionStart { get; set; }

    /// <summary>Thời điểm member kết thúc (LeftAt ?? session.EndedAt ?? now).</summary>
    public DateTime SessionEnd { get; set; }

    public int DurationMinutes { get; set; }

    // === Bill breakdown ===
    public decimal Subtotal { get; set; }
    public decimal DepositApplied { get; set; }
    public decimal PenaltyAmount { get; set; }
    public decimal TotalAmount { get; set; }

    // === Payment info ===
    public DateTime PaidAt { get; set; }
    public string? PaymentMethod { get; set; }
    public string? PaymentStatus { get; set; }
    public string? OrderId { get; set; }
    public Guid? TransactionId { get; set; }

    // ===== M2: BVC payment tracking =====
    /// <summary>M2: Số BVC đã trừ cho bill của member này (0 nếu pay cash hoặc chưa pay).</summary>
    public long PaidBvcAmount { get; set; }

    /// <summary>M2: Số VND cash còn lại khi split BVC + cash.</summary>
    public decimal PaidCashRemainder { get; set; }

    /// <summary>M2: Thời điểm refund BVC (null nếu chưa refund).</summary>
    public DateTime? BvcRefundedAt { get; set; }

    /// <summary>M2: Lý do refund BVC.</summary>
    public string? BvcRefundReason { get; set; }

    public DateTime ReceiptGeneratedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// File receipt (bytes + content type + filename).
/// Returned by <see cref="BoardVerse.Services.IServices.IReceiptService.GenerateMemberReceiptAsync"/>.
/// </summary>
public class ReceiptFileDto
{
    public byte[] Bytes { get; set; } = [];
    public string ContentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}