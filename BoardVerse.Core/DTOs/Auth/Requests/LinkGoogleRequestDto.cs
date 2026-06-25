using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Auth.Requests
{
    public class LinkGoogleRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.GoogleIdTokenRequired)]
        [StringLength(4000, MinimumLength = 10, ErrorMessage = ApiErrorMessages.Validation.GoogleIdTokenLength)]
        public required string IdToken { get; set; }
    }
}
