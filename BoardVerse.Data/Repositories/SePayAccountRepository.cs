using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class SePayAccountRepository : ISePayAccountRepository
    {
        private readonly BoardVerseDbContext _db;

        public SePayAccountRepository(BoardVerseDbContext db)
        {
            _db = db;
        }

        public async Task<SePayAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _db.SePayAccounts
                .Include(x => x.Cafe)
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<SePayAccount?> GetByCafeIdAsync(Guid cafeId, CancellationToken cancellationToken = default)
        {
            return await _db.SePayAccounts
                .FirstOrDefaultAsync(x => x.CafeId == cafeId && x.AccountType == SePayAccountType.Cafe);
        }

        public async Task<SePayAccount?> GetMasterAccountAsync(CancellationToken cancellationToken = default)
        {
            return await _db.SePayAccounts
                .FirstOrDefaultAsync(x => x.AccountType == SePayAccountType.Master && x.IsActive);
        }

        /// <summary>
        /// SePay Personal per-cafe routing: match BankCode + AccountNumber từ webhook payload.
        /// - BankCode so khớp case-insensitive, chấp nhận alias phổ biến
        ///   (SePay gửi "MBBank" / "Vietcombank" / "ACB" v.v.; BoardVerse store "MB" / "VCB" / "ACB").
        /// - AccountNumber so khớp exact (sau khi trim + bỏ khoảng trắng).
        /// </summary>
        public async Task<SePayAccount?> GetCafeAccountByBankInfoAsync(
            string bankCode,
            string accountNumber,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(bankCode) || string.IsNullOrWhiteSpace(accountNumber))
            {
                return null;
            }

            var normalizedBank = NormalizeBankCode(bankCode);
            var normalizedAccount = accountNumber.Trim().Replace(" ", string.Empty);

            // Lấy tất cả cafe account active (số lượng nhỏ — mỗi cafe 1 account).
            var candidates = await _db.SePayAccounts
                .Where(x => x.AccountType == SePayAccountType.Cafe && x.IsActive)
                .ToListAsync(cancellationToken);

            return candidates.FirstOrDefault(x =>
                x.AccountNumber != null
                && x.AccountNumber.Trim().Replace(" ", string.Empty) == normalizedAccount
                && NormalizeBankCode(x.BankCode) == normalizedBank);
        }

        private static string NormalizeBankCode(string? bankCode)
        {
            if (string.IsNullOrWhiteSpace(bankCode)) return string.Empty;
            var normalized = bankCode.Trim().ToUpperInvariant();
            // Strip "BANK" suffix: "MBBank" → "MB", "Vietcombank" → "VIETCOM".
            if (normalized.EndsWith("BANK", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(0, normalized.Length - 4);
            }
            return normalized;
        }

        public async Task<IReadOnlyList<SePayAccount>> GetAllAsync(SePayAccountQuery? query = null, CancellationToken cancellationToken = default)
        {
            var queryable = _db.SePayAccounts
                .Include(x => x.Cafe)
                .AsQueryable();

            if (query != null)
            {
                if (query.AccountType.HasValue)
                    queryable = queryable.Where(x => x.AccountType == query.AccountType.Value);

                if (query.CafeId.HasValue)
                    queryable = queryable.Where(x => x.CafeId == query.CafeId.Value);

                if (query.IsActive.HasValue)
                    queryable = queryable.Where(x => x.IsActive == query.IsActive.Value);
            }

            return await queryable.OrderBy(x => x.AccountType).ThenBy(x => x.CreatedAt).ToListAsync();
        }

        public Task AddAsync(SePayAccount account, CancellationToken cancellationToken = default)
        {
            _db.SePayAccounts.Add(account);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(SePayAccount account, CancellationToken cancellationToken = default)
        {
            account.UpdatedAt = DateTime.UtcNow;
            _db.SePayAccounts.Update(account);
            return Task.CompletedTask;
        }

        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var account = await _db.SePayAccounts.FindAsync(id);
            if (account != null)
            {
                _db.SePayAccounts.Remove(account);
            }
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _db.SaveChangesAsync();
        }
    }
}
