using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Pos
{
    /// <summary>
    /// PATCH DTO để cập nhật một phần thông tin bàn (Name, SeatCount, SortOrder).
    /// Tất cả field optional — chỉ field được gửi mới được cập nhật.
    /// </summary>
    public class UpdateCafeTableRequestDto
    {
        [StringLength(100, MinimumLength = 1, ErrorMessage = ApiErrorMessages.Validation.TableNameLength)]
        public string? Name { get; set; }

        [Range(1, 50, ErrorMessage = ApiErrorMessages.Validation.SeatsPerTableRange)]
        public int? SeatCount { get; set; }

        [Range(0, 9999, ErrorMessage = ApiErrorMessages.Validation.SortOrderRange)]
        public int? SortOrder { get; set; }
    }
}
