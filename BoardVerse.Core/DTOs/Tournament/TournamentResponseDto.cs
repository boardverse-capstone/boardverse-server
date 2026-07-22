using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Tournament;

/// <summary>
/// Thông tin tournament trả về cho client (mobile + POS).
/// </summary>
public class TournamentResponseDto
{
    public Guid Id { get; set; }
    public Guid CafeId { get; set; }
    public string CafeName { get; set; } = string.Empty;
    public Guid CreatedByManagerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid GameTemplateId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime RegistrationDeadline { get; set; }
    public int RoundDurationMinutes { get; set; }
    public int MinParticipants { get; set; }
    public int MaxParticipants { get; set; }
    public decimal EntryFee { get; set; }
    public int TotalRounds { get; set; }
    public int PreliminaryRounds { get; set; }
    public int FinalistCount { get; set; }
    public int CurrentRound { get; set; }

    /// <summary>
    /// Thời điểm bắt đầu giải đấu (khi chuyển sang OnGoing).
    /// Dùng để track khi nào tournament start để detect no-show.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    public int MinKarmaRequirement { get; set; }
    public int MinEloRequirement { get; set; }
    public int MaxEloRequirement { get; set; }
    public int NoShowKarmaPenalty { get; set; }

    /// <summary>
    /// Karma bonus cho winner/finalist do hệ thống tự tính theo rank (linear giảm dần).
    /// FinalistBonus theo FinalistCount hiện tại của tournament.
    /// </summary>
    public int WinnerKarmaBonus { get; set; }
    public int FinalistKarmaBonus { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public TournamentStatus Status { get; set; }

    /// <summary>Số người đã đăng ký (snapshot).</summary>
    public int RegisteredCount { get; set; }

    /// <summary>Số người đã check-in.</summary>
    public int CheckedInCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>True nếu user hiện tại đã đăng ký tournament này (optional, set bởi controller).</summary>
    public bool? CurrentUserRegistered { get; set; }

    /// <summary>Trạng thái đăng ký của user hiện tại (optional).</summary>
    public TournamentParticipantStatus? CurrentUserParticipantStatus { get; set; }

    /// <summary>Pairing mode: Auto (hệ thống tự chia) hoặc Manual (manager tự chọn).</summary>
    public TournamentPairingMode PairingMode { get; set; }

    /// <summary>Trạng thái manual pairings đã set cho từng round (1-4).</summary>
    public ManualPairingsSummaryDto ManualPairings { get; set; } = new();
}

public class ManualPairingsSummaryDto
{
    public bool Round1Set { get; set; }
    public bool Round2Set { get; set; }
    public bool Round3Set { get; set; }
    public bool FinalSet { get; set; }
}