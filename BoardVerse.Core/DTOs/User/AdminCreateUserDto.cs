using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.User
{
    public class AdminCreateUserDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.UsernameRequired)]
        [StringLength(100, ErrorMessage = ApiErrorMessages.Validation.UsernameMax100)]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = ApiErrorMessages.Validation.EmailRequired)]
        [EmailAddress(ErrorMessage = ApiErrorMessages.Validation.EmailInvalid)]
        [StringLength(256, ErrorMessage = ApiErrorMessages.Validation.EmailMaxLength)]
        public string Email { get; set; } = string.Empty;

        [StringLength(128, MinimumLength = 8, ErrorMessage = ApiErrorMessages.Validation.PasswordMin8)]
        public string? Password { get; set; }

        [StringLength(32, ErrorMessage = ApiErrorMessages.Validation.RoleMax32)]
        public string Role { get; set; } = "Player";
    }
}
