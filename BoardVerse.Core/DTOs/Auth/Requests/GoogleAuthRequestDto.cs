using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Auth.Requests
{
    public class GoogleAuthRequestDto
    {
        [Required(ErrorMessage = "Google idToken is required.")]
        [StringLength(4000, MinimumLength = 10, ErrorMessage = "Google idToken must be between 10 and 4000 characters.")]
        public required string IdToken { get; set; }
    }
}
