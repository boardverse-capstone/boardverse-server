using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Pos
{
    public class ComponentChecklistDto
    {
        public Guid SessionGameId { get; set; }
        public Guid GameTemplateId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public List<ComponentCheckItemDto> Components { get; set; } = [];
    }

    public class ComponentCheckItemDto
    {
        public Guid ComponentId { get; set; }
        public string ComponentName { get; set; } = string.Empty;
        public BoardGameComponentKind? ComponentKind { get; set; }
        public int ExpectedQuantity { get; set; }
        public int ActualQuantity { get; set; }
        public decimal PenaltyFee { get; set; }
    }
}
