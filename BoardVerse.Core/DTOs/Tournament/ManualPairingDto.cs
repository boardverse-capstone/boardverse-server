using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Tournament;

/// <summary>
/// Một bàn đấu do manager tự sắp xếp (manual pairing).
/// </summary>
public class ManualPairingDto
{
    /// <summary>Số thứ tự bàn trong vòng (1, 2, 3...).</summary>
    [Range(1, 100)]
    public int MatchNumber { get; set; }

    /// <summary>
    /// Danh sách UserId ngồi cùng bàn (2-4 người cho Swiss; đúng 4 cho Final).
    /// Thứ tự trong list quyết định Player1Id, Player2Id, Player3Id, Player4Id trong match.
    /// </summary>
    [Required]
    [MinLength(2)]
    [MaxLength(4)]
    public List<Guid> PlayerIds { get; set; } = new();
}

/// <summary>
/// Request lưu manual pairings cho 1 vòng đấu.
/// </summary>
public class SetRoundPairingsRequestDto
{
    /// <summary>RoundNumber: 1-3 cho Swiss, 4 cho Final.</summary>
    [Range(1, 4)]
    public int RoundNumber { get; set; }

    /// <summary>Danh sách các bàn đấu do manager sắp xếp.</summary>
    [Required]
    [MinLength(1)]
    public List<ManualPairingDto> Pairings { get; set; } = new();
}

/// <summary>
/// Response preview/save manual pairings.
/// </summary>
public class RoundPairingsResponseDto
{
    public Guid TournamentId { get; set; }
    public int RoundNumber { get; set; }
    public string Source { get; set; } = string.Empty; // "Manual" hoặc "Auto (suggested)"
    public List<ManualPairingDto> Pairings { get; set; } = new();

    /// <summary>Cảnh báo nếu có (vd: "Số người không chia hết cho 4, bàn 3 chỉ có 1 người").</summary>
    public List<string> Warnings { get; set; } = new();
}