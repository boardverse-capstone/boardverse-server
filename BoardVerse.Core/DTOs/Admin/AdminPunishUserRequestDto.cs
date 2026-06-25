using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Admin
{
    public class AdminPunishUserRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.PunishmentActionRequired)]
        public AdminPunishmentActionType ActionType { get; set; }

        [Range(1, 365, ErrorMessage = ApiErrorMessages.Validation.SuspendDurationRange)]
        public int? DurationDays { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.ReasonRequired)]
        [StringLength(1000, MinimumLength = 5, ErrorMessage = ApiErrorMessages.Validation.ReasonLength5To1000)]
        public string Reason { get; set; } = string.Empty;
    }
}
