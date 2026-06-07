using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Cafe
{
    public class AddStaffRequestDto
    {
        [Required]
        [EmailAddress]
        public required string Email { get; set; }

        [Required]
        [MaxLength(100)]
        public required string FullName { get; set; }
    }
}
