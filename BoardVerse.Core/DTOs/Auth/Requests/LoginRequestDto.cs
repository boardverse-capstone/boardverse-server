using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Auth.Requests
{
    public class LoginRequestDto
    {
        [Required(ErrorMessage = "Username or email is required.")]
        [StringLength(256, MinimumLength = 3, ErrorMessage = "Username or email must be between 3 and 256 characters.")]
        public required string UsernameOrEmail { get; set; }

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be between 6 and 100 characters.")]
        public required string Password { get; set; }
    }
}
