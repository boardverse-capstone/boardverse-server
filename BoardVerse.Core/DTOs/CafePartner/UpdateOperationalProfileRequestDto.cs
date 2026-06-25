using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.CafePartner
{
    /// <summary>Phase 2 — Web POS operational profile before Active.</summary>
    public class UpdateOperationalProfileRequestDto
    {
        [Range(1, 10000, ErrorMessage = ApiErrorMessages.Validation.TableCountRange)]
        public int NumberOfTables { get; set; }

        [Range(0, 1000, ErrorMessage = ApiErrorMessages.Validation.PrivateRoomCountRange)]
        public int NumberOfPrivateRooms { get; set; }

        [MinLength(3)]
        public List<string> SpaceImageUrls { get; set; } = new();

        [Range(1, 100000, ErrorMessage = ApiErrorMessages.Validation.GamesOwnedRange)]
        public int NumberOfGamesOwned { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.PopularGamesListRequired)]
        [StringLength(2000, MinimumLength = 3, ErrorMessage = ApiErrorMessages.Validation.PopularGamesListLength)]
        public string PopularGamesList { get; set; } = string.Empty;

        public bool HasGameMaster { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.FieldRequired)]
        public CafePartnerBillingModel BillingModel { get; set; }

        /// <summary>Optional. Omitted or empty → backend manages names from <see cref="NumberOfTables"/>.</summary>
        public List<string>? TableNames { get; set; }
    }
}
