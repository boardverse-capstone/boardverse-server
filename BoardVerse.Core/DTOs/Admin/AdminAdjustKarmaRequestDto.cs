using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Admin
{
    public class AdminAdjustKarmaRequestDto
    {
        [Required]
        [Range(-100, 100)]
        public int Amount { get; set; }

        [Required]
        [StringLength(1000, MinimumLength = 5)]
        public string Reason { get; set; } = string.Empty;
    }
}
