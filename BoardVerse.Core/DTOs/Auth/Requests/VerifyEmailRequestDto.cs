using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Auth.Requests
{
    public class VerifyEmailRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.VerificationTokenRequired)]
        [StringLength(10, MinimumLength = 6, ErrorMessage = ApiErrorMessages.Validation.VerificationTokenLength)]
        public required string Token { get; set; }
    }
}
