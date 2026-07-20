using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Tournament;

/// <summary>
/// Manager ghi nhận kết quả 1 bàn đấu (Swiss/Final).
/// Tối đa 4 player, mỗi người có score + số thẻ đã mua (tiebreaker).
/// Winner phải là 1 trong 4 player đã liệt kê.
/// </summary>
public class RecordMatchResultRequestDto
{
    [Required]
    public Guid MatchId { get; set; }

    /// <summary>
    /// WinnerParticipantId — phải nằm trong 4 player slot.
    /// Walk-in có UserId=null nên WinnerUserId cũng nullable.
    /// </summary>
    [Required]
    public Guid? WinnerUserId { get; set; }

    /// <summary>StaffId đã ghi nhận (auto-fill từ claims nếu null).</summary>
    public Guid? RecordedByStaffId { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    [Required]
    [MinLength(2)]
    public List<MatchPlayerResultDto> Results { get; set; } = new();
}

public class MatchPlayerResultDto
{
    /// <summary>
    /// TournamentParticipant.Id của player trong trận đấu.
    /// Registered player: UserId = participant.UserId; Walk-in: UserId = null.
    /// Để walk-in có thể tham gia Final, dùng ParticipantId để lookup.
    /// </summary>
    [Required]
    public Guid? UserId { get; set; }

    /// <summary>
    /// Điểm Prestige Points cuối ván (0–30).
    /// Validation thực tế ở service layer dùng per-game config
    /// (Splendor = 15, Splendor Duel = 20); Range attribute chỉ là upper-bound defensive
    /// để chặn input rác trước khi gọi DB.
    /// </summary>
    [Range(0, 30)]
    public int Score { get; set; }

    /// <summary>Số thẻ Development đã mua — dùng làm tiebreaker.</summary>
    [Range(0, 50)]
    public int CardsBought { get; set; }
}

public class TournamentMatchResponseDto
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public int RoundNumber { get; set; }
    public int MatchNumber { get; set; }
    public bool IsFinal { get; set; }

    public Guid? Player1Id { get; set; }
    public Guid? Player2Id { get; set; }
    public Guid? Player3Id { get; set; }
    public Guid? Player4Id { get; set; }

    public int? Player1Score { get; set; }
    public int? Player2Score { get; set; }
    public int? Player3Score { get; set; }
    public int? Player4Score { get; set; }

    public int? Player1CardsBought { get; set; }
    public int? Player2CardsBought { get; set; }
    public int? Player3CardsBought { get; set; }
    public int? Player4CardsBought { get; set; }

    public Guid? WinnerPlayerId { get; set; }
    public TournamentMatchStatus Status { get; set; }

    public DateTime? ScheduledStartTime { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }

    public string? Notes { get; set; }
}