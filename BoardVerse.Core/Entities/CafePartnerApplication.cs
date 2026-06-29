using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    /// <summary>Phase 1 onboarding application. Immutable after approval except audit timestamps.</summary>
    public class CafePartnerApplication
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string CafeName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string RepresentativeEmail { get; set; } = string.Empty;
        public string BusinessLicense { get; set; } = string.Empty;
        public string? BusinessLicenseImageUrl { get; set; }

        public CafePartnerApplicationStatus Status { get; set; } = CafePartnerApplicationStatus.PendingApproval;
        public string? RejectionReason { get; set; }
        public DateTime? ApprovedAt { get; set; }

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