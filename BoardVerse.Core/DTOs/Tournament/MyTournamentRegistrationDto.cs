using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Tournament;

/// <summary>
/// Thông tin tournament + vai trò cá nhân của user trong tournament đó.
/// Dùng cho API GET /tournaments/my-registrations.
/// </summary>
public class MyTournamentRegistrationDto
{
    public Guid TournamentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string CafeName { get; set; } = string.Empty;
    public Guid CafeId { get; set; }
    public DateTime StartTime { get; set; }
    public TournamentStatus TournamentStatus { get; set; }

    // === Player's personal data in this tournament ===
    public Guid ParticipantId { get; set; }
    public TournamentParticipantStatus ParticipantStatus { get; set; }
    public bool IsWalkIn { get; set; }
    public string? WalkInDisplayName { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime? CheckedInAt { get; set; }

    /// <summary>Swiss score = Wins + Draws*0.5.</summary>
    public decimal SwissScore { get; set; }
    public int SwissWins { get; set; }
    public int SwissDraws { get; set; }
    public int SwissLosses { get; set; }

    /// <summary>Final rank (1 = winner). Null nếu tournament chưa Completed.</summary>
    public int? FinalRank { get; set; }

    public int InitialElo { get; set; }
    public int FinalElo { get; set; }
    public int EloDelta { get; set; }
}
