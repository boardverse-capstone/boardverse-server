using System.Text.Json;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Lobby;

/// <summary>
/// Tạo yêu cầu ghép nhóm (Lobby Merge Request).
/// Staff POS gọi khi scan mã member A3 muốn nhập vào Nhóm B đang active.
/// Exception Path §4 — boardverse-business-context.mdc.
/// </summary>
public class CreateLobbyMergeRequestDto
{
    /// <summary>Lobby nguồn — lobby mà member muốn rời (Nhóm A).</summary>
    public Guid SourceLobbyId { get; set; }

    /// <summary>Lobby đích — lobby đang hoạt động mà member muốn nhập vào (Nhóm B).</summary>
    public Guid TargetLobbyId { get; set; }

    /// <summary>Lý do ghép nhóm (optional, max 500 ký tự).</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Idempotency key để tránh duplicate khi staff bấm nhiều lần.
    /// Format đề xuất: MERGE-{memberId:N}-{timestampMs}.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Danh sách member cụ thể sẽ được chuyển sang lobby đích (optional).
    /// <para>
    /// Semantics:
    /// <list type="bullet">
    /// <item><c>null</c> hoặc empty → <b>transfer TẤT CẢ</b> active members của source lobby
    /// (giữ nguyên behavior cũ, backward compatible).</item>
    /// <item>Có giá trị → chỉ chuyển những member có ID nằm trong list.</item>
    /// </list>
    /// </para>
    /// <para>
    /// ID semantics (phụ thuộc vào loại source lobby):
    /// <list type="bullet">
    /// <item>Online source (<c>SourceLobby.ReservationId != null</c>): mỗi Guid là
    /// <c>LobbyMember.Id</c>.</item>
    /// <item>Walk-in source (<c>SourceLobby.ReservationId == null</c>): mỗi Guid là
    /// <c>ActiveSessionMember.Id</c> (bao gồm cả guest slot).</item>
    /// </list>
    /// </para>
    /// <para>
    /// Khi <c>SelectedMemberIds</c> được cung cấp, hệ thống sẽ:
    /// <list type="number">
    /// <item>Validate tất cả ID đều là member active hiện tại của source lobby.</item>
    /// <item>Tính lại <c>CombinedCount</c> dựa trên số selected (không phải tổng).</item>
    /// <item>Re-check seat availability với số selected thay vì tổng.</item>
    /// <item>Filter BR-USER-LIMIT-02/03 chỉ cho các member được chọn.</item>
    /// <item>Chỉ transfer các member trong list; các member còn lại ở nguyên source lobby
    /// (không bị dissolve trừ khi source rỗng sau transfer).</item>
    /// </list>
    /// </para>
    /// <para>
    /// Dùng khi staff muốn chỉ chuyển 1-2 người cụ thể, ví dụ:
    /// A1, A2 về sớm + A3 muốn ghép Nhóm B, A4 ở lại Nhóm A.
    /// </para>
    /// </summary>
    public List<Guid>? SelectedMemberIds { get; set; }
}

/// <summary>
/// Duyệt / từ chối yêu cầu ghép nhóm.
/// </summary>
public class ReviewLobbyMergeRequestDto
{
    /// <summary>
    /// Staff ghi chú khi duyệt hoặc từ chối (optional, max 500 ký tự).
    /// </summary>
    public string? ReviewNote { get; set; }
}

/// <summary>DTO trả về cho lobby merge request.</summary>
public class LobbyMergeRequestDto
{
    public Guid Id { get; set; }
    public Guid SourceLobbyId { get; set; }
    public Guid TargetLobbyId { get; set; }
    public string SourceLobbyName { get; set; } = string.Empty;
    public string TargetLobbyName { get; set; } = string.Empty;
    public Guid RequestedByUserId { get; set; }
    public string RequestedByUserName { get; set; } = string.Empty;
    public LobbyMergeRequestStatus Status { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public int SourceMembersCount { get; set; }
    public int SourceActiveMembersAtRequest { get; set; }
    public string? Reason { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewedByUserName { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Tổng số người sau khi ghép (target + source).</summary>
    public int CombinedCount { get; set; }

    /// <summary>Sức chứa tối đa của bàn/café tại thời điểm tạo request.</summary>
    public int SeatCapacity { get; set; }

    /// <summary>True nếu CombinedCount <= SeatCapacity.</summary>
    public bool FitsCapacity { get; set; }

    /// <summary>
    /// Danh sách member cụ thể sẽ được chuyển sang lobby đích (null = transfer all).
    /// Xem <see cref="CreateLobbyMergeRequestDto.SelectedMemberIds"/> cho semantics.
    /// </summary>
    public List<Guid>? SelectedMemberIds { get; set; }
}

/// <summary>
/// DTO trả về sau khi approve ghép nhóm thành công.
/// </summary>
public class LobbyMergeApprovedDto
{
    /// <summary>Mã yêu cầu ghép đã duyệt.</summary>
    public Guid MergeRequestId { get; set; }

    /// <summary>Lobby nguồn (Nhóm A) sau khi merge.</summary>
    public Guid SourceLobbyId { get; set; }

    /// <summary>Lobby đích (Nhóm B) sau khi merge.</summary>
    public Guid TargetLobbyId { get; set; }

    /// <summary>Tổng số member đã chuyển từ lobby nguồn sang lobby đích.</summary>
    public int MembersTransferred { get; set; }

    /// <summary>
    /// Danh sách ID các member thực sự được chuyển sang lobby đích.
    /// Rỗng nếu merge không có SelectedMemberIds (transfer all) — để tránh response DTO quá to
    /// với case 30+ người. Khi SelectedMemberIds được truyền, trả về chính xác list đã chọn
    /// (đã được validate) cho audit và UI confirmation.
    /// </summary>
    public List<Guid> TransferredMemberIds { get; set; } = new();

    /// <summary>ActiveSession của lobby đích sau khi merge.</summary>
    public Guid TargetActiveSessionId { get; set; }

    /// <summary>Idempotency key đã dùng.</summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// DTO trả về audit log của merge.
/// </summary>
public class LobbyMergeAuditLogDto
{
    public Guid Id { get; set; }
    public Guid? MergeRequestId { get; set; }
    public Guid? SourceLobbyId { get; set; }
    public Guid? TargetLobbyId { get; set; }
    public Guid PerformedByUserId { get; set; }
    public string PerformedByUserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Metadata { get; set; }
    public bool? Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}
