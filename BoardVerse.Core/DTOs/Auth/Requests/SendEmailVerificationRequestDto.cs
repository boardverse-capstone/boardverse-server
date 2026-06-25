using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Auth.Requests
{
    public class SendEmailVerificationRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.EmailRequired)]
        [EmailAddress(ErrorMessage = ApiErrorMessages.Validation.EmailInvalid)]
        [StringLength(256, ErrorMessage = ApiErrorMessages.Validation.EmailMaxLength)]
        public required string Email { get; set; }
    }
}
