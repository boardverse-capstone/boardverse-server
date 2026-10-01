using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Bản ghi nợ của member khi session bị force-close (M2/C2.16 — Gap #33).
    /// <para>
    /// Được tạo khi Manager chọn <c>UnpaidMemberHandling = "MarkAsDebt"</c>:
    /// member không thanh toán, nhưng host muốn ghi nhận công nợ thay vì phạt NoShow.
    /// </para>
    /// <para>
    /// Member sau đó có thể trả nợ cash tại quán (set <c>Status = Resolved</c>) hoặc
    /// Manager quyết định write-off (set <c>Status = WrittenOff</c>).
    /// </para>
    /// <para>
    /// KHÔNG ghi vào BvcLedgerEntry — Debt amount là VND cash (đã chốt bill),
    /// không phải BVC. Member không có wallet BVC trừ cho khoản nợ này.
    /// </para>
    /// (2026-10-01)
    /// </summary>
    public class MemberDebt
    {
        public Guid Id { get; set; }

        /// <summary>Member bị ghi nợ (FK to <see cref="ActiveSessionMember"/>).</summary>
        public Guid MemberId { get; set; }

        /// <summary>User liên quan (snapshot — Guest_Slot có thể null).</summary>
        public Guid? UserId { get; set; }

        /// <summary>Session chứa member bị ghi nợ (FK to <see cref="ActiveSession"/>).</summary>
        public Guid SessionId { get; set; }

        /// <summary>
        /// Số BVC legacy — không dùng trong M2/C2.16 (Debt là VND cash, không phải BVC).
        /// Giữ lại cho trường hợp Debt có thể kết hợp BVC sau này. Default 0.
        /// </summary>
        public long AmountBvc { get; set; }

        /// <summary>Số VND cash nợ.</summary>
        public decimal AmountCash { get; set; }

        /// <summary>Trạng thái xử lý nợ.</summary>
        public DebtStatus Status { get; set; } = DebtStatus.Pending;

        /// <summary>Lý do tạo nợ (vd: "ForceClose_MarkAsDebt", "Bill_Disputed"). Max 500 ký tự.</summary>
        public string Reason { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Thời điểm resolve / write-off. Null nếu còn <see cref="DebtStatus.Pending"/>.</summary>
        public DateTime? ResolvedAt { get; set; }

        /// <summary>UserId của staff/manager đã resolve debt. Null nếu chưa resolve.</summary>
        public Guid? ResolvedByUserId { get; set; }

        // === Navigation ===
        public virtual ActiveSessionMember Member { get; set; } = null!;
        public virtual ActiveSession Session { get; set; } = null!;
        public virtual User? User { get; set; }
    }
}