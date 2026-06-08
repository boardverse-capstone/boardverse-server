using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Cafe
{
    public class AddStaffRequestDto
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
        public required string Email { get; set; }

        [StringLength(100, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 100 characters.")]
        public string? Username { get; set; }

        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be between 8 and 100 characters.")]
        public string? Password { get; set; }
    }
}
