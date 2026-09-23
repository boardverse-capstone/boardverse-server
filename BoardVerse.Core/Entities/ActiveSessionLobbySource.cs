using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Theo dõi nguồn lobby góp vào một ActiveSession khi có ghép nhóm (merge).
///
/// Khi A3 nhảy từ Nhóm A sang Nhóm B:
/// - Nhóm A (ActiveSession cũ): session vẫn chạy, A3 được ghi nhận đã rời → Member.LeftAt
/// - Nhóm B (ActiveSession mới): thêm 1 row vào ActiveSessionLobbySources để trace nguồn lobby
///
/// Dùng để:
/// - Audit trail: trace đầy đủ lịch sử ghép nhóm
/// - Deposit settlement: xác định BVC deposit của lobby nào đã capture/forfeit
/// - Karma aggregation: xác định source lobby để ghi nhận Karma
/// </summary>
public class ActiveSessionLobbySource
{
    public Guid Id { get; set; }

    /// <summary>ActiveSession mà lobby này góp vào.</summary>
    public Guid ActiveSessionId { get; set; }

    /// <summary>Lobby nguồn.</summary>
    public Guid LobbyId { get; set; }

    /// <summary>Reservation gốc của lobby (nullable).</summary>
    public Guid? ReservationId { get; set; }

    /// <summary>User thực hiện ghép (staff/host).</summary>
    public Guid? MergedByUserId { get; set; }

    /// <summary>Thời điểm lobby được ghép vào session.</summary>
    public DateTime MergedAt { get; set; }

    /// <summary>
    /// True nếu lobby nguồn đã bị dissolved (không còn active) sau khi merge.
    /// </summary>
    public bool SourceDissolved { get; set; }

    /// <summary>Thời điểm lobby nguồn bị dissolved.</summary>
    public DateTime? SourceDissolvedAt { get; set; }

    /// <summary>
    /// Trạng thái deposit của reservation tại thời điểm merge.
    /// Dùng để xác định deposit có được capture, release hay forfeit sau khi merge.
    /// </summary>
    public BookingDepositStatus DepositStatusAtMerge { get; set; }

    // === Audit ===
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // === Navigation ===
    public virtual ActiveSession ActiveSession { get; set; } = null!;
    public virtual Lobby Lobby { get; set; } = null!;
    public virtual Reservation? Reservation { get; set; }
    public virtual User? MergedByUser { get; set; }
}
