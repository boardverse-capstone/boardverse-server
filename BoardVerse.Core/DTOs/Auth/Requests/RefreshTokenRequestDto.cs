using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Auth.Requests
{
    public class RefreshTokenRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.RefreshTokenRequired)]
        [StringLength(500, MinimumLength = 20, ErrorMessage = ApiErrorMessages.Validation.RefreshTokenLength)]
        public required string RefreshToken { get; set; }
    }
}
