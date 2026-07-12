using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Session
{
    public class AddGuestSlotRequestDto
    {
        [Required]
        [MaxLength(100)]
        public string DisplayName { get; set; } = string.Empty;
    }
}
