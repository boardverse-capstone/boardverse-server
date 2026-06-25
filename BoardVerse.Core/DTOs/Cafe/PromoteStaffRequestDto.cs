using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Cafe
{
    public class PromoteStaffRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.EmailRequired)]
        [EmailAddress(ErrorMessage = ApiErrorMessages.Validation.EmailInvalid)]
        public required string Email { get; set; }

        [StringLength(100, MinimumLength = 3, ErrorMessage = ApiErrorMessages.Validation.UsernameLength3To100)]
        public string? Username { get; set; }

        [StringLength(100, MinimumLength = 8, ErrorMessage = ApiErrorMessages.Validation.PasswordLength8To100)]
        public string? Password { get; set; }
    }
}
