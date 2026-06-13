using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.User
{
    public class ProfileCreateDto
    {
        [StringLength(1000, ErrorMessage = "Bio cannot exceed 1000 characters.")]
        public string? Bio { get; set; }

        // Optional PII
        [StringLength(100, ErrorMessage = "First name cannot exceed 100 characters.")]
        public string? FirstName { get; set; }

        [StringLength(100, ErrorMessage = "Last name cannot exceed 100 characters.")]
        public string? LastName { get; set; }
        public DateOnly? DateOfBirth { get; set; }

        [Phone(ErrorMessage = "Phone number is invalid.")]
        [StringLength(50, ErrorMessage = "Phone number cannot exceed 50 characters.")]
        public string? PhoneNumber { get; set; }
    }
}