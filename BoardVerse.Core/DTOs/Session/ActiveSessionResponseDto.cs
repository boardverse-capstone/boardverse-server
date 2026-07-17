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
        public Guid? CafeInventoryBoxId { get; set; }
        public string BoxBarcode { get; set; } = string.Empty;
        public Guid GameTemplateId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public int DefaultPlayTimeMinutes { get; set; }
        public DateTime StartedAt { get; set; }
        public int ElapsedMinutes { get; set; }
        public int EstimatedRemainingMinutes { get; set; }
        public GroupSessionStatus Status { get; set; }
        public decimal Subtotal { get; set; }
        public decimal DepositAppliedAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public bool IsCheckingInventory { get; set; }
        public bool HasMissingComponents { get; set; }
        public DateTime? EndedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public List<ActiveSessionMemberDto> Members { get; set; } = new();
        public List<ActiveSessionGameDto> Games { get; set; } = new();
    }
}
