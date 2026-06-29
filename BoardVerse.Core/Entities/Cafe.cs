using BoardVerse.Core.Enum;
using NetTopologySuite.Geometries;

namespace BoardVerse.Core.Entities
{
    public class Cafe
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required string Name { get; set; }
        public required string Address { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public Point? Location { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Description { get; set; }
        public Guid ManagerId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; } = true;

        public CafePartnerOperationalStatus? PartnerOperationalStatus { get; set; }
        public string? PartnerOperationalStatusReason { get; set; }
        public DateTime? PartnerOperationalStatusChangedAt { get; set; }
        public TimeSpan? WeekdayOpen { get; set; }
        public TimeSpan? WeekdayClose { get; set; }
        public TimeSpan? WeekendOpen { get; set; }
        public TimeSpan? WeekendClose { get; set; }

        // Phase 2+ operational profile (source of truth after admin approval)
        public int NumberOfTables { get; set; }
        public int NumberOfPrivateRooms { get; set; }
        public string SpaceImageUrlsJson { get; set; } = "[]";
        public int NumberOfGamesOwned { get; set; }
        public string PopularGamesList { get; set; } = string.Empty;
        public bool HasGameMaster { get; set; }
        public CafePartnerBillingModel BillingModel { get; set; } = CafePartnerBillingModel.ByHour;
        /// <summary>JSON array of table names configured on Web POS.</summary>
        public string TableLayoutJson { get; set; } = "[]";
        public DateTime? OperationalProfileUpdatedAt { get; set; }

        public virtual User Manager { get; set; } = null!;
        public virtual CafePartnerApplication? PartnerApplication { get; set; }
        public virtual ICollection<CafeStaff> StaffMembers { get; set; } = new List<CafeStaff>();
        public virtual ICollection<CafeTable> Tables { get; set; } = new List<CafeTable>();
        public virtual ICollection<CafeGameInventory> Inventories { get; set; } = new List<CafeGameInventory>();
    }
}
