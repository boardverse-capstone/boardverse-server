using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Admin
{
    public class AdminCreateGameComponentRequestDto
    {
        [Required]
        [StringLength(200, MinimumLength = 1)]
        public string ComponentName { get; set; } = string.Empty;

        public BoardGameComponentKind? ComponentKind { get; set; }

        [Range(1, 9999)]
        public int DefaultQuantity { get; set; } = 1;
    }

    public class AdminUpdateGameComponentRequestDto
    {
        [StringLength(200, MinimumLength = 1)]
        public string? ComponentName { get; set; }

        public BoardGameComponentKind? ComponentKind { get; set; }

        [Range(1, 9999)]
        public int? DefaultQuantity { get; set; }
    }

    public class AdminSetGameCategoriesRequestDto
    {
        [Required]
        public List<Guid> CategoryIds { get; set; } = [];
    }
}
