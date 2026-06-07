using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Cafe
{
    public class AddStaffRequestDto
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
        public required string Email { get; set; }

        [Required(ErrorMessage = "Full name is required.")]
        [MaxLength(100, ErrorMessage = "Full name cannot exceed 100 characters.")]
        public required string FullName { get; set; }
    }
}
