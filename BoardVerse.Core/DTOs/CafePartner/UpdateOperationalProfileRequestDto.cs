using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.CafePartner
{
    /// <summary>
    /// Phase 2 — Web POS operational profile before Active.
    /// NumberOfTables/TableNames/NumberOfGamesOwned/PopularGamesList are derived server-side
    /// from <c>CafeTables</c>, <c>CafeGameInventory</c>, respectively. Their values were
    /// previously redundant — managed via dedicated endpoints (POST inventory, PATCH tables).
    /// </summary>
    public class UpdateOperationalProfileRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.WorkingHoursRequired)]
        public WorkingHoursDto WorkingHours { get; set; } = new();

        [Range(0, 1000, ErrorMessage = ApiErrorMessages.Validation.PrivateRoomCountRange)]
        public int NumberOfPrivateRooms { get; set; }

        [MinLength(3)]
        public List<string> SpaceImageUrls { get; set; } = new();

        public bool HasGameMaster { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.FieldRequired)]
        public CafePartnerBillingModel BillingModel { get; set; }

        [Range(0, 10000000, ErrorMessage = ApiErrorMessages.Validation.BasePriceRange)]
        public decimal BasePrice { get; set; }

        /// <summary>Giá block lũy tiến theo phút. Bắt buộc với TimeBased; bỏ qua với FlatEntry.</summary>
        public decimal? TieredBlockRate { get; set; }

        /// <summary>Thời gian mỗi block tính tiền (phút). Mặc định 15.</summary>
        [Range(1, 1440, ErrorMessage = ApiErrorMessages.Validation.TieredBlockMinutesRange)]
        public int TieredBlockMinutes { get; set; } = 15;

        /// <summary>% cọc so với giá base. Mặc định 50%.</summary>
        [Range(0, 0.5, ErrorMessage = ApiErrorMessages.Validation.DepositPercentageRange)]
        public decimal DepositPercentage { get; set; } = 0.5m;
    }
}
