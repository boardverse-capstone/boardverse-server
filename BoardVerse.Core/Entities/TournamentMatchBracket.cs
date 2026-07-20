using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Một bàn đấu trong tournament Splendor.
/// Mỗi bàn có tối đa 4 người chơi (theo luật Splendor 2-4 players).
///
/// Swiss rounds: người chơi được ghép theo thứ hạng (cùng điểm gặp nhau).
/// Final round: 4 người cao điểm nhất sau PreliminaryRounds, chọn ra 1 Winner.
///
/// Manager (qua POS) nhập kết quả từng bàn: WinnerPlayerId + điểm từng người.
/// </summary>
public class TournamentMatchBracket
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // === Relationships ===
    public Guid TournamentId { get; set; }

    // === Match identity ===
    /// <summary>Vòng đấu (1-3 cho Swiss, 4 cho Final).</summary>
    public int RoundNumber { get; set; }

    /// <summary>Số thứ tự bàn trong vòng (1, 2, 3...).</summary>
    public int MatchNumber { get; set; }

    /// <summary>True nếu đây là bàn chung kết.</summary>
    public bool IsFinal { get; set; }

    // === Players (tối đa 4 cho Splendor) ===
    public Guid? Player1Id { get; set; }
    public Guid? Player2Id { get; set; }
    public Guid? Player3Id { get; set; }
    public Guid? Player4Id { get; set; }

    // === Scores (Prestige Points sau ván đấu) ===
    public int? Player1Score { get; set; }
    public int? Player2Score { get; set; }
    public int? Player3Score { get; set; }
    public int? Player4Score { get; set; }

    // === Tiebreaker info (cho Swiss ranking) ===
    /// <summary>Số thẻ Development đã mua của Player1. Snapshot để tiebreak.</summary>
    public int? Player1CardsBought { get; set; }
    public int? Player2CardsBought { get; set; }
    public int? Player3CardsBought { get; set; }
    public int? Player4CardsBought { get; set; }

    // === Result ===
    /// <summary>UserId của người thắng ván này (1 trong 4 player).</summary>
    public Guid? WinnerPlayerId { get; set; }

    /// <summary>
    /// True khi Elo delta của ván này đã được aggregate vào TournamentParticipant.
    /// Tránh apply 2 lần nếu admin re-run logic.
    /// </summary>
    public bool EloApplied { get; set; }

    /// <summary>
    /// K-factor được dùng cho ván này (snapshot từ SystemConfig).
    /// Hữu ích cho audit và replay.
    /// </summary>
    public int EloKFactorUsed { get; set; }

    // === State & Timing ===
    public TournamentMatchStatus Status { get; set; } = TournamentMatchStatus.Scheduled;

    public DateTime? ScheduledStartTime { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }

    /// <summary>StaffId của manager đã ghi nhận kết quả.</summary>
    public Guid? RecordedByStaffId { get; set; }

    public string? Notes { get; set; }

    // === Audit ===
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // === Navigation ===
    public virtual Tournament Tournament { get; set; } = null!;
    public virtual User? Player1 { get; set; }
    public virtual User? Player2 { get; set; }
    public virtual User? Player3 { get; set; }
    public virtual User? Player4 { get; set; }
    public virtual User? WinnerPlayer { get; set; }
    public virtual User? RecordedByStaff { get; set; }
}
