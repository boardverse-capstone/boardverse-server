using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.User
{
    public class UpdateAvatarRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.AvatarUrlRequired)]
        [Url(ErrorMessage = ApiErrorMessages.Validation.AvatarUrlInvalid)]
        public string AvatarUrl { get; set; } = string.Empty;
    }
}
