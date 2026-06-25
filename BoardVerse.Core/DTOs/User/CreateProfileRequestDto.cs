using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.User
{
    public class ProfileCreateDto
    {
        [StringLength(1000, ErrorMessage = ApiErrorMessages.Validation.BioMax1000)]
        public string? Bio { get; set; }

        // Optional PII
        [StringLength(100, ErrorMessage = ApiErrorMessages.Validation.FirstNameMax100)]
        public string? FirstName { get; set; }

        [StringLength(100, ErrorMessage = ApiErrorMessages.Validation.LastNameMax100)]
        public string? LastName { get; set; }
        public DateOnly? DateOfBirth { get; set; }

        [Phone(ErrorMessage = ApiErrorMessages.Validation.PhoneInvalid)]
        [StringLength(50, ErrorMessage = ApiErrorMessages.Validation.PhoneMax50)]
        public string? PhoneNumber { get; set; }
    }
}
