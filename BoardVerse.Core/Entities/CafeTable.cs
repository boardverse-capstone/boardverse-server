using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    public class CafeTable
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CafeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public CafeTableStatus Status { get; set; } = CafeTableStatus.Available;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual Cafe Cafe { get; set; } = null!;
    }
}
