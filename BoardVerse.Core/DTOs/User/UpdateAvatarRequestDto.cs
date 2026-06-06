using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.User
{
    public class UpdateAvatarRequestDto
    {
        [Required(ErrorMessage = "Avatar URL is required.")]
        [Url(ErrorMessage = "Avatar URL must be a valid URL.")]
        public string AvatarUrl { get; set; } = string.Empty;
    }
}