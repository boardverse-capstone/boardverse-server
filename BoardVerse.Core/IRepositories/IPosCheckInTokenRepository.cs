using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho POS check-in token (BR §21A.7 — 2 chiều).
/// Token 16-char alphanumeric, unique trong DB. TTL 30 phút.
/// </summary>
public interface IPosCheckInTokenRepository
{
    Task<PosCheckInToken?> GetByIdAsync(Guid id);

    Task<PosCheckInToken?> GetByTokenAsync(string token);

    Task<List<PosCheckInToken>> GetActiveTokensForCafeAsync(Guid cafeId);

    Task AddAsync(PosCheckInToken token);

    Task<bool> TokenExistsAsync(string token);
}