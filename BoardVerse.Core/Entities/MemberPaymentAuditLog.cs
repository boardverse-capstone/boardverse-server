using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Audit log cho thanh toán per-member bằng BVC (Case 2).
    /// Mỗi lần member (hoặc staff) thanh toán bill cá nhân bằng BVC, một row được insert
    /// vào table này kèm FK tới ledger entry (audit trail).
    /// docs/design/host-deposit-discount-and-bvc-payment-design.md §C1.3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Khác với <see cref="MemberPayment"/> (staff-recorded cash/QR audit từ 2026-08-24):
    /// </para>
    /// <list type="bullet">
    ///   <item><c>MemberPaymentAuditLog</c>: BVC payment — track AmountBvc + wallet ledger FK + idempotency key.</item>
    ///   <item><c>MemberPayment</c>: cash/QR từ POS — track StaffId + OrderId + bank transaction.</item>
    /// </list>
    /// <para>
    /// KHÔNG xóa MemberPayment cũ — backward compat với reporting cũ. Hai table chạy song song.
    /// </para>
    /// <para>
    /// Refund flow (Task C2.8): set <see cref="RefundedAt"/> + <see cref="RefundReason"/> — KHÔNG insert row mới
    /// (giữ nguyên audit chain để trace: 1 payment row, có thể có 1 refund gắn vào).
    /// </para>
    /// </remarks>
    public class MemberPaymentAuditLog
    {
        public Guid Id { get; set; }

        /// <summary>Member đã thanh toán.</summary>
        public Guid MemberId { get; set; }

        /// <summary>User thực hiện thanh toán (member.UserId hoặc staff.UserId).</summary>
        public Guid? UserId { get; set; }

        /// <summary>
        /// Phương thức thanh toán: 'Bvc' | 'Cash' | 'BvcPartial'.
        /// - 'Bvc': toàn bộ bill bằng BVC (PaidBvc).
        /// - 'Cash': bill bằng cash (replicated từ MemberPayment để consolidate report).
        /// - 'BvcPartial': split BVC + cash (PartialBvc).
        /// </summary>
        public string PaymentMethod { get; set; } = null!;

        /// <summary>Số BVC đã debit khỏi wallet (long, không decimal).</summary>
        public long AmountBvc { get; set; }

        /// <summary>Số VND cash còn lại (chỉ áp dụng cho BvcPartial mode; 0 cho full BVC/cash).</summary>
        public decimal AmountCash { get; set; }

        /// <summary>FK to <c>BvcLedgerEntry.Id</c> — audit trail với wallet ledger.</summary>
        public Guid? WalletTxnId { get; set; }

        /// <summary>
        /// BR § XVII.1: Idempotency key, UNIQUE constraint.
        /// Format: <c>bvc-pay-{SessionId}-{MemberId}-{Timestamp:o}</c>.
        /// </summary>
        public string IdempotencyKey { get; set; } = null!;

        /// <summary>Staff thực hiện payment (null nếu member tự trả qua mobile).</summary>
        public Guid? CreatedByStaffId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // === Refund tracking (Task C2.8 — Gap #11) ===

        /// <summary>Thời điểm refund (null nếu chưa refund).</summary>
        public DateTime? RefundedAt { get; set; }

        /// <summary>Lý do refund (vd: "BillSai", "DisputeResolved", "ManualAdjustment").</summary>
        public string? RefundReason { get; set; }

        /// <summary>FK to <c>BvcLedgerEntry.Id</c> của refund transaction (audit chain).</summary>
        public Guid? RefundLedgerEntryId { get; set; }

        // === Navigation ===
        public virtual ActiveSessionMember Member { get; set; } = null!;
        public virtual User? User { get; set; }
        public virtual BvcLedgerEntry? WalletTxn { get; set; }
        public virtual BvcLedgerEntry? RefundLedgerEntry { get; set; }
        public virtual User? CreatedByStaff { get; set; }
    }
}
