using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface ISePayAccountRepository
    {
        Task<SePayAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<SePayAccount?> GetByCafeIdAsync(Guid cafeId, CancellationToken cancellationToken = default);
        Task<SePayAccount?> GetMasterAccountAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SePayAccount>> GetAllAsync(SePayAccountQuery? query = null, CancellationToken cancellationToken = default);
        Task AddAsync(SePayAccount account, CancellationToken cancellationToken = default);
        Task UpdateAsync(SePayAccount account, CancellationToken cancellationToken = default);
        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
