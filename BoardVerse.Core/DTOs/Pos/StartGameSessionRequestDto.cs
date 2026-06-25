using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Pos
{
    public class StartGameSessionRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.TableIdRequired)]
        public Guid CafeTableId { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.BarcodeRequired)]
        [StringLength(50, MinimumLength = 3, ErrorMessage = ApiErrorMessages.Validation.BarcodeLength)]
        public string Barcode { get; set; } = string.Empty;
    }
}
