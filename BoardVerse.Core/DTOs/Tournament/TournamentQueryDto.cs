namespace BoardVerse.Core.DTOs.Tournament;

/// <summary>
/// Query params cho GET /api/v1/tournaments.
/// </summary>
public class TournamentQueryDto
{
    public Guid? CafeId { get; set; }
    public string? Status { get; set; }
    public bool UpcomingOnly { get; set; } = false;
    public bool IncludeFullDetail { get; set; } = false;
}