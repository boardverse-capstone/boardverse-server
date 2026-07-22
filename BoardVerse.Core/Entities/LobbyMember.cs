namespace BoardVerse.Core.Entities;

/// <summary>
/// Trạng thái lobby member (readiness cho InProgress).
/// BR-LOBBY-READY-01: Khi lobby FULL, các member phải bấm Ready để chuyển InProgress.
/// BR-LOBBY-READY-02: Nếu sau 5 phút FULL mà chưa đủ Ready → Host có thể cancel.
/// </summary>
public enum LobbyMemberStatus
{
    /// <summary>Member vừa join, chưa sẵn sàng.</summary>
    Joined = 0,

    /// <summary>Member đã bấm Ready, đang chờ các member khác.</summary>
    Ready = 1,

    /// <summary>Member bị Host kick khỏi lobby.</summary>
    Kicked = 2,

    /// <summary>Member tự rời lobby.</summary>
    Left = 3
}

public class LobbyMember
{
    public Guid Id { get; set; }
    public Guid LobbyId { get; set; }
    public Guid UserId { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    /// <summary>Host-led check-in: true nếu đây là người khởi tạo phòng chờ.</summary>
    public bool IsHost { get; set; }

    /// <summary>Trạng thái member: Joined/Ready/Kicked/Left.</summary>
    public LobbyMemberStatus Status { get; set; } = LobbyMemberStatus.Joined;

    /// <summary>Thời điểm member bấm Ready.</summary>
    public DateTime? ReadyAt { get; set; }

    /// <summary>Thời điểm member rời lobby (Left/Kicked).</summary>
    public DateTime? LeftAt { get; set; }

    public virtual Lobby Lobby { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}
