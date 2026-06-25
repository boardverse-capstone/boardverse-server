using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Rating
{
    public class SubmitKarmaRatingsRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.LobbyIdRequired)]
        public Guid LobbyId { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.RatingsRequired)]
        [MinLength(1)]
        public List<KarmaRatingEntryDto> Ratings { get; set; } = [];
    }

    public class KarmaRatingEntryDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.TargetUserIdRequired)]
        public Guid TargetUserId { get; set; }

        /// <summary>Optional duplicate of root lobbyId; root value takes precedence when set.</summary>
        public Guid? LobbyId { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.TagsRequired)]
        [MinLength(1)]
        public List<KarmaRatingTag> Tags { get; set; } = [];
    }
}
