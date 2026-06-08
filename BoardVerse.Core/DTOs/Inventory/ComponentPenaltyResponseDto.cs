namespace BoardVerse.Core.DTOs.Inventory
{
    public class ComponentPenaltyResponseDto
    {
        public Guid Id { get; set; }
        public Guid GameComponentTemplateId { get; set; }
        public string ComponentName { get; set; } = string.Empty;
        public int DefaultQuantity { get; set; }
        public decimal PenaltyFee { get; set; }
    }
}
