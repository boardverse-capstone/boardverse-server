using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Inventory
{
    /// <summary>
    /// Public/player view of cafe inventory — no component penalty fees.
    /// </summary>
    public class CafeInventoryBrowseDto
    {
        public Guid Id { get; set; }
        public Guid GameTemplateId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public int? BggGameId { get; set; }
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public int PlayTime { get; set; }
        public int BoxQuantity { get; set; }
        public CafeGameInventoryStatus Status { get; set; }
    }
}
