using BoardVerse.Core.DTOs.Pos;

namespace BoardVerse.Services.IServices
{
    public interface ICafePosService
    {
        Task<IReadOnlyList<CafeTableStatusDto>> GetTablesAsync(Guid cafeId, Guid userId, string userRole);
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
        Task<ActiveSessionDto> EndGameSessionAsync(
            Guid cafeId,
            Guid userId,
            string userRole,
            Guid sessionId);
    }
}
