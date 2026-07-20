namespace BoardVerse.Core.DTOs.Tournament;

/// <summary>
/// Kết quả của auto no-show detection job.
/// </summary>
public class NoShowDetectionResult
{
    /// <summary>
    /// TournamentId đã được xử lý. Null nếu không có tournament nào cần xử lý.
    /// </summary>
    public Guid? TournamentId { get; set; }

    /// <summary>
    /// Tổng số participants được đánh dấu no-show.
    /// </summary>
    public int TotalMarked { get; set; }

    /// <summary>
    /// Tổng số Karma penalty đã áp dụng.
    /// </summary>
    public int TotalKarmaPenalty { get; set; }

    /// <summary>
    /// Danh sách participant Ids đã được đánh dấu no-show.
    /// </summary>
    public IReadOnlyList<Guid> MarkedParticipantIds { get; set; } = [];
}
