using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Yêu cầu ghép nhóm (Lobby Merge Request).
/// Theo Exception Path §4 trong boardverse-business-context.mdc.
///
/// Tạo khi staff quét mã member A3 muốn nhập vào Nhóm B:
/// - SourceLobbyId = lobby cũ của A3 (Nhóm A)
/// - TargetLobbyId = lobby đang hoạt động của Nhóm B
/// - Status = Pending → Approved/Rejected/Expired/Cancelled
/// </summary>
public class LobbyMergeRequest
{
    public Guid Id { get; set; }

    /// <summary>
    /// Lobby nguồn — lobby mà member muốn rời khỏi.
    /// </summary>
    public Guid SourceLobbyId { get; set; }

    /// <summary>
    /// Lobby đích — lobby mà member muốn nhập vào.
    /// </summary>
    public Guid TargetLobbyId { get; set; }

    /// <summary>
    /// UserId của người thực hiện yêu cầu (thường là staff tại POS).
    /// </summary>
    public Guid RequestedByUserId { get; set; }

    /// <summary>Trạng thái yêu cầu ghép.</summary>
    public LobbyMergeRequestStatus Status { get; set; } = LobbyMergeRequestStatus.Pending;

    /// <summary>
    /// Tổng số thành viên trong lobby nguồn tại thời điểm tạo request.
    /// Dùng để audit và xác nhận không có thành viên "mất tích" khi merge.
    /// </summary>
    public int SourceMembersCount { get; set; }

    /// <summary>
    /// Số thành viên đang active (chưa checkout) trong lobby nguồn tại thời điểm tạo request.
    /// </summary>
    public int SourceActiveMembersAtRequest { get; set; }

    /// <summary>
    /// Lý do ghép nhóm (optional, max 500 ký tự).
    /// Ví dụ: "Nhóm A về sớm, A3 muốn chuyển sang Nhóm B".
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Staff/admin đã xử lý yêu cầu (Approved/Rejected).
    /// </summary>
    public Guid? ReviewedByUserId { get; set; }

    /// <summary>Thời điểm staff xử lý.</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>
    /// Ghi chú của staff khi duyệt/từ chối (optional).
    /// </summary>
    public string? ReviewNote { get; set; }

    /// <summary>
    /// Thời điểm yêu cầu hết hạn (mặc định +15 phút từ CreatedAt).
    /// Quá hạn mà chưa xử lý → tự động chuyển Status = Expired.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Idempotency key để tránh tạo duplicate request khi staff bấm nhiều lần.
    /// Format: MERGE-{lobbySourceId:N}-{userId:N}-{timestampMs}.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Tổng số người sau khi ghép (target current members + source active members),
    /// tính tại thời điểm tạo request.
    /// </summary>
    public int CombinedCount { get; set; }

    /// <summary>
    /// Sức chứa tối đa của bàn/café tại thời điểm tạo request.
    /// </summary>
    public int SeatCapacity { get; set; }

    /// <summary>
    /// True nếu CombinedCount <= SeatCapacity tại thời điểm tạo request.
    /// </summary>
    public bool FitsCapacity { get; set; }

    // === Audit ===
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // === Navigation ===
    /// <summary>Lobby nguồn.</summary>
    public virtual Lobby SourceLobby { get; set; } = null!;

    /// <summary>Lobby đích.</summary>
    public virtual Lobby TargetLobby { get; set; } = null!;

    /// <summary>User thực hiện request.</summary>
    public virtual User RequestedByUser { get; set; } = null!;

    /// <summary>Staff/admin duyệt request.</summary>
    public virtual User? ReviewedByUser { get; set; }
}
