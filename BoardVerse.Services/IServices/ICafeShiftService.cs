using BoardVerse.Core.DTOs.CafeShift;
using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices;

public interface ICafeShiftService
{
    Task<CafeShiftResponseDto> OpenShiftAsync(Guid cafeId, Guid userId, decimal openingCashBalance);
    Task<CafeShiftResponseDto> CloseShiftAsync(Guid shiftId, Guid userId, decimal closingCashBalance);
    // P0-Fix-#6: truyền callerUserId để service validate ownership (Manager/CafeStaff phải thuộc cafe).
    Task<CafeShiftResponseDto?> GetCurrentShiftAsync(Guid cafeId, Guid callerUserId, bool isAdmin);
    Task<CafeShiftHistoryResponseDto> GetShiftHistoryAsync(Guid cafeId, int page, int pageSize, Guid callerUserId, bool isAdmin);
}
