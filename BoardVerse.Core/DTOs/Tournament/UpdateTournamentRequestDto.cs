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

    /// <summary>Phí tham dự (VNĐ). Range 0-10,000,000.</summary>
    [Range(0, 10_000_000)]
    public decimal? EntryFee { get; set; }

    /// <summary>
    /// Mô tả giải thưởng cho người thắng. Optional — null = giữ nguyên,
    /// empty string = xoá giải thưởng, string mới = cập nhật. Tối đa 1000 ký tự.
    /// </summary>
    [StringLength(1000)]
    public string? Prize { get; set; }

    /// <summary>URL ảnh đại diện cho tournament (thumbnail/banner).</summary>
    [StringLength(500)]
    public string? ImageUrl { get; set; }
}