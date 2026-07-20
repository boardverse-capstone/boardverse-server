using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Tournament;

/// <summary>
/// Yêu cầu tạo giải đấu Splendor (Manager). [Role: Manager]
/// Game hiện tại cố định là Splendor — GameTemplateId nhận từ client nhưng
/// service sẽ verify trùng với Splendor hoặc dùng lookup tự động.
/// </summary>
public class CreateTournamentRequestDto
{
    [Required]
    [StringLength(200, MinimumLength = 5)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    /// <summary>Mã Splendor GameTemplate. Optional — nếu null, service tự lookup "Splendor".</summary>
    public Guid? GameTemplateId { get; set; }

    [Required]
    public DateTime StartTime { get; set; }

    /// <summary>Hạn chót đăng ký. Optional — default = StartTime - 24h.</summary>
    public DateTime? RegistrationDeadline { get; set; }

    /// <summary>Phút/vòng. Default 45.</summary>
    [Range(15, 240)]
    public int RoundDurationMinutes { get; set; } = 45;

    /// <summary>Số người tối đa. Default 32 (8 bàn). Phải là bội số của 4.</summary>
    [Range(4, 32)]
    public int MaxParticipants { get; set; } = 32;

    /// <summary>Điểm Karma tối thiểu để đăng ký. Default 0.</summary>
    [Range(0, 1000)]
    public int MinKarmaRequirement { get; set; } = 0;

    [Range(0, 500)]
    public int WinnerKarmaBonus { get; set; } = 50;

    [Range(0, 500)]
    public int FinalistKarmaBonus { get; set; } = 20;

    [Range(-500, 0)]
    public int NoShowKarmaPenalty { get; set; } = -30;

    /// <summary>Pairing mode: Auto (mặc định) hoặc Manual. Manager có thể đổi sau qua /pairing-mode endpoint.</summary>
    public TournamentPairingMode PairingMode { get; set; } = TournamentPairingMode.Auto;

    /// <summary>
    /// F18 Fix: Số người tối thiểu để mở giải. Optional — nếu null, dùng GameTemplate.TournamentMinPlayersPerTable.
    /// Manager có thể override cao hơn (vd game 2-min nhưng muốn tối thiểu 4 cho vui) nhưng không thấp hơn GameTemplate config.
    /// </summary>
    [Range(2, 32)]
    public int? MinParticipants { get; set; }
}