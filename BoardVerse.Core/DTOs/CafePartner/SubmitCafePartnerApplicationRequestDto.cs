using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.CafePartner
{
    /// <summary>Phase 1 — Landing Page registration.</summary>
    public class SubmitCafePartnerApplicationRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.CafePartnerCafeNameRequired)]
        [StringLength(100, MinimumLength = 5, ErrorMessage = ApiErrorMessages.Validation.CafePartnerCafeNameLength)]
        public string CafeName { get; set; } = string.Empty;

        [Required(ErrorMessage = ApiErrorMessages.Validation.CafePartnerAddressRequired)]
        [StringLength(500, MinimumLength = 10, ErrorMessage = ApiErrorMessages.Validation.CafePartnerAddressLength)]
        public string Address { get; set; } = string.Empty;

        [Required(ErrorMessage = ApiErrorMessages.Validation.FieldRequired)]
        [Range(-90, 90, ErrorMessage = ApiErrorMessages.Validation.LatitudeRange)]
        public double Latitude { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.FieldRequired)]
        [Range(-180, 180, ErrorMessage = ApiErrorMessages.Validation.LongitudeRange)]
        public double Longitude { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.CafePartnerHotlineRequired)]
        [StringLength(11, MinimumLength = 10, ErrorMessage = ApiErrorMessages.Validation.CafePartnerHotlineLength)]
        public string Hotline { get; set; } = string.Empty;

        [Required(ErrorMessage = ApiErrorMessages.Validation.CafePartnerRepresentativeEmailRequired)]
        [EmailAddress(ErrorMessage = ApiErrorMessages.Validation.EmailInvalid)]
        [StringLength(256, ErrorMessage = ApiErrorMessages.Validation.EmailMaxLength)]
        public string RepresentativeEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = ApiErrorMessages.Validation.WorkingHoursRequired)]
        public WorkingHoursDto WorkingHours { get; set; } = new();

        [Required(ErrorMessage = ApiErrorMessages.Validation.CafePartnerBusinessLicenseRequired)]
        [StringLength(50, MinimumLength = 5, ErrorMessage = ApiErrorMessages.Validation.CafePartnerBusinessLicenseLength)]
        public string BusinessLicense { get; set; } = string.Empty;

        [Required(ErrorMessage = ApiErrorMessages.Validation.CafePartnerBusinessLicenseImageRequired)]
        public string BusinessLicenseImageUrl { get; set; } = string.Empty;
    }
}
