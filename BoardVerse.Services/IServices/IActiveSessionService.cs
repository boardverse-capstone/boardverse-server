using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Services.IServices
{
    public interface IActiveSessionService
    {
        Task<ActiveSessionResponseDto> StartSessionAsync(Guid cafeId, Guid hostUserId, StartSessionRequestDto request, CancellationToken ct = default);
        Task<ActiveSessionResponseDto> CheckoutAsync(Guid cafeId, Guid sessionId, CheckoutRequestDto request, CancellationToken ct = default);
        Task<ActiveSessionResponseDto> AddGuestSlotAsync(Guid cafeId, Guid sessionId, AddGuestSlotRequestDto request, CancellationToken ct = default);
        Task<ActiveSessionResponseDto> EndGameAsync(Guid cafeId, Guid sessionId, CancellationToken ct = default);
        Task<ActiveSessionResponseDto> PartialCheckoutAsync(Guid cafeId, Guid sessionId, PartialCheckoutRequestDto request, CancellationToken ct = default);
        Task<ActiveSessionResponseDto> GetSessionAsync(Guid cafeId, Guid sessionId, CancellationToken ct = default);
        Task<MergeSessionResponseDto> MergeSessionAsync(Guid cafeId, Guid sourceSessionId, MergeSessionRequestDto request, CancellationToken ct = default);
        Task<PaySessionResponseDto> PaySessionAsync(Guid cafeId, Guid sessionId, PaySessionRequestDto request, CancellationToken ct = default);

        /// <summary>
        /// Single source of truth cho việc đóng phiên chơi.
        /// Được gọi từ cả POS (staff bấm tay) lẫn webhook (SePay ping khi nhận tiền QR).
        /// Webhook dùng trigger = SePayWebhook; POS dùng Manual.
        /// Side-effects đầy đủ: capture BVC, release table/box, close lobby,
        /// tạo WalkInWindow (early checkout), build member invoices.
        /// Idempotent: re-check Status == Unpaid trong transaction.
        /// </summary>
        Task<PaySessionResponseDto> PaySessionCoreAsync(Guid cafeId, Guid sessionId, PaySessionRequestDto request, PayTrigger trigger, CancellationToken ct = default);
        Task<ActiveSessionResponseDto> AttachGameAsync(Guid cafeId, Guid sessionId, AttachGameRequestDto request, CancellationToken ct = default);
        Task<ActiveSessionResponseDto> AddLateMemberAsync(Guid cafeId, Guid sessionId, AddLateMemberRequestDto request, CancellationToken ct = default);
        Task RecordInventoryLossAsync(Guid cafeId, Guid userId, Guid sessionId, RecordInventoryLossRequestDto request, CancellationToken ct = default);
        Task<AlternativeCafesResponseDto> GetAlternativeCafesAsync(Guid excludeCafeId, Guid gameTemplateId, int memberCount, DateTime scheduledTime, CancellationToken ct = default);

        /// <summary>
        /// Submit component checklist cho 1 game trong phiên chơi (BR-12).
        /// Nhân viên POS scan linh kiện thực tế → tính penalty nếu thiếu/hỏng.
        /// </summary>
        Task<ActiveSessionResponseDto> SubmitComponentCheckAsync(Guid cafeId, Guid sessionId, SubmitComponentCheckRequestDto request, CancellationToken ct = default);

        /// <summary>
        /// GAP-1 Fix: Cho phép revert từ CHECKING về ACTIVE nếu nhân viên bấm nhầm.
        /// Chỉ cho phép khi chưa có thành viên nào được checkout (chưa có member trong trạng thái FINISHED).
        /// </summary>
        Task<ActiveSessionResponseDto> ResumeSessionAsync(Guid cafeId, Guid staffUserId, Guid sessionId, CancellationToken ct = default);

        /// <summary>
        /// L-05: Tiếp tục phiên đang bị tạm dừng.
        /// Chỉ hoạt động khi phiên đang ACTIVE và IsPaused = true.
        /// </summary>
        Task<ActiveSessionResponseDto> ResumeFromPauseAsync(Guid cafeId, Guid sessionId, CancellationToken ct = default);

        /// <summary>
        /// L-05: Tạm dừng phiên chơi — timer không đếm.
        /// Chỉ áp dụng khi phiên đang ACTIVE.
        /// </summary>
        Task<ActiveSessionResponseDto> PauseSessionAsync(Guid cafeId, Guid sessionId, CancellationToken ct = default);

        // ============ PLAYER-FACING APIs ============

        /// <summary>
        /// Player xem phiên chơi hiện tại của mình.
        /// GET /api/v1/sessions/me/current
        /// </summary>
        Task<GetCurrentSessionResponseDto?> GetCurrentSessionAsync(Guid userId, CancellationToken ct = default);

        /// <summary>
        /// Player gia hạn thêm thời gian chơi.
        /// POST /api/v1/sessions/me/extend
        /// </summary>
        Task<ExtendSessionResponseDto> ExtendSessionAsync(Guid userId, int extensionMinutes, CancellationToken ct = default);

        /// <summary>
        /// Player thanh toán invoice bằng BVC.
        /// POST /api/v1/sessions/me/pay
        /// </summary>
        Task<PlayerPaySessionResponseDto> PlayerPaySessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// GAP-8 + GAP-2 + GAP-7 Fix: Lấy lịch sử phiên đã chơi của player (bao gồm walk-in) + cursor pagination + date range filter.
    /// GET /api/v1/sessions/me/history?limit=20&beforePaidAt=2026-08-01T10:00:00Z&fromDate=2026-08-01&toDate=2026-08-31
    /// </summary>
    /// <param name="fromDate">Filter: chỉ lấy session từ ngày này trở đi (UTC).</param>
    /// <param name="toDate">Filter: chỉ lấy session đến ngày này (UTC).</param>
    Task<IReadOnlyList<SessionHistoryResponseDto>> GetSessionHistoryAsync(
        Guid userId,
        int limit = 20,
        DateTime? beforePaidAt = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

        // ===== POS Extension Request APIs (GAP-NEW-1) =====

        /// <summary>
        /// Lấy danh sách yêu cầu gia hạn đang chờ của một quán.
        /// GET /api/cafes/{cafeId}/pos/extension-requests/pending
        /// </summary>
        Task<IReadOnlyList<PendingExtensionRequestDto>> GetPendingExtensionRequestsAsync(Guid cafeId, CancellationToken ct = default);

        /// <summary>
        /// POS staff duyệt yêu cầu gia hạn — cập nhật session + request status.
        /// POST /api/cafes/{cafeId}/pos/extension-requests/{requestId}/approve
        /// </summary>
        Task<ExtensionRequestProcessedDto> ApproveExtensionRequestAsync(
            Guid cafeId,
            Guid staffUserId,
            Guid requestId,
            int approvedMinutes,
            CancellationToken ct = default);

        /// <summary>
        /// POS staff từ chối yêu cầu gia hạn.
        /// POST /api/cafes/{cafeId}/pos/extension-requests/{requestId}/reject
        /// </summary>
        Task<ExtensionRequestProcessedDto> RejectExtensionRequestAsync(
            Guid cafeId,
            Guid staffUserId,
            Guid requestId,
            string? reason,
            CancellationToken ct = default);
    }
}