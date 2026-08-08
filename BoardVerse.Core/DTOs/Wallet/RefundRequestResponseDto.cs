using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Wallet;

/// <summary>
/// Response chi tiết 1 refund request (dùng cho cả player lẫn admin).
/// </summary>
public class RefundRequestResponseDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Email user (chỉ populate cho admin response).</summary>
    public string? UserEmail { get; set; }

    public Guid RelatedLedgerEntryId { get; set; }

    /// <summary>Loại ledger entry liên kết (TopUp, DepositHold, ...).</summary>
    public LedgerEntryType? RelatedLedgerEntryType { get; set; }

    /// <summary>Amount của ledger entry liên kết (context cho admin).</summary>
    public long? RelatedLedgerEntryAmount { get; set; }

    public long RequestedAmountBvc { get; set; }
    public long? ApprovedAmountBvc { get; set; }
    public string PlayerReason { get; set; } = string.Empty;
    public string? AdminNote { get; set; }
    public RefundRequestStatus Status { get; set; }
    public Guid? ResolvedByAdminId { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResultLedgerEntryId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Trang danh sách refund request (phân trang).
/// </summary>
public class RefundRequestPageDto
{
    public IReadOnlyList<RefundRequestResponseDto> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalItems / PageSize);
}