using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Inventory
{
    public class CafeInventoryResponseDto
    {
        public Guid Id { get; set; }
        public Guid CafeId { get; set; }
        public Guid GameTemplateId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public int? BggGameId { get; set; }
        public int BoxQuantity { get; set; }
        public CafeGameInventoryStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<ComponentPenaltyResponseDto> ComponentPenalties { get; set; } = [];
    }
}
