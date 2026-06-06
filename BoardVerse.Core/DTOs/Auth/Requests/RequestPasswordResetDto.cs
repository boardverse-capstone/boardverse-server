using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Auth.Requests
{
    public class RequestPasswordResetDto
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
        [StringLength(256, ErrorMessage = "Email cannot exceed 256 characters.")]
        public required string Email { get; set; }
    }
}
