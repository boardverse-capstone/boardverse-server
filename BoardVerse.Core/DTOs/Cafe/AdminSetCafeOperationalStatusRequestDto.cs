using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Cafe
{
    public class AdminSetCafeOperationalStatusRequestDto
    {
        /// <summary>DATA_BLANK, ACTIVE, INACTIVE, or BANNED.</summary>
        [Required]
        [StringLength(32)]
        public string Status { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Reason { get; set; }
    }
}
