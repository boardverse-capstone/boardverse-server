using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Auth.Requests
{
    public class ResetPasswordDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.ResetTokenRequired)]
        [StringLength(10, MinimumLength = 6, ErrorMessage = ApiErrorMessages.Validation.ResetTokenLength)]
        public required string Token { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.NewPasswordRequired)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = ApiErrorMessages.Validation.PasswordLength6To100)]
        public required string NewPassword { get; set; }
    }
}
