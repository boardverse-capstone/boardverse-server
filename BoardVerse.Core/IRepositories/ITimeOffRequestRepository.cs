using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho TimeOffRequest — yêu cầu nghỉ phép của staff.
/// </summary>
public interface ITimeOffRequestRepository
{
    Task<TimeOffRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TimeOffRequest>> GetByCafeAsync(Guid cafeId, TimeOffStatus? status = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TimeOffRequest>> GetByStaffAsync(Guid staffUserId, CancellationToken cancellationToken = default);
    Task AddAsync(TimeOffRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(TimeOffRequest request, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
