using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.User
{
    public class AdminUpdateUserDto
    {
        [StringLength(100, ErrorMessage = ApiErrorMessages.Validation.UsernameMax100)]
        public string? Username { get; set; }

        [EmailAddress(ErrorMessage = ApiErrorMessages.Validation.EmailInvalid)]
        [StringLength(256, ErrorMessage = ApiErrorMessages.Validation.EmailMaxLength)]
        public string? Email { get; set; }

        [StringLength(128, MinimumLength = 8, ErrorMessage = ApiErrorMessages.Validation.PasswordMin8)]
        public string? Password { get; set; }

        [StringLength(32, ErrorMessage = ApiErrorMessages.Validation.RoleMax32)]
        public string? Role { get; set; }
        public bool? IsActive { get; set; }
    }
}
