using BoardVerse.Core.DTOs.Pos;

namespace BoardVerse.Services.IServices
{
    public interface ICafePosService
    {
        Task<IReadOnlyList<CafeTableStatusDto>> GetTablesAsync(Guid cafeId, Guid userId, string userRole);
        Task SyncTablesAsync(Guid cafeId, Guid managerId, IReadOnlyList<string> tableNames);
        Task<IReadOnlyList<CafeInventoryBoxDto>> GetBoxesAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid? gameTemplateId);
        Task<CafeInventoryBoxDto> GetBoxByBarcodeAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            string barcode);
        Task<IReadOnlyList<ActiveSessionDto>> GetActiveSessionsAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid? gameTemplateId);
        Task<ActiveSessionDto> StartGameSessionAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            StartGameSessionRequestDto request);

        /// <summary>
        /// Host-led check-in: Quét một lần mã đặt chỗ để kích hoạt phiên chơi cho cả nhóm.
        /// </summary>
        Task<ActiveSessionDto> StartSessionFromBookingAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            StartSessionFromBookingRequestDto request);

        Task<ActiveSessionDto> EndGameSessionAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId);

        // BR-12: Component Checklist
        Task<ComponentChecklistDto> GetComponentChecklistAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionGameId);
        Task<ComponentChecklistDto> SubmitComponentCheckAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            SubmitComponentCheckRequestDto request);
    }
}
