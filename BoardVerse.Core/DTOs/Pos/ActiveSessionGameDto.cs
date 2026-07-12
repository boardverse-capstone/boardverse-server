using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Pos
{
    public class ActiveSessionGameDto
    {
        public Guid Id { get; set; }
        public Guid CafeInventoryBoxId { get; set; }
        public string BoxBarcode { get; set; } = string.Empty;
        public Guid GameTemplateId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public DateTime AttachedAt { get; set; }
        public ComponentCheckStatus CheckStatus { get; set; }
        public decimal TotalPenaltyAmount { get; set; }
    }
}
