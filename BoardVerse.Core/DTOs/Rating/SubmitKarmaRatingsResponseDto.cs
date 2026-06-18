using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Rating
{
    public class SubmitKarmaRatingsResponseDto
    {
        public Guid LobbyId { get; set; }
        public IReadOnlyList<KarmaRatingAppliedDto> AppliedRatings { get; set; } = [];
    }

    public class KarmaRatingAppliedDto
    {
        public Guid TargetUserId { get; set; }
        public IReadOnlyList<KarmaRatingTag> Tags { get; set; } = [];
        public decimal KarmaDeltaApplied { get; set; }
        public int TargetKarmaPointsAfter { get; set; }
        public string TargetGamerTier { get; set; } = string.Empty;
    }
}
