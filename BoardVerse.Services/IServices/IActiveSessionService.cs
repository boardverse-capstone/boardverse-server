using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

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
        Task<ActiveSessionResponseDto> ResumeSessionAsync(Guid cafeId, Guid sessionId, CancellationToken ct = default);

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
    }
}