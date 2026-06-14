using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    public class CafeGameInventory
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CafeId { get; set; }
        public Guid GameTemplateId { get; set; }
        private int _boxQuantity = 1;
        public int BoxQuantity
        {
            get => _boxQuantity;
            set
            {
                if (value < 1)
                    throw new ArgumentException("BoxQuantity must be at least 1");
                _boxQuantity = value;
            }
        }
        public CafeGameInventoryStatus Status { get; set; } = CafeGameInventoryStatus.Available;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;

        public virtual Cafe Cafe { get; set; } = null!;
        public virtual GameTemplate GameTemplate { get; set; } = null!;
        public virtual ICollection<CafeGameComponentPenalty> ComponentPenalties { get; set; } = [];
        public virtual ICollection<CafeInventoryBox> Boxes { get; set; } = [];
    }
}
