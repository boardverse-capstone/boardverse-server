using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Admin
{
    public class AdminCreateGameComponentRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.ComponentNameRequired)]
        [StringLength(200, MinimumLength = 1, ErrorMessage = ApiErrorMessages.Validation.ComponentNameLength)]
        public string ComponentName { get; set; } = string.Empty;

        public BoardGameComponentKind? ComponentKind { get; set; }

        [Range(1, 9999, ErrorMessage = ApiErrorMessages.Validation.DefaultQuantityRange)]
        public int DefaultQuantity { get; set; } = 1;
    }

    public class AdminUpdateGameComponentRequestDto
    {
        [StringLength(200, MinimumLength = 1, ErrorMessage = ApiErrorMessages.Validation.ComponentNameLength)]
        public string? ComponentName { get; set; }

        public BoardGameComponentKind? ComponentKind { get; set; }

        [Range(1, 9999, ErrorMessage = ApiErrorMessages.Validation.DefaultQuantityRange)]
        public int? DefaultQuantity { get; set; }
    }

    public class AdminSetGameCategoriesRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.FieldRequired)]
        public List<Guid> CategoryIds { get; set; } = [];
    }
}
