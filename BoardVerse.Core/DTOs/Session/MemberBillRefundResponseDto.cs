namespace BoardVerse.Core.DTOs.Session
{
    /// <summary>
    /// Response sau khi refund bill BVC thành công.
    /// M2 — docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.8.
    /// </summary>
    public class MemberBillRefundResponseDto
    {
        /// <summary>ID của MemberPaymentAuditLog đã refund.</summary>
        public Guid MemberPaymentAuditLogId { get; set; }

        /// <summary>ID member được refund.</summary>
        public Guid MemberId { get; set; }

        /// <summary>Số BVC đã refund về wallet.</summary>
        public long RefundedBvc { get; set; }

        /// <summary>Thời điểm refund.</summary>
        public DateTime RefundedAt { get; set; }

        /// <summary>Lý do refund (echo từ request).</summary>
        public string? RefundReason { get; set; }

        /// <summary>FK to <c>BvcLedgerEntry.Id</c> của refund transaction.</summary>
        public Guid RefundLedgerEntryId { get; set; }
    }
}