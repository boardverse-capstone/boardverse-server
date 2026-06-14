using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Physical game box copy tracked by barcode for POS scan and availability.
    /// </summary>
    public class CafeInventoryBox
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CafeGameInventoryId { get; set; }
        public string Barcode { get; set; } = string.Empty;
        public CafeGameInventoryStatus Status { get; set; } = CafeGameInventoryStatus.Available;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual CafeGameInventory CafeGameInventory { get; set; } = null!;
    }
}
