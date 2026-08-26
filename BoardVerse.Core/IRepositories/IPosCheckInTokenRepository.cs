using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho POS check-in token (BR §21A.7 — 2 chiều).
/// Token 16-char alphanumeric, unique trong DB. TTL 30 phút.
/// </summary>
public interface IPosCheckInTokenRepository
{
    Task<PosCheckInToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PosCheckInToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);

    Task<List<PosCheckInToken>> GetActiveTokensForCafeAsync(Guid cafeId, CancellationToken cancellationToken = default);

    Task AddAsync(PosCheckInToken token, CancellationToken cancellationToken = default);

    Task<bool> TokenExistsAsync(string token, CancellationToken cancellationToken = default);
}