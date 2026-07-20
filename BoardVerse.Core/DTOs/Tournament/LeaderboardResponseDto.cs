namespace BoardVerse.Core.DTOs.Tournament;

/// <summary>
/// Top N users theo GlobalElo (BR-10: Elo tính từ Tournament, không dùng cho Lobby).
/// </summary>
public class LeaderboardEntryDto
{
    public int Rank { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int GlobalElo { get; set; }
    public int TournamentsPlayed { get; set; }

    /// <summary>Số lần user đạt FinalRank = 1.</summary>
    public int ChampionsCount { get; set; }
}

public class LeaderboardResponseDto
{
    public int TotalPlayers { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<LeaderboardEntryDto> Entries { get; set; } = new();
}