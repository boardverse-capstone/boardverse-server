using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho ShiftSwapRequest — yêu cầu đổi ca giữa 2 staff.
/// </summary>
public interface IShiftSwapRequestRepository
{
    Task<ShiftSwapRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShiftSwapRequest>> GetByCafeAsync(Guid cafeId, ShiftSwapStatus? status = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShiftSwapRequest>> GetPendingForStaffAsync(Guid staffUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShiftSwapRequest>> GetByRequesterAsync(Guid requesterId, CancellationToken cancellationToken = default);
    Task AddAsync(ShiftSwapRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(ShiftSwapRequest request, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
