using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Pos
{
    public class ActiveSessionDto
    {
        public Guid Id { get; set; }
        public Guid HostId { get; set; }
        public string HostName { get; set; } = string.Empty;
        public Guid? LobbyId { get; set; }
        public Guid? CafeTableId { get; set; }
        public string TableName { get; set; } = string.Empty;

        /// <summary>Số bàn được staff gán khi check-in. Null nếu chưa gán.</summary>
        public int? TableNumber { get; set; }

        public int DefaultPlayTimeMinutes { get; set; }
        public DateTime StartedAt { get; set; }
        public int ElapsedMinutes { get; set; }
        public int EstimatedRemainingMinutes { get; set; }

        /// <summary>
        /// Phase 4 / EC-10 (§7.1 doc time-slot-fixed-end-design.md):
        /// True khi game có khả năng chưa kết thúc trước khi TimeSlot hết.
        /// POS UI hiển thị banner "Game có thể chưa xong trước khi hết TimeSlot.
        /// Hãy bấm Extend hoặc End sớm."
        /// </summary>
        public bool TimeOverrunWarning { get; set; }

        /// <summary>EC-10: Chi tiết warning — số phút TimeSlot còn lại.</summary>
        public int TimeSlotRemainingMinutes { get; set; }

        public GroupSessionStatus Status { get; set; }
        public decimal Subtotal { get; set; }
        public decimal DepositAppliedAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public bool IsCheckingInventory { get; set; }
        public bool HasMissingComponents { get; set; }
        public bool IsPaused { get; set; }
        public DateTime? PausedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public DateTime? PaidAt { get; set; }

        public IReadOnlyList<ActiveSessionMemberDto> Members { get; set; } = [];
        public IReadOnlyList<ActiveSessionGameDto> Games { get; set; } = [];
    }
}
