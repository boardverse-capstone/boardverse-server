namespace BoardVerse.Core.Entities;

/// <summary>
/// Spectator entry cho phép người dùng xem tournament mà không cần đăng ký tham gia.
/// Spectator có thể xem matches nhưng không điều khiển được kết quả.
/// </summary>
public class TournamentSpectator
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Mã giải đấu.</summary>
    public Guid TournamentId { get; set; }

    /// <summary>User xem giải đấu.</summary>
    public Guid UserId { get; set; }

    /// <summary>Thời điểm bắt đầu xem.</summary>
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Thời điểm rời khỏi (null nếu đang xem).</summary>
    public DateTime? LeftAt { get; set; }

    // === Navigation ===
    public virtual Tournament Tournament { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}
