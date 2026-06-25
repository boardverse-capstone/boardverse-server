using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.CafePartner
{
    public class WorkingHoursDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.FieldRequired)]
        public string WeekdayStart { get; set; } = string.Empty;

        [Required(ErrorMessage = ApiErrorMessages.Validation.FieldRequired)]
        public string WeekdayEnd { get; set; } = string.Empty;

        [Required(ErrorMessage = ApiErrorMessages.Validation.FieldRequired)]
        public string WeekendStart { get; set; } = string.Empty;

        [Required(ErrorMessage = ApiErrorMessages.Validation.FieldRequired)]
        public string WeekendEnd { get; set; } = string.Empty;
    }
}
