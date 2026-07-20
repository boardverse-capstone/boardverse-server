namespace BoardVerse.Core.Entities;

/// <summary>
/// Lưu contribution Elo của từng player trong từng match.
/// Dùng cho: revert Elo khi sửa kết quả match (UpdateMatchResultAsync).
/// Mỗi row = EloDelta của 1 player trong 1 match.
/// </summary>
public class TournamentMatchEloContribution
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Mã match tương ứng.</summary>
    public Guid MatchId { get; set; }

    /// <summary>Mã participant (TournamentParticipant) tương ứng.</summary>
    public Guid ParticipantId { get; set; }

    /// <summary>Elo delta đã apply cho player trong match này.</summary>
    public int EloDelta { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // === Navigation ===
    public virtual TournamentMatchBracket Match { get; set; } = null!;
    public virtual TournamentParticipant Participant { get; set; } = null!;
}
