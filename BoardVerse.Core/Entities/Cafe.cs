using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    public class Cafe
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required string Name { get; set; }
        public required string Address { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Description { get; set; }
        public Guid ManagerId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; } = true;

        public CafePartnerOperationalStatus? PartnerOperationalStatus { get; set; }
        public TimeSpan? WeekdayOpen { get; set; }
        public TimeSpan? WeekdayClose { get; set; }
        public TimeSpan? WeekendOpen { get; set; }
        public TimeSpan? WeekendClose { get; set; }

        public virtual User Manager { get; set; } = null!;
        public virtual ICollection<CafeStaff> StaffMembers { get; set; } = new List<CafeStaff>();
    }
}
