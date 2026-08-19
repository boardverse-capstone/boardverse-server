using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// BR-KARMA-01 §4.3 + §9.6: Repository cho <see cref="KarmaShortPlayRecord"/>.
/// </summary>
public interface IKarmaShortPlayRecordRepository
{
    Task<KarmaShortPlayRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<KarmaShortPlayRecord?> GetByReservationAndUserAsync(Guid reservationId, Guid userId, CancellationToken ct = default);
    Task<KarmaShortPlayRecord?> GetLatestByUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>BR-KARMA-02/03: Đếm số record ACTIVE của user (dùng để trigger warning/restriction).</summary>
    Task<int> GetActiveCountByUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>BR-KARMA-04: Đánh Expired các record cũ hơn cutoff.</summary>
    Task<int> ExpireOldRecordsAsync(DateTime cutoff, CancellationToken ct = default);

    Task AddAsync(KarmaShortPlayRecord record, CancellationToken ct = default);
    Task UpdateAsync(KarmaShortPlayRecord record, CancellationToken ct = default);
}