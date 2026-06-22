using System;

namespace BoardVerse.Core.Entities
{
    public class CafeStaff
    {
        public Guid CafeId { get; set; }
        public Guid UserId { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual Cafe Cafe { get; set; } = null!;
        public virtual User User { get; set; } = null!;
    }
}
