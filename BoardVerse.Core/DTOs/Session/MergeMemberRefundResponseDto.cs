namespace BoardVerse.Core.DTOs.Session;

/// <summary>
/// M1 / Option A: Response cho <c>IMergeService.HandleMemberMergeAsync</c>.
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §B3.1.
/// </summary>
public class MergeMemberRefundResponseDto
{
    /// <summary>Member ID đã xử lý.</summary>
    public Guid MemberId { get; set; }

    /// <summary>UserId của member (null nếu Guest_Slot — không refund).</summary>
    public Guid? UserId { get; set; }

    /// <summary>True nếu member có deposit và được refund thành công.</summary>
    public bool DepositRefunded { get; set; }

    /// <summary>Số BVC đã refund về wallet. 0 nếu không có deposit hoặc Guest_Slot.</summary>
    public long DepositRefundedBvc { get; set; }

    /// <summary>Ledger entry ID ghi nhận refund transaction.</summary>
    public Guid? LedgerEntryId { get; set; }

    /// <summary>Audit log row ID (MemberDepositAuditLogs).</summary>
    public Guid AuditLogId { get; set; }

    /// <summary>Action đã thực hiện: Refunded_OnMerge / GuestSlot_NoDeposit / DepositConsumed_BeforeMerge / IdempotentReplay.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Lý do / ghi chú.</summary>
    public string? Reason { get; set; }

    /// <summary>Thời điểm xử lý.</summary>
    public DateTime ProcessedAt { get; set; }
}
