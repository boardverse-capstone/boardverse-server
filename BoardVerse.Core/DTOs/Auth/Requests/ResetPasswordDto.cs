using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Auth.Requests
{
    public class ResetPasswordDto
    {
        [Required(ErrorMessage = "Reset token is required.")]
        [StringLength(10, MinimumLength = 6, ErrorMessage = "Reset token must be between 6 and 10 characters.")]
        public required string Token { get; set; }

        [Required(ErrorMessage = "New password is required.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "New password must be between 6 and 100 characters.")]
        public required string NewPassword { get; set; }
    }
}
