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
        public string? Description { get; set; }
        public int? BggGameId { get; set; }
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public int PlayTime { get; set; }
        public int BoxQuantity { get; set; }
        public CafeGameInventoryStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsActive { get; set; } = true;
        public List<ComponentPenaltyResponseDto> ComponentPenalties { get; set; } = [];
    }
}
