using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Auth.Requests
{
    public class VerifyEmailRequestDto
    {
        [Required(ErrorMessage = "Verification token is required.")]
        [StringLength(10, MinimumLength = 6, ErrorMessage = "Verification token must be between 6 and 10 characters.")]
        public required string Token { get; set; }
    }
}
