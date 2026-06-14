using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.CafePartner
{
    /// <summary>Phase 1 — Landing Page registration.</summary>
    public class SubmitCafePartnerApplicationRequestDto
    {
        [Required]
        [StringLength(100, MinimumLength = 5)]
        public string CafeName { get; set; } = string.Empty;

        [Required]
        [StringLength(500, MinimumLength = 10)]
        public string Address { get; set; } = string.Empty;

        [Required]
        [Range(-90, 90)]
        public double Latitude { get; set; }

        [Required]
        [Range(-180, 180)]
        public double Longitude { get; set; }

        [Required]
        [StringLength(11, MinimumLength = 10)]
        public string Hotline { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string RepresentativeEmail { get; set; } = string.Empty;

        [Required]
        public WorkingHoursDto WorkingHours { get; set; } = new();

        [Required]
        [StringLength(50, MinimumLength = 5)]
        public string BusinessLicense { get; set; } = string.Empty;

        [Required]
        public string BusinessLicenseImageUrl { get; set; } = string.Empty;
    }
}
