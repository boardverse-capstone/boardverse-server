using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Audit log cho mọi thao tác ghép nhóm (Lobby Merge).
/// Append-only: không UPDATE/DELETE.
/// Dùng để trace lịch sử, debug, và compliance.
/// </summary>
public class LobbyMergeAuditLog
{
    public Guid Id { get; set; }

    /// <summary>Yêu cầu ghép nhóm liên quan.</summary>
    public Guid? MergeRequestId { get; set; }

    /// <summary>Lobby nguồn.</summary>
    public Guid? SourceLobbyId { get; set; }

    /// <summary>Lobby đích.</summary>
    public Guid? TargetLobbyId { get; set; }

    /// <summary>Reservation nguồn.</summary>
    public Guid? SourceReservationId { get; set; }

    /// <summary>Reservation đích.</summary>
    public Guid? TargetReservationId { get; set; }

    /// <summary>Active session nguồn.</summary>
    public Guid? SourceActiveSessionId { get; set; }

    /// <summary>Active session đích.</summary>
    public Guid? TargetActiveSessionId { get; set; }

    /// <summary>User thực hiện thao tác (staff/admin/system).</summary>
    public Guid PerformedByUserId { get; set; }

    /// <summary>
    /// Action thực hiện.
    /// Giá trị: MergeRequested, MergeApproved, MergeRejected, MergeExpired,
    /// MemberTransferred, SessionLinked, ReservationAbsorbed, etc.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Metadata bổ sung (JSON).
    /// Ví dụ: { "memberIds": [...], "depositRefunded": true, "previousLobbyId": "..." }
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>True nếu action thành công.</summary>
    public bool? Success { get; set; }

    /// <summary>Lỗi chi tiết nếu thất bại.</summary>
    public string? ErrorMessage { get; set; }

    // === Audit ===
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // === Navigation ===
    public virtual Lobby? SourceLobby { get; set; }
    public virtual Lobby? TargetLobby { get; set; }
    public virtual Reservation? SourceReservation { get; set; }
    public virtual Reservation? TargetReservation { get; set; }
    public virtual ActiveSession? SourceActiveSession { get; set; }
    public virtual ActiveSession? TargetActiveSession { get; set; }
    public virtual User PerformedByUser { get; set; } = null!;
    public virtual LobbyMergeRequest? MergeRequest { get; set; }
}
