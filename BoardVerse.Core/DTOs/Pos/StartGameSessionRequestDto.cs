using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Pos
{
    public class StartGameSessionRequestDto
    {
        [Required]
        public Guid CafeTableId { get; set; }

        [Required]
        [StringLength(50, MinimumLength = 3)]
        public string Barcode { get; set; } = string.Empty;
    }
}
