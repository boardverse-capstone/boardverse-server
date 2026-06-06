using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Auth.Requests
{
    public class ChangePasswordDto
    {
        [Required(ErrorMessage = "Current password is required.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Current password must be between 6 and 100 characters.")]
        public required string CurrentPassword { get; set; }

        [Required(ErrorMessage = "New password is required.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "New password must be between 8 and 100 characters.")]
        public required string NewPassword { get; set; }

        [Required(ErrorMessage = "Confirm new password is required.")]
        [Compare(nameof(NewPassword), ErrorMessage = "Confirm new password must match the new password.")]
        public required string ConfirmNewPassword { get; set; }
    }
}
