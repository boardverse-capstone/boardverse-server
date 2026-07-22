using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Tournament;

/// <summary>
/// Cập nhật thông tin tournament khi còn ở trạng thái Draft.
/// </summary>
public class UpdateTournamentRequestDto
{
    [StringLength(200, MinimumLength = 5)]
    public string? Title { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    public DateTime? StartTime { get; set; }
    public DateTime? RegistrationDeadline { get; set; }

    [Range(15, 240)]
    public int? RoundDurationMinutes { get; set; }

    [Range(4, 32)]
    public int? MaxParticipants { get; set; }

    [Range(0, 100)]
    public int? MinKarmaRequirement { get; set; }

    [Range(0, 5000)]
    public int? MinEloRequirement { get; set; }

    [Range(0, 5000)]
    public int? MaxEloRequirement { get; set; }

    [Range(-100, 0)]
    public int? NoShowKarmaPenalty { get; set; }

    /// <summary>Manager bật/tắt tự động extend registration khi thiếu người.</summary>
    public bool? AutoExtendOnShortage { get; set; }

    /// <summary>Số lần extend tối đa (0-5). Null = giữ nguyên config cũ.</summary>
    [Range(0, 5)]
    public int? MaxExtensionCount { get; set; }

    /// <summary>Số phút mỗi lần extend (5-120). Null = giữ nguyên config cũ.</summary>
    [Range(5, 120)]
    public int? ExtensionMinutesPerAttempt { get; set; }

    /// <summary>Manager chỉ định số rounds Swiss (1-5). Null = giữ nguyên PreliminaryRounds.</summary>
    [Range(1, 5)]
    public int? PreliminaryRounds { get; set; }
}