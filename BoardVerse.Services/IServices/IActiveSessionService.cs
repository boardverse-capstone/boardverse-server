using BoardVerse.Core.DTOs.Pos;
using BoardVerse.Core.DTOs.Session;
using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices
{
    public interface IActiveSessionService
    {
        Task<ActiveSessionResponseDto> StartSessionAsync(Guid cafeId, Guid hostUserId, StartSessionRequestDto request);
        Task<ActiveSessionResponseDto> CheckoutAsync(Guid cafeId, Guid sessionId, CheckoutRequestDto request);
        Task<ActiveSessionResponseDto> AddGuestSlotAsync(Guid cafeId, Guid sessionId, AddGuestSlotRequestDto request);
        Task<ActiveSessionResponseDto> EndGameAsync(Guid cafeId, Guid sessionId);
        Task<ActiveSessionResponseDto> PartialCheckoutAsync(Guid cafeId, Guid sessionId, PartialCheckoutRequestDto request);
        Task<ActiveSessionResponseDto> GetSessionAsync(Guid cafeId, Guid sessionId);
        Task<MergeSessionResponseDto> MergeSessionAsync(Guid cafeId, Guid sourceSessionId, MergeSessionRequestDto request);
        Task<PaySessionResponseDto> PaySessionAsync(Guid cafeId, Guid sessionId, PaySessionRequestDto request);
        Task<ActiveSessionResponseDto> AttachGameAsync(Guid cafeId, Guid sessionId, AttachGameRequestDto request);
        Task<ActiveSessionResponseDto> AddLateMemberAsync(Guid cafeId, Guid sessionId, AddLateMemberRequestDto request);
        Task RecordInventoryLossAsync(Guid cafeId, Guid userId, Guid sessionId, RecordInventoryLossRequestDto request);
        Task<AlternativeCafesResponseDto> GetAlternativeCafesAsync(Guid excludeCafeId, Guid gameTemplateId, int memberCount, DateTime scheduledTime);

        /// <summary>
        /// Submit component checklist cho 1 game trong phiên chơi (BR-12).
        /// Nhân viên POS scan linh kiện thực tế → tính penalty nếu thiếu/hỏng.
        /// </summary>
        Task<ActiveSessionResponseDto> SubmitComponentCheckAsync(Guid cafeId, Guid sessionId, SubmitComponentCheckRequestDto request);
    }
}