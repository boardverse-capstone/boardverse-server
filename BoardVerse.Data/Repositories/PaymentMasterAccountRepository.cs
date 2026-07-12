using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class PaymentMasterAccountRepository : IPaymentMasterAccountRepository
    {
        private readonly BoardVerseDbContext _db;

        public PaymentMasterAccountRepository(BoardVerseDbContext db)
        {
            _db = db;
        }

        public async Task<PaymentMasterAccount?> GetActiveAsync()
        {
            return await _db.PaymentMasterAccounts
                .FirstOrDefaultAsync(m => m.IsActive);
        }

        public async Task<PaymentMasterAccount?> GetByIdAsync(Guid id)
        {
            return await _db.PaymentMasterAccounts.FindAsync(id);
        }

        public async Task<IReadOnlyList<PaymentMasterAccount>> GetAllAsync()
        {
            return await _db.PaymentMasterAccounts.ToListAsync();
        }

        public Task AddAsync(PaymentMasterAccount masterAccount)
        {
            _db.PaymentMasterAccounts.Add(masterAccount);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(PaymentMasterAccount masterAccount)
        {
            _db.PaymentMasterAccounts.Update(masterAccount);
            return Task.CompletedTask;
        }

        public async Task DeleteAsync(Guid id)
        {
            var account = await _db.PaymentMasterAccounts.FindAsync(id);
            if (account != null)
            {
                _db.PaymentMasterAccounts.Remove(account);
            }
        }

        public Task SaveChangesAsync()
        {
            return _db.SaveChangesAsync();
        }
    }
}
