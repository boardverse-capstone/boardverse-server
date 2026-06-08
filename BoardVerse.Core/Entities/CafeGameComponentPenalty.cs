namespace BoardVerse.Core.Entities
{
    public class CafeGameComponentPenalty
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CafeGameInventoryId { get; set; }
        public Guid GameComponentTemplateId { get; set; }
        private decimal _penaltyFee;
        public decimal PenaltyFee
        {
            get => _penaltyFee;
            set
            {
                if (value < 0)
                    throw new ArgumentException("PenaltyFee cannot be negative");
                _penaltyFee = value;
            }
        }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public virtual CafeGameInventory CafeGameInventory { get; set; } = null!;
        public virtual GameComponentTemplate GameComponentTemplate { get; set; } = null!;
    }
}
