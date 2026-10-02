using System.Text.Json.Serialization;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Session
{
    /// <summary>
    /// Response sau khi member trả bill bằng BVC (Case 2).
    /// M2 — docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.7.
    /// </summary>
    public class MemberBillPaymentResponseDto
    {
        public Guid MemberId { get; set; }

        /// <summary>Số BVC đã trừ từ wallet.</summary>
        public long BvcAmount { get; set; }

        /// <summary>Số VND cash còn lại (khi PartialBvc). 0 nếu trả full BVC.</summary>
        public decimal CashRemainder { get; set; }

        /// <summary>Trạng thái thanh toán mới (PaidBvc | PartialBvc).</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MemberPaymentStatus Status { get; set; }

        /// <summary>Phương thức thanh toán: "BVC" (full) | "BVC_PARTIAL" (split).</summary>
        public string PaymentMethod { get; set; } = string.Empty;

        /// <summary>Thời điểm thanh toán.</summary>
        public DateTime PaidAt { get; set; }

        /// <summary>FK to <c>BvcLedgerEntry.Id</c> (audit trail với wallet).</summary>
        public Guid LedgerEntryId { get; set; }

        /// <summary>
        /// FK to <c>MemberPaymentAuditLog.Id</c>.
        /// Dùng cho refund flow (Pass §C2.8) — cần id này để refund.
        /// </summary>
        public Guid AuditLogId { get; set; }
    }
}