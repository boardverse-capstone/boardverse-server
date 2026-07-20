namespace BoardVerse.Core.DTOs.Tournament;

/// <summary>
/// Lịch sử Elo của user qua các tournament đã/đang tham gia.
/// Dùng cho biểu đồ line chart và bảng detail ở player UI.
/// </summary>
public class EloHistoryResponseDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public int CurrentElo { get; set; }
    public List<EloHistoryEntryDto> History { get; set; } = new();
}

public class EloHistoryEntryDto
{
    public Guid TournamentId { get; set; }
    public string TournamentTitle { get; set; } = string.Empty;
    public string GameTemplateName { get; set; } = string.Empty;

    /// <summary>StartTime của tournament (dùng làm trục X cho chart).</summary>
    public DateTime TournamentDate { get; set; }

    /// <summary>Elo trước khi tournament bắt đầu.</summary>
    public int EloBefore { get; set; }

    /// <summary>Elo sau khi tournament kết thúc (= InitialElo + EloDelta + winner bonus).</summary>
    public int EloAfter { get; set; }

    /// <summary>Tổng delta (có thể âm, ví dụ: -15).</summary>
    public int EloDelta { get; set; }

    public int? FinalRank { get; set; }
    public string TournamentStatus { get; set; } = string.Empty;
}