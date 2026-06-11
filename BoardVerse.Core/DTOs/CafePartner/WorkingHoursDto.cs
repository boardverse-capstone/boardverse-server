using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.CafePartner
{
    public class WorkingHoursDto
    {
        [Required]
        public string WeekdayStart { get; set; } = string.Empty;

        [Required]
        public string WeekdayEnd { get; set; } = string.Empty;

        [Required]
        public string WeekendStart { get; set; } = string.Empty;

        [Required]
        public string WeekendEnd { get; set; } = string.Empty;
    }
}
