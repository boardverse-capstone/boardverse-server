using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    public class CafePartnerApplication
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // Phase 1: Landing Page registration
        public string CafeName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string Hotline { get; set; } = string.Empty;
        public string RepresentativeEmail { get; set; } = string.Empty;
        public string BusinessLicense { get; set; } = string.Empty;
        public string? BusinessLicenseImageUrl { get; set; }
        public TimeSpan WeekdayOpen { get; set; }
        public TimeSpan WeekdayClose { get; set; }
        public TimeSpan WeekendOpen { get; set; }
        public TimeSpan WeekendClose { get; set; }

        // Phase 2: Web POS self-onboarding (filled after approval)
        public int NumberOfTables { get; set; }
        public int NumberOfPrivateRooms { get; set; }
        public string SpaceImageUrlsJson { get; set; } = "[]";
        public int NumberOfGamesOwned { get; set; }
        public string PopularGamesList { get; set; } = string.Empty;
        public bool HasGameMaster { get; set; }
        public CafePartnerBillingModel BillingModel { get; set; }
        /// <summary>JSON array of table names/positions configured on Web POS.</summary>
        public string TableLayoutJson { get; set; } = "[]";

        public CafePartnerApplicationStatus Status { get; set; } = CafePartnerApplicationStatus.PendingApproval;
        public string? RejectionReason { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public DateTime? OperationalProfileUpdatedAt { get; set; }

        public Guid? SubmittedByUserId { get; set; }
        public Guid? ReviewedByAdminId { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public Guid? CreatedManagerUserId { get; set; }
        public Guid? CreatedCafeId { get; set; }

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public virtual User? SubmittedByUser { get; set; }
        public virtual User? ReviewedByAdmin { get; set; }
        public virtual User? CreatedManager { get; set; }
        public virtual Cafe? CreatedCafe { get; set; }
    }
}
