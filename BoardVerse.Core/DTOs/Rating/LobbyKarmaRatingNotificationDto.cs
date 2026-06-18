namespace BoardVerse.Core.DTOs.Rating
{
    /// <summary>Payload for AC 3.1 — mobile push when POS completes billing (push delivery is client-side).</summary>
    public class LobbyKarmaRatingNotificationDto
    {
        public Guid LobbyId { get; set; }
        public IReadOnlyList<Guid> MemberUserIds { get; set; } = [];
        public DateTime RatingOpenedAt { get; set; }
        public string NotificationType { get; set; } = "KarmaRatingRequired";
    }
}
