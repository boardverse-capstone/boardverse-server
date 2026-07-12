namespace BoardVerse.Core.DTOs.Session
{
    public class ComponentCheckoutItemDto
    {
        public Guid ComponentId { get; set; }
        public bool IsMissing { get; set; }
        public bool IsDamaged { get; set; }
        public decimal? PenaltyFee { get; set; }
    }
}
