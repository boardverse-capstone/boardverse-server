using System.ComponentModel.DataAnnotations;

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
        [Required]
        [MinLength(1)]
        public List<SystemConfigUpdateItemDto> Configs { get; set; } = [];
    }

    public class SystemConfigUpdateItemDto
    {
        [Required]
        [StringLength(100, MinimumLength = 2)]
        public string ConfigKey { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string ConfigValue { get; set; } = string.Empty;
    }
}
