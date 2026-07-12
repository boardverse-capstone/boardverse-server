using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Phòng chờ trực tuyến (Lobby).
    /// Theo boardverse-state-machine.mdc - Section 2.
    /// BR-07: Lobby.MaxMembers <= SeatCount (tự quản lý)
    /// BR-08: Auto-hủy nếu trước giờ hẹn X phút mà chưa đạt MinPlayers
    /// BR-10: Filter theo Karma (không dùng Elo)
    /// </summary>
    public class Lobby
    {
        public Guid Id { get; set; }

        // === Game & Host ===
        public Guid HostUserId { get; set; }
        public Guid GameTemplateId { get; set; }

        // === Scheduling (BR-08) ===
        /// <summary>Thời điểm dự kiến bắt đầu chơi tại quán.</summary>
        public DateTime? ScheduledStartTime { get; set; }

        /// <summary>Latitude của quán mục tiêu (từ Cafe) - dùng để tìm phòng chờ gần user.</summary>
        public double? Latitude { get; set; }

        /// <summary>Longitude của quán mục tiêu (từ Cafe) - dùng để tìm phòng chờ gần user.</summary>
        public double? Longitude { get; set; }

        /// <summary>Phút trước giờ hẹn để trigger auto-hủy nếu chưa đủ người. (BR-08)</summary>
        public int CancellationLeadTimeMinutes { get; set; } = 30;

        // === Capacity (BR-07) ===
        /// <summary>Số người tối đa trong phòng chờ.</summary>
        public int MaxMembers { get; set; }

        /// <summary>
        /// Số ghế tối đa mà lobby này cần.
        /// BR-07: Members.Count <= SeatCount khi có giá trị.
        /// </summary>
        public int? SeatCount { get; set; }

        // === Session link ===
        public Guid? ActiveSessionId { get; set; }

        // === State ===
        public LobbyStatus Status { get; set; } = LobbyStatus.Open;

        /// <summary>Thời điểm mở màn hình đánh giá Karma.</summary>
        public DateTime? RatingOpenedAt { get; set; }

        // === Audit ===
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // === Navigation ===
        public virtual User HostUser { get; set; } = null!;
        public virtual GameTemplate GameTemplate { get; set; } = null!;
        public virtual ActiveSession? ActiveSession { get; set; }
        public virtual ICollection<LobbyMember> Members { get; set; } = [];
    }
}
