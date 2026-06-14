using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.CafePartner
{
    /// <summary>Phase 2 — Web POS operational profile before Active.</summary>
    public class UpdateOperationalProfileRequestDto
    {
        [Range(1, 10000)]
        public int NumberOfTables { get; set; }

        [Range(0, 1000)]
        public int NumberOfPrivateRooms { get; set; }

        [MinLength(3)]
        public List<string> SpaceImageUrls { get; set; } = new();

        [Range(1, 100000)]
        public int NumberOfGamesOwned { get; set; }

        [Required]
        [StringLength(2000, MinimumLength = 3)]
        public string PopularGamesList { get; set; } = string.Empty;

        public bool HasGameMaster { get; set; }

        [Required]
        public CafePartnerBillingModel BillingModel { get; set; }

        /// <summary>Optional. Omitted or empty → backend manages names from <see cref="NumberOfTables"/>.</summary>
        public List<string>? TableNames { get; set; }
    }
}
