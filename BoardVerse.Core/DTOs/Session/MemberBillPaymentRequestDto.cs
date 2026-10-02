using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Session
{
    /// <summary>
    /// Request cho member trả bill bằng BVC (Case 2 — Member BVC bill payment).
    /// M2 — docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.7.
    ///
    /// <para>Behavior:</para>
    /// <list type="bullet">
    ///   <item><c>BvcAmount == TotalDue</c>: PaidBvc + PaymentMethod="BVC".</item>
    ///   <item><c>0 &lt; BvcAmount &lt; TotalDue</c>: PartialBvc + PaymentMethod="BVC_PARTIAL";
    ///         staff sẽ collect <c>PaidCashRemainder</c> sau.</item>
    ///   <item><c>BvcAmount &gt; TotalDue</c>: 400 BadRequest (overpayment — Gap #6).</item>
    /// </list>
    /// </summary>
    public class MemberBillPaymentRequestDto
    {
        /// <summary>Số BVC member muốn trả cho bill của mình.</summary>
        /// <remarks>
        /// Cho phép <c>0</c> (cash-only — staff thu tiền mặt 100% qua tay, vẫn ghi audit log).
        /// Phải <c>0 &lt; BvcAmount ≤ totalDue</c> cho case có BVC (Gap #6 overpayment).
        /// </remarks>
        [Range(0, long.MaxValue, ErrorMessage = "BvcAmount phải >= 0 (cho phép 0 = cash-only).")]
        public long BvcAmount { get; set; }

        /// <summary>
        /// Idempotency key BR §XVII.1 — unique per request.
        /// Format khuyến nghị: <c>bvc-pay-{SessionId}-{MemberId}-{Timestamp:o}</c>.
        /// Nếu đã tồn tại với amount khớp → trả kết quả cũ (replay).
        /// Nếu đã tồn tại với amount khác → 409 Conflict.
        /// </summary>
        [Required(ErrorMessage = "Idempotency key là bắt buộc.")]
        [MaxLength(100, ErrorMessage = "Idempotency key không được vượt quá 100 ký tự.")]
        public string IdempotencyKey { get; set; } = string.Empty;
    }
}