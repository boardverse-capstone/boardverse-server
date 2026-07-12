using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface IPaymentMasterAccountRepository
    {
        Task<PaymentMasterAccount?> GetActiveAsync();
        Task<PaymentMasterAccount?> GetByIdAsync(Guid id);
        Task<IReadOnlyList<PaymentMasterAccount>> GetAllAsync();
        Task AddAsync(PaymentMasterAccount masterAccount);
        Task UpdateAsync(PaymentMasterAccount masterAccount);
        Task DeleteAsync(Guid id);
        Task SaveChangesAsync();
    }
}
