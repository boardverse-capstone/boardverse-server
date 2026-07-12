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

        // === Checkout ===
        /// <summary>True nếu đã thanh toán và rời nhóm (về sớm).</summary>
        public bool IsCheckedOut { get; set; }

        /// <summary>Thời điểm checkout.</summary>
        public DateTime? CheckedOutAt { get; set; }

        // === Navigation ===
        public virtual ActiveSession ActiveSession { get; set; } = null!;
        public virtual User? User { get; set; }

        // === Audit ===
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
