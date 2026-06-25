using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Auth.Requests
{
    public class TestGoogleLoginDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.EmailRequired)]
        [EmailAddress(ErrorMessage = ApiErrorMessages.Validation.EmailInvalid)]
        public string Email { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.NameRequired)]
        [StringLength(100, ErrorMessage = ApiErrorMessages.Validation.NameMax100)]
        public string Name { get; set; }
    }
}
