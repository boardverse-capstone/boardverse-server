using BoardVerse.Core.DTOs.CafeShift;
using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Services.IServices;

public interface ICafeShiftService
{
    Task<CafeShiftResponseDto> OpenShiftAsync(Guid cafeId, Guid userId, decimal openingCashBalance, CancellationToken cancellationToken = default);
    Task<CafeShiftResponseDto> CloseShiftAsync(Guid shiftId, Guid userId, decimal closingCashBalance, CancellationToken cancellationToken = default);
    // P0-Fix-#6: truyền callerUserId để service validate ownership (Manager/CafeStaff phải thuộc cafe).
    Task<CafeShiftResponseDto?> GetCurrentShiftAsync(Guid cafeId, Guid callerUserId, bool isAdmin, CancellationToken cancellationToken = default);
    Task<CafeShiftHistoryResponseDto> GetShiftHistoryAsync(Guid cafeId, int page, int pageSize, Guid callerUserId, bool isAdmin, CancellationToken cancellationToken = default);
}
