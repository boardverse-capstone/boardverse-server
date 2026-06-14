using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Cafe
{
    public class UpdateCafeRequestDto
    {
        [StringLength(200, ErrorMessage = "Name cannot exceed 200 characters.")]
        public string? Name { get; set; }

        [StringLength(500, ErrorMessage = "Address cannot exceed 500 characters.")]
        public string? Address { get; set; }

        [StringLength(50, ErrorMessage = "Phone number cannot exceed 50 characters.")]
        public string? PhoneNumber { get; set; }

        [StringLength(2000, ErrorMessage = "Description cannot exceed 2000 characters.")]
        public string? Description { get; set; }

        [Range(-90, 90)]
        public double? Latitude { get; set; }

        [Range(-180, 180)]
        public double? Longitude { get; set; }
    }
}
