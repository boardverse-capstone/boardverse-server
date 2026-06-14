using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Pos
{
    public class CafeInventoryBoxDto
    {
        public Guid Id { get; set; }
        public Guid CafeGameInventoryId { get; set; }
        public Guid GameTemplateId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public CafeGameInventoryStatus Status { get; set; }
    }
}
