using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Admin
{
    public class SystemConfigEntryDto
    {
        public string ConfigKey { get; set; } = string.Empty;
        public string ConfigValue { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
    }

    public class SystemConfigBulkUpdateRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.FieldRequired)]
        [MinLength(1)]
        public List<SystemConfigUpdateItemDto> Configs { get; set; } = [];
    }

    public class SystemConfigUpdateItemDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.ConfigKeyRequired)]
        [StringLength(100, MinimumLength = 2, ErrorMessage = ApiErrorMessages.Validation.ConfigKeyLength)]
        public string ConfigKey { get; set; } = string.Empty;

        [Required(ErrorMessage = ApiErrorMessages.Validation.ConfigValueRequired)]
        [StringLength(500, ErrorMessage = ApiErrorMessages.Validation.ConfigValueMax500)]
        public string ConfigValue { get; set; } = string.Empty;
    }
}
