using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Thành viên trong phiên chơi tại quán (ActiveSession - Individual Session).
    /// Theo boardverse-state-machine.mdc - Section 4.2.
    /// BR-12: Kiểm kê khi về sớm
    /// BR-13: Guest_Slot không chịu trách nhiệm tài sản độc lập
    /// BR-14: Phí phạt không gán vào Guest_Slot
    /// </summary>
    public class ActiveSessionMember
    {
        public Guid Id { get; set; }

        // === Relationships ===
        public Guid ActiveSessionId { get; set; }

        /// <summary>Nếu là khách vô danh (BR-13), UserId = null.</summary>
        public Guid? UserId { get; set; }
        public bool IsGuestSlot { get; set; }

        /// <summary>Tên hiển thị cho Guest_Slot.</summary>
        public string? GuestDisplayName { get; set; }

        /// <summary>
        /// Số điện thoại Guest_Slot (optional, dùng để liên hệ khi cần).
        /// Validate format VN (10-11 chữ số, đầu 03/05/07/08/09) ở service layer.
        /// </summary>
        public string? GuestPhoneNumber { get; set; }

        // === Session Link (BR-14: Tách/ghép nhóm) ===
        /// <summary>Session gốc khi member tách nhóm. Dùng để track thời gian liên tục.</summary>
        public Guid? OriginalSessionId { get; set; }

        // === Individual Session State ===
        /// <summary>Trạng thái phiên cá nhân.</summary>
        public IndividualSessionStatus Status { get; set; } = IndividualSessionStatus.Playing;

        // === Timing (Individual) ===
        /// <summary>Thời điểm bắt đầu chơi (có thể khác StartedAt của session gốc khi ghép nhóm).</summary>
        public DateTime JoinedAt { get; set; }

        /// <summary>Thời điểm kết thúc phiên cá nhân.</summary>
        public DateTime? LeftAt { get; set; }

        /// <summary>Tổng phút chơi của cá nhân này.</summary>
        public int TotalMinutesPlayed { get; set; }

        // === Penalty (BR-14) ===
        /// <summary>Phí phạt thiếu linh kiện. KHÔNG gán vào Guest_Slot. (BR-14)</summary>
        public decimal PenaltyAmount { get; set; }

        /// <summary>Lý do phạt.</summary>
        public string? PenaltyReason { get; set; }

        /// <summary>Đã thanh toán phí phạt chưa.</summary>
        public bool IsPenaltyPaid { get; set; }

        // === Host role (BR-12, BR-22) ===
        /// <summary>
        /// True nếu thành viên này là host của phiên chơi.
        /// Host chịu trách nhiệm tổng quát phiên + là người đặt cọc BVC.
        /// Mỗi ActiveSession chỉ có duy nhất 1 host member.
        /// </summary>
        public bool IsHost { get; set; }

        // === Checkout ===
        /// <summary>True nếu đã thanh toán và rời nhóm (về sớm).</summary>
        public bool IsCheckedOut { get; set; }

        /// <summary>Thời điểm checkout.</summary>
        public DateTime? CheckedOutAt { get; set; }

        // === Deposit (BR-15, BR-22) ===
        /// <summary>
        /// GAP-10 Fix: Số tiền deposit đã áp dụng cho thành viên này khi thanh toán.
        /// Mỗi thành viên có deposit riêng nếu có booking.
        /// </summary>
        public decimal DepositAppliedAmount { get; set; }

        /// <summary>
        /// GAP-10 Fix: ID của deposit đã áp dụng cho thành viên này.
        /// </summary>
        public Guid? DepositId { get; set; }

        // === Billing (BR-15: hóa đơn cá nhân) ===
        /// <summary>
        /// Tiền giờ chơi cá nhân (subtotal trước penalty, trước khi áp deposit).
        /// Tính theo công thức: tổng phút chơi cá nhân × đơn giá áp dụng.
        /// </summary>
        public decimal Subtotal { get; set; }

        /// <summary>
        /// Tổng hóa đơn cuối cùng phải thanh toán.
        /// Công thức: Subtotal + PenaltyAmount - DepositAppliedAmount.
        /// </summary>
        public decimal TotalAmount { get; set; }

        // === Navigation ===
        public virtual ActiveSession ActiveSession { get; set; } = null!;
        public virtual User? User { get; set; }

        // === Audit ===
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Last write timestamp. Used as concurrency token for optimistic concurrency on penalty/financial updates.</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
