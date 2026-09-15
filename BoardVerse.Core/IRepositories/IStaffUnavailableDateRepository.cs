using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho StaffUnavailableDate — ngày staff không thể làm việc.
/// </summary>
public interface IStaffUnavailableDateRepository
{
    Task<StaffUnavailableDate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaffUnavailableDate>> GetByStaffAsync(
        Guid staffUserId,
        Guid cafeId,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken cancellationToken = default);

    Task<bool> IsUnavailableAsync(Guid staffUserId, DateOnly date, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid staffUserId, DateOnly date, CancellationToken cancellationToken = default);
    Task AddAsync(StaffUnavailableDate item, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<StaffUnavailableDate> items, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
