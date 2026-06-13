using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.CafePartner
{
    public class CafePartnerApplicationResponseDto
    {
        public Guid Id { get; set; }
        public string CafeName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Hotline { get; set; } = string.Empty;
        public string RepresentativeEmail { get; set; } = string.Empty;
        public WorkingHoursDto WorkingHours { get; set; } = new();
        public string BusinessLicense { get; set; } = string.Empty;
        public string? BusinessLicenseImageUrl { get; set; }

        public int NumberOfTables { get; set; }
        public int NumberOfPrivateRooms { get; set; }
        public List<string> SpaceImageUrls { get; set; } = new();
        public int NumberOfGamesOwned { get; set; }
        public string PopularGamesList { get; set; } = string.Empty;
        public bool HasGameMaster { get; set; }
        public string BillingModel { get; set; } = string.Empty;
        public List<string> TableNames { get; set; } = new();

        public string ApplicationStatus { get; set; } = string.Empty;
        public string? OperationalStatus { get; set; }
        public string? RejectionReason { get; set; }
        public bool RequiresCsSupport { get; set; }
        public bool IsTableLayoutConfigured { get; set; }
        public bool CanActivate { get; set; }
        public List<string> ActivationBlockers { get; set; } = new();

        public Guid? SubmittedByUserId { get; set; }
        public string? SubmittedByUsername { get; set; }
        public Guid? ReviewedByAdminId { get; set; }
        public string? ReviewedByAdminUsername { get; set; }
        public Guid? CreatedManagerUserId { get; set; }
        public Guid? CreatedCafeId { get; set; }
        public DateTime SubmittedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public DateTime? OperationalProfileUpdatedAt { get; set; }
    }
}
