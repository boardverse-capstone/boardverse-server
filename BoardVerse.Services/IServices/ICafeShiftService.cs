using BoardVerse.Core.DTOs.CafeShift;
using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices;

public interface ICafeShiftService
{
    Task<CafeShiftResponseDto> OpenShiftAsync(Guid cafeId, Guid userId, decimal openingCashBalance);
    Task<CafeShiftResponseDto> CloseShiftAsync(Guid shiftId, Guid userId, decimal closingCashBalance);
    Task<CafeShiftResponseDto?> GetCurrentShiftAsync(Guid cafeId);
    Task<CafeShiftHistoryResponseDto> GetShiftHistoryAsync(Guid cafeId, int page, int pageSize);
}
