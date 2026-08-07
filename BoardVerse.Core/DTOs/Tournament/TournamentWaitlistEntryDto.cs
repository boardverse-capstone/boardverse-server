using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Tournament;

public class TournamentWaitlistEntryDto
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public string TournamentTitle { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int Position { get; set; }
    public TournamentWaitlistStatus Status { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? OfferedAt { get; set; }
    public DateTime? OfferExpiresAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
}
