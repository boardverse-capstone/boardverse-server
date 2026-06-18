using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Rating
{
    public class SubmitKarmaRatingsRequestDto
    {
        [Required]
        public Guid LobbyId { get; set; }

        [Required]
        [MinLength(1)]
        public List<KarmaRatingEntryDto> Ratings { get; set; } = [];
    }

    public class KarmaRatingEntryDto
    {
        [Required]
        public Guid TargetUserId { get; set; }

        /// <summary>Optional duplicate of root lobbyId; root value takes precedence when set.</summary>
        public Guid? LobbyId { get; set; }

        [Required]
        [MinLength(1)]
        public List<KarmaRatingTag> Tags { get; set; } = [];
    }
}
