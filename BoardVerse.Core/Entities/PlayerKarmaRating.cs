namespace BoardVerse.Core.Entities
{
    /// <summary>Immutable record of a cross-rating submission (one rater → one target per lobby).</summary>
    public class PlayerKarmaRating
    {
        public Guid Id { get; set; }
        public Guid LobbyId { get; set; }
        public Guid RaterUserId { get; set; }
        public Guid TargetUserId { get; set; }
        /// <summary>JSON array of <see cref="Enum.KarmaRatingTag"/> names.</summary>
        public string TagsJson { get; set; } = "[]";
        public decimal KarmaDeltaApplied { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual Lobby Lobby { get; set; } = null!;
        public virtual User RaterUser { get; set; } = null!;
        public virtual User TargetUser { get; set; } = null!;
    }
}
