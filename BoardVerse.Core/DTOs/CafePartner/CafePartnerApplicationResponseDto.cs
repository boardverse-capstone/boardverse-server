namespace BoardVerse.Core.DTOs.CafePartner
{
    /// <summary>Phase 1 application — submit, public lookup, admin review.</summary>
    public class CafePartnerApplicationResponseDto
    {
        public Guid Id { get; set; }
        public string CafeName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string RepresentativeEmail { get; set; } = string.Empty;
        public string BusinessLicense { get; set; } = string.Empty;
        public string? BusinessLicenseImageUrl { get; set; }

        public string ApplicationStatus { get; set; } = string.Empty;
        public string? RejectionReason { get; set; }

        /// <summary>Set after admin approval.</summary>
        public Guid? CreatedCafeId { get; set; }

        /// <summary>Summary from linked cafe when approved (null while pending).</summary>
        public string? OperationalStatus { get; set; }

        public Guid? SubmittedByUserId { get; set; }
        public string? SubmittedByUsername { get; set; }
        public Guid? ReviewedByAdminId { get; set; }
        public string? ReviewedByAdminUsername { get; set; }
        public Guid? CreatedManagerUserId { get; set; }
        public DateTime SubmittedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
    }
}
