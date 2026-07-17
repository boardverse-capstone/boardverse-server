using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Phiên chơi tại quán (ActiveSession - Group Session).
    /// Theo boardverse-state-machine.mdc - Section 4.1.
    /// BR-12: CHECKING - khóa in hóa đơn đến khi kiểm kê xong
    /// </summary>
    public class ActiveSession
    {
        public Guid Id { get; set; }

        // === Relationships ===
        public Guid CafeId { get; set; }
        public Guid HostId { get; set; }

        /// <summary>
        /// FK CafeTable (nullable vì nhân viên POS có thể add game sau khi nhóm đã chơi
        /// mà chưa gán bàn trong lần scan đầu).
        /// </summary>
        public Guid? CafeTableId { get; set; }

        /// <summary>
        /// FK CafeInventoryBox (nullable cho cùng lý do — game có thể attach sau).
        /// BR-12 các session có nhiều game cần tra cứu theo ActiveSessionGame.CafeInventoryBoxId.
        /// </summary>
        public Guid? CafeInventoryBoxId { get; set; }
        public Guid GameTemplateId { get; set; }

        /// <summary>Lobby liên kết. Nullable vì có thể bắt đầu session trực tiếp không qua lobby.</summary>
        public Guid? LobbyId { get; set; }

        // === Game & Table ===
        public virtual ICollection<ActiveSessionGame> Games { get; set; } = [];

        // === BR-12: Component Checklist ===
        /// <summary>True khi đang kiểm kê linh kiện (CHECKING state).</summary>
        public bool IsCheckingInventory { get; set; }

        /// <summary>True khi phát hiện thiếu linh kiện - chờ xử lý.</summary>
        public bool HasMissingComponents { get; set; }

        /// <summary>BR-15: Tổng tiền phạt hao hụt linh kiện.</summary>
        public decimal PenaltyAmount { get; set; }

        /// <summary>Mã đơn hàng cho thanh toán SePay.</summary>
        public string? OrderId { get; set; }

        /// <summary>Nội dung chuyển khoản ngẫu nhiên cho thanh toán SePay/VietQR.</summary>
        public string? TransferContent { get; set; }

        /// <summary>BR-15: Tổng tiền phạt hao hụt linh kiện.</summary>

        // === State (Group Session) ===
        /// <summary>Trạng thái phiên chơi tổng. Theo MDC Section 4.1.</summary>
        public GroupSessionStatus Status { get; set; } = GroupSessionStatus.Active;

        // === Timing ===
        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        /// <summary>Tổng số phút đã chơi (tính đến thời điểm hiện tại hoặc EndedAt).</summary>
        public int TotalMinutesPlayed { get; set; }

        /// <summary>Tổng tiền giờ chơi.</summary>
        public decimal Subtotal { get; set; }

        /// <summary>Số tiền deposit đã cấn trừ.</summary>
        public decimal DepositAppliedAmount { get; set; }

        /// <summary>Tổng tiền cuối cùng (Subtotal - DepositAppliedAmount).</summary>
        public decimal TotalAmount { get; set; }

        // === Audit ===
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PaidAt { get; set; }

        // === Navigation ===
        public virtual Cafe Cafe { get; set; } = null!;
        public virtual CafeTable? CafeTable { get; set; }
        public virtual CafeInventoryBox? CafeInventoryBox { get; set; }
        public virtual GameTemplate GameTemplate { get; set; } = null!;
        public virtual User Host { get; set; } = null!;
        public virtual Lobby? Lobby { get; set; }
        public virtual ICollection<ActiveSessionMember> Members { get; set; } = [];
    }
}
