namespace BoardVerse.Core.Entities;

/// <summary>
/// Loại report lobby.
/// </summary>
public enum LobbyReportCategory
{
    Spam = 0,
    InappropriateContent = 1,
    Scam = 2,
    FakeCafe = 3,
    Other = 99
}

/// <summary>
/// Báo cáo vi phạm lobby do user gửi.
/// BR-LOBBY-REPORT-01: Reporter không thể report lobby mình đang làm Host.
/// BR-LOBBY-REPORT-02: 1 (ReporterId, LobbyId) chỉ có 1 Pending report.
/// </summary>
public class LobbyReport
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ReporterId { get; set; }
    public Guid LobbyId { get; set; }

    public LobbyReportCategory Category { get; set; } = LobbyReportCategory.Other;

    /// <summary>Lý do chi tiết (5-1000 ký tự).</summary>
    public string Reason { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending";

    public Guid? ReviewedByAdminId { get; set; }
    public string? AdminNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }

    public virtual User Reporter { get; set; } = null!;
    public virtual Lobby Lobby { get; set; } = null!;
}
