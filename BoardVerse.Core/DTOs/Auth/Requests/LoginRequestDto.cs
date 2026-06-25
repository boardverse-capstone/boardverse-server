using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Auth.Requests
{
    public class LoginRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.UsernameOrEmailRequired)]
        [StringLength(256, MinimumLength = 3, ErrorMessage = ApiErrorMessages.Validation.UsernameOrEmailLength3To256)]
        public required string UsernameOrEmail { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.PasswordRequired)]
        [StringLength(100, MinimumLength = 8, ErrorMessage = ApiErrorMessages.Validation.PasswordLength8To100)]
        public required string Password { get; set; }
    }
}
