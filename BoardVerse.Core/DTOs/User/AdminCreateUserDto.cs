using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.User
{
    public class AdminCreateUserDto
    {
        [Required]
        [StringLength(100, ErrorMessage = "Username cannot exceed 100 characters.")]
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
        [StringLength(256, ErrorMessage = "Email cannot exceed 256 characters.")]
        public string Email { get; set; } = string.Empty;

        [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
        public string? Password { get; set; }

        [StringLength(32, ErrorMessage = "Role cannot exceed 32 characters.")]
        public string Role { get; set; } = "User";
    }
}