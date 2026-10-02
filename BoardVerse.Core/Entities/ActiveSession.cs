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

        // === State (Group Session) ===
        /// <summary>Trạng thái phiên chơi tổng. Theo MDC Section 4.1.</summary>
        public GroupSessionStatus Status { get; set; } = GroupSessionStatus.Active;

        // === Timing ===
        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        /// <summary>L-05: Phiên đang tạm dừng (timer không đếm).</summary>
        public bool IsPaused { get; set; }

        /// <summary>L-05: Thời điểm phiên bị tạm dừng.</summary>
        public DateTime? PausedAt { get; set; }

        /// <summary>Tổng số phút đã chơi (tính đến thời điểm hiện tại hoặc EndedAt).</summary>
        public int TotalMinutesPlayed { get; set; }

        /// <summary>Tổng tiền giờ chơi.</summary>
        public decimal Subtotal { get; set; }

        /// <summary>
        /// Số tiền deposit đã cấn trừ.
        /// Mô hình mới: Host đặt cọc, tiền đi vào tài khoản BoardVerse → luôn = 0.
        /// Mỗi thành viên thanh toán 100% tiền giờ khi checkout.
        /// </summary>
        public decimal DepositAppliedAmount { get; set; }

        /// <summary>Tổng tiền cuối cùng (thường = Subtotal vì DepositAppliedAmount = 0).</summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// Tổng tiền phạt đền bù linh kiện mất/hỏng (surcharge_fine).
        /// Tính khi return-game, cập nhật vào hóa đơn phiên chơi.
        /// </summary>
        public decimal SurchargeFine { get; set; }

        // === Audit ===
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PaidAt { get; set; }

        /// <summary>Last write timestamp. Used as concurrency token for optimistic concurrency on financial updates.</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // === BR-13 (revised 2026-09-30): Walk-in audit fields ===
        /// <summary>
        /// BR-13 (revised 2026-09-30): True khi phiên được tạo bởi POS staff cho khách vãng lai
        /// (không qua Reservation/Booking). Default false (backward compat với phiên cũ).
        /// Khi true:
        ///   - <c>HostId</c> = staff.UserId (audit/SignalR, tránh NRE ở 30+ chỗ đang dùng HostId).
        ///   - <c>StartedByStaffId</c> = staff.UserId (audit field chính xác cho staff).
        ///   - <c>ActiveSessionMember</c> có thể có 1 row <c>IsHost = true</c> nếu staff chỉ định
        ///     primary customer (Option B); nếu không có → MapSession hiển thị "Khách vãng lai".
        /// MapSession check flag này để quyết định hiển thị HostName/HostId phù hợp.
        /// </summary>
        public bool IsWalkInSession { get; set; } = false;

        /// <summary>
        /// BR-13 (revised 2026-09-30): UserId của POS staff đã khởi tạo phiên walk-in.
        /// Null khi <c>IsWalkInSession = false</c> (phiên do customer online tạo qua Reservation/Booking).
        /// Khác <c>HostId</c>: HostId là field NOT NULL để tránh NRE; StartedByStaffId mới là audit field
        /// chính xác. Dùng cho PlayerActionHistory / dispute resolution.
        /// </summary>
        public Guid? StartedByStaffId { get; set; }

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
