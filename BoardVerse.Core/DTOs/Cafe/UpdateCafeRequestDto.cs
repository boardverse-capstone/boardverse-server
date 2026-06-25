using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Cafe
{
    public class UpdateCafeRequestDto
    {
        [StringLength(200, ErrorMessage = ApiErrorMessages.Validation.CafeNameMax200)]
        public string? Name { get; set; }

        [StringLength(500, ErrorMessage = ApiErrorMessages.Validation.AddressMax500)]
        public string? Address { get; set; }

        [StringLength(50, ErrorMessage = ApiErrorMessages.Validation.PhoneNumberMax50)]
        public string? PhoneNumber { get; set; }

        [StringLength(2000, ErrorMessage = ApiErrorMessages.Validation.DescriptionMax2000)]
        public string? Description { get; set; }

        [Range(-90, 90, ErrorMessage = ApiErrorMessages.Validation.LatitudeRange)]
        public double? Latitude { get; set; }

        [Range(-180, 180, ErrorMessage = ApiErrorMessages.Validation.LongitudeRange)]
        public double? Longitude { get; set; }
    }
}
