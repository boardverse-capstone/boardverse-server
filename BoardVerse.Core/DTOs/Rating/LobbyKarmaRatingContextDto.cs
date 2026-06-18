using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Rating
{
    public class LobbyKarmaRatingContextDto
    {
        public Guid LobbyId { get; set; }
        public string LobbyStatus { get; set; } = string.Empty;
        public bool CanSubmitRatings { get; set; }
        public IReadOnlyList<KarmaRatingTagOptionDto> AvailableTags { get; set; } = [];
        public IReadOnlyList<LobbyMemberRatingTargetDto> MembersToRate { get; set; } = [];
    }

    public class KarmaRatingTagOptionDto
    {
        public KarmaRatingTag Tag { get; set; }
        public decimal KarmaWeight { get; set; }
    }

    public class LobbyMemberRatingTargetDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public bool AlreadyRated { get; set; }
    }
}
