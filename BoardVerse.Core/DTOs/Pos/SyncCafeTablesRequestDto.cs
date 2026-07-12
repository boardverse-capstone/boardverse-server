using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Pos
{
    public class SyncCafeTablesRequestDto
    {
        [Required]
        [MinLength(1, ErrorMessage = "Cần ít nhất 1 tên bàn.")]
        public List<string> TableNames { get; set; } = [];
    }
}
