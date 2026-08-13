using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Session
{
    public class ActiveSessionResponseDto
    {
        public Guid Id { get; set; }
        public Guid CafeId { get; set; }
        public Guid HostId { get; set; }
        public Guid? CafeTableId { get; set; }
        public string TableName { get; set; } = string.Empty;

        /// <summary>Số bàn được staff gán khi check-in. Null nếu chưa gán.</summary>
        public int? TableNumber { get; set; }

        public Guid? CafeInventoryBoxId { get; set; }
        public string BoxBarcode { get; set; } = string.Empty;
        public Guid GameTemplateId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public int DefaultPlayTimeMinutes { get; set; }
        public DateTime StartedAt { get; set; }
        public int ElapsedMinutes { get; set; }
        public int EstimatedRemainingMinutes { get; set; }

        /// <summary>Phase 4 / EC-10: True khi game có thể không kết thúc trước khi TimeSlot hết.</summary>
        public bool TimeOverrunWarning { get; set; }

        /// <summary>Phase 4 / EC-10: Số phút TimeSlot còn lại (0 nếu không thuộc Reservation).</summary>
        public int TimeSlotRemainingMinutes { get; set; }

        public GroupSessionStatus Status { get; set; }
        public decimal Subtotal { get; set; }
        public decimal DepositAppliedAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public bool IsCheckingInventory { get; set; }
        public bool HasMissingComponents { get; set; }
        /// <summary>L-05: Phiên đang bị tạm dừng.</summary>
        public bool IsPaused { get; set; }
        /// <summary>L-05: Thời điểm phiên bị tạm dừng.</summary>
        public DateTime? PausedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public List<ActiveSessionMemberDto> Members { get; set; } = new();
        public List<ActiveSessionGameDto> Games { get; set; } = new();
    }
}
