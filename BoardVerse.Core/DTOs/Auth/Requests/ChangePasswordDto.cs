using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Auth.Requests
{
    public class ChangePasswordDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.CurrentPasswordRequired)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = ApiErrorMessages.Validation.PasswordLength6To100)]
        public required string CurrentPassword { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.NewPasswordRequired)]
        [StringLength(100, MinimumLength = 8, ErrorMessage = ApiErrorMessages.Validation.PasswordLength8To100)]
        public required string NewPassword { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.ConfirmPasswordRequired)]
        [Compare(nameof(NewPassword), ErrorMessage = ApiErrorMessages.Validation.ConfirmPasswordMismatch)]
        public required string ConfirmNewPassword { get; set; }
    }
}
