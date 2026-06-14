namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Live play session started when POS assigns a game box to a table.
    /// </summary>
    public class ActiveSession
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CafeId { get; set; }
        public Guid CafeTableId { get; set; }
        public Guid CafeInventoryBoxId { get; set; }
        public Guid GameTemplateId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual Cafe Cafe { get; set; } = null!;
        public virtual CafeTable CafeTable { get; set; } = null!;
        public virtual CafeInventoryBox CafeInventoryBox { get; set; } = null!;
        public virtual GameTemplate GameTemplate { get; set; } = null!;
    }
}
