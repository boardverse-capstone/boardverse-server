using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Service xử lý ghép nhóm lobby (Lobby Merge).
/// Theo Exception Path §4 — boardverse-business-context.mdc.
///
/// Kịch bản: Nhóm A gồm A1, A2, A3, A4 đang chơi. A1, A2 về sớm.
/// A3 muốn chuyển sang Nhóm B (đang active tại quán).
/// Staff quét mã A3 → tạo LobbyMergeRequest → duyệt → A3 được ghép vào Nhóm B.
///
/// BR-REQUIRED §17.5: Atomic transaction cho mọi thao tác ghi.
/// BR-REQUIRED §17.6: Append-only audit log (LobbyMergeAuditLog).
/// BR-REQUIRED §17.1: Idempotency key cho CreateMergeRequestAsync.
/// </summary>
public interface ILobbyMergeService
{
    /// <summary>
    /// Tạo yêu cầu ghép nhóm.
    /// Staff POS gọi khi scan mã member A3 muốn nhập vào Nhóm B đang active.
    /// Tự động set ExpiresAt = now + 15 phút.
    /// BR-REQUIRED §17.1: Idempotency key tránh duplicate.
    /// </summary>
    /// <param name="cafeId">Mã cafe đang vận hành (để validate quyền staff).</param>
    /// <param name="staffUserId">UserId của nhân viên/manager thực hiện.</param>
    /// <param name="request">Thông tin yêu cầu ghép.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>LobbyMergeRequestDto đã tạo.</returns>
    Task<LobbyMergeRequestDto> CreateMergeRequestAsync(
        Guid cafeId,
        Guid staffUserId,
        CreateLobbyMergeRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Staff/Manager duyệt yêu cầu ghép nhóm — thực hiện ghép member vào lobby đích.
    /// Thực hiện atomic:
    /// 1. Update LobbyMember.LeftReason = MergedIntoAnotherLobby
    /// 2. Link ActiveSessionMember sang ActiveSession đích (nếu có)
    /// 3. Insert ActiveSessionLobbySource audit trail
    /// 4. Insert LobbyMergeAuditLog
    /// 5. Nếu lobby nguồn không còn member nào active → tự động dissolve lobby nguồn
    /// BR-REQUIRED §17.4 + §17.5: Atomic transaction.
    /// BR-DEMO-02: DemoGuard bypasses BR-USER-LIMIT-02 (schedule overlap) and
    /// BR-USER-LIMIT-03 (deposit cap) when demo mode is active.
    /// </summary>
    /// <param name="cafeId">Mã cafe đang vận hành.</param>
    /// <param name="staffUserId">UserId của nhân viên/manager duyệt.</param>
    /// <param name="requestId">Mã yêu cầu ghép.</param>
    /// <param name="reviewNote">Ghi chú của staff (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>LobbyMergeApprovedDto kèm chi tiết merge.</returns>
    Task<LobbyMergeApprovedDto> ApproveMergeAsync(
        Guid cafeId,
        Guid staffUserId,
        Guid requestId,
        string? reviewNote = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Staff/Manager từ chối yêu cầu ghép nhóm.
    /// Cập nhật trạng thái = Rejected, ghi audit log.
    /// Không thay đổi lobby hay session.
    /// </summary>
    Task<LobbyMergeRequestDto> RejectMergeAsync(
        Guid cafeId,
        Guid staffUserId,
        Guid requestId,
        ReviewLobbyMergeRequestDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy chi tiết một yêu cầu ghép nhóm.
    /// </summary>
    Task<LobbyMergeRequestDto?> GetMergeRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách yêu cầu ghép nhóm đang chờ (status = Pending) của một cafe.
    /// </summary>
    /// <param name="cafeId">Mã cafe.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<LobbyMergeRequestDto>> GetPendingRequestsAsync(
        Guid cafeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách tất cả yêu cầu ghép nhóm của một lobby (bất kể trạng thái).
    /// Dùng cho: GET /api/v1/lobbies/{lobbyId}/merge-requests.
    /// </summary>
    Task<IReadOnlyList<LobbyMergeRequestDto>> GetRequestsByLobbyAsync(
        Guid lobbyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy lịch sử merge của một lobby (audit log).
    /// </summary>
    /// <param name="lobbyId">Mã lobby.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<LobbyMergeAuditLogDto>> GetLobbyMergeHistoryAsync(
        Guid lobbyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hủy yêu cầu ghép nhóm (bởi người gửi hoặc staff).
    /// Chỉ hủy được khi status = Pending.
    /// </summary>
    Task<LobbyMergeRequestDto> CancelMergeRequestAsync(
        Guid cafeId,
        Guid userId,
        Guid requestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Background job: đánh dấu các yêu cầu ghép đã hết hạn (Pending + ExpiresAt < now).
    /// Chạy định kỳ mỗi 1-5 phút.
    /// </summary>
    Task<int> ExpireOverdueRequestsAsync(CancellationToken cancellationToken = default);
}
