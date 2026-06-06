using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Auth.Requests
{
    public class RefreshTokenRequestDto
    {
        [Required(ErrorMessage = "Refresh token is required.")]
        [StringLength(500, MinimumLength = 20, ErrorMessage = "Refresh token must be between 20 and 500 characters.")]
        public required string RefreshToken { get; set; }
    }
}
