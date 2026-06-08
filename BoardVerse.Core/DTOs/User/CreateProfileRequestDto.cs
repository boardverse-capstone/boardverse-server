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
        public DateTime? DateOfBirth { get; set; }

        [StringLength(250, ErrorMessage = "Home address cannot exceed 250 characters.")]
        public string? HomeAddress { get; set; }
    }
}