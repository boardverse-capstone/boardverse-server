using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface ISePayAccountRepository
    {
        Task<SePayAccount?> GetByIdAsync(Guid id);
        Task<SePayAccount?> GetByCafeIdAsync(Guid cafeId);
        Task<SePayAccount?> GetMasterAccountAsync();
        Task<IReadOnlyList<SePayAccount>> GetAllAsync(SePayAccountQuery? query = null);
        Task AddAsync(SePayAccount account);
        Task UpdateAsync(SePayAccount account);
        Task DeleteAsync(Guid id);
        Task SaveChangesAsync();
    }
}
