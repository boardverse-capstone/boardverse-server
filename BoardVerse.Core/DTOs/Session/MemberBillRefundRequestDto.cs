using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Session
{
    /// <summary>
    /// Request refund BVC bill payment (Case 2 — Gap #11 bill sai / dispute).
    /// M2 — docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.8.
    /// </summary>
    public class MemberBillRefundRequestDto
    {
        /// <summary>Lý do refund (audit trail).</summary>
        [Required(ErrorMessage = "Lý do refund là bắt buộc.")]
        [MaxLength(500, ErrorMessage = "Lý do refund không được vượt quá 500 ký tự.")]
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Idempotency key BR §XVII.1.
        /// Format khuyến nghị: <c>refund-bvc-{AuditLogId}-{Timestamp:o}</c>.
        /// </summary>
        [Required(ErrorMessage = "Idempotency key là bắt buộc.")]
        [MaxLength(100, ErrorMessage = "Idempotency key không được vượt quá 100 ký tự.")]
        public string IdempotencyKey { get; set; } = string.Empty;
    }
}