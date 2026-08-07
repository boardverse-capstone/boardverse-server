namespace BoardVerse.Core.Entities;

/// <summary>
/// Bảng ghi nhận notification lobby đã gửi — chống gửi trùng (BR-NEW-13).
/// </summary>
public class LobbyNotificationSent
{
    public Guid Id { get; set; }
    public Guid LobbyId { get; set; }
    public LobbyNotificationMilestone Milestone { get; set; }
    public DateTime SentAt { get; set; }

    /// <summary>
    /// User nhận notification. Null nếu gửi broadcast (host + members).
    /// </summary>
    public Guid? RecipientUserId { get; set; }

    public virtual Lobby Lobby { get; set; } = null!;
}

/// <summary>
/// Milestone cho notification lobby (BR-NEW-13).
/// </summary>
public enum LobbyNotificationMilestone
{
    At48hRecruitmentDeadline = 1,
    At24hRecruitmentDeadline = 2,
    At2hPreferredStart = 3,
    At30mPreferredStart = 4
}
