using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Admin
{
    public class AdminPunishUserRequestDto
    {
        [Required]
        public AdminPunishmentActionType ActionType { get; set; }

        [Range(1, 365)]
        public int? DurationDays { get; set; }

        [Required]
        [StringLength(1000, MinimumLength = 5)]
        public string Reason { get; set; } = string.Empty;
    }
}
