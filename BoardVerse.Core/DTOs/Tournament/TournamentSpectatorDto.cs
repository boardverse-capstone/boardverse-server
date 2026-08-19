namespace BoardVerse.Core.DTOs.Tournament;

public class TournamentSpectatorDto
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public string TournamentTitle { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }
}
