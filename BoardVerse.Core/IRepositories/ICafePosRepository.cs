using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface ICafePosRepository
    {
        Task<bool> CanOperateCafeAsync(Guid cafeId, Guid userId, string userRole);
        Task<IReadOnlyList<CafeTable>> GetActiveTablesAsync(Guid cafeId);
        Task<CafeTable?> GetTableAsync(Guid cafeId, Guid tableId);
        Task<CafeInventoryBox?> GetBoxByBarcodeAsync(Guid cafeId, string barcode);
        Task<IReadOnlyList<CafeInventoryBox>> GetBoxesAsync(Guid cafeId, Guid? gameTemplateId);
        Task<ActiveSession?> GetActiveSessionByIdAsync(Guid cafeId, Guid sessionId);
        Task<ActiveSession?> GetActiveSessionByBoxIdAsync(Guid boxId);
        Task<IReadOnlyList<ActiveSession>> GetActiveSessionsAsync(Guid cafeId, Guid? gameTemplateId);
        Task AddSessionAsync(ActiveSession session);
        Task SaveChangesAsync();
    }
}
