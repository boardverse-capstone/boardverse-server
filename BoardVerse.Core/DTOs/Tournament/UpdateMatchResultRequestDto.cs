using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Tournament;

/// <summary>
/// Manager sửa kết quả 1 bàn đấu đã ghi nhận (case nhập sai điểm / sai winner tại POS).
/// Chỉ cho phép khi match Status = Completed và Swiss round chưa qua (chưa build round kế tiếp).
/// Final match không cho sửa (FinalRank + Karma + Elo đã sync xong).
/// </summary>
public class UpdateMatchResultRequestDto
{
    [Required]
    public Guid MatchId { get; set; }

    /// <summary>
    /// WinnerParticipantId mới — phải nằm trong 4 player slot.
    /// Walk-in có UserId=null nên WinnerUserId cũng nullable.
    /// </summary>
    [Required]
    public Guid? WinnerUserId { get; set; }

    /// <summary>Lý do sửa (bắt buộc, audit trail).</summary>
    [Required]
    [StringLength(500)]
    public string CorrectionReason { get; set; } = string.Empty;

    [Required]
    [MinLength(2)]
    public List<MatchPlayerResultDto> Results { get; set; } = new();
}
