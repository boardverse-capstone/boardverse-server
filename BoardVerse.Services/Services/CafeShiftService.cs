using BoardVerse.Core.DTOs.CafeShift;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services;

public class CafeShiftService : ICafeShiftService
{
    private readonly ICafeShiftRepository _shiftRepository;
    private readonly ICafeRepository _cafeRepository;

    public CafeShiftService(ICafeShiftRepository shiftRepository, ICafeRepository cafeRepository)
    {
        _shiftRepository = shiftRepository;
        _cafeRepository = cafeRepository;
    }

    public async Task<CafeShiftResponseDto> OpenShiftAsync(Guid cafeId, Guid userId, decimal openingCashBalance)
    {
        var cafe = await _cafeRepository.GetByIdAsync(cafeId);
        if (cafe == null)
            throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        var existingShift = await _shiftRepository.GetCurrentOpenShiftAsync(cafeId);
        if (existingShift != null)
            throw new ConflictException(ApiErrorMessages.CafeShift.ShiftAlreadyOpen(existingShift.Id));

        var shift = new CafeShift
        {
            CafeId = cafeId,
            OpenedByUserId = userId,
            OpenedAt = DateTime.UtcNow,
            OpeningCashBalance = openingCashBalance,
            ClosingCashBalance = 0,
            TotalRevenue = 0,
            TotalSessions = 0,
            Status = ShiftStatus.Open
        };

        await _shiftRepository.AddAsync(shift);
        await _shiftRepository.SaveChangesAsync();

        return MapToDto(shift);
    }

    public async Task<CafeShiftResponseDto> CloseShiftAsync(Guid shiftId, Guid userId, decimal closingCashBalance)
    {
        var shift = await _shiftRepository.GetByIdAsync(shiftId);
        if (shift == null)
            throw new NotFoundException(ApiErrorMessages.CafeShift.ShiftNotFound(shiftId));

        if (shift.Status == ShiftStatus.Closed)
            throw new ConflictException(ApiErrorMessages.CafeShift.ShiftAlreadyClosed(shiftId));

        shift.ClosedByUserId = userId;
        shift.ClosedAt = DateTime.UtcNow;
        shift.ClosingCashBalance = closingCashBalance;
        shift.Status = ShiftStatus.Closed;

        await _shiftRepository.UpdateAsync(shift);
        await _shiftRepository.SaveChangesAsync();

        return MapToDto(shift);
    }

    // P0-Fix-#6: validate caller có quyền xem shift của cafe hay không.
    // Admin có thể xem tất cả. Manager/CafeStaff phải thuộc cafe đó.
    private async Task EnsureCallerCanReadCafeShiftsAsync(Guid cafeId, Guid callerUserId, bool isAdmin)
    {
        if (isAdmin) return;

        // Manager: cafe.ManagerId == callerUserId
        var cafe = await _cafeRepository.GetByIdAsync(cafeId);
        if (cafe == null)
        {
            throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
        }

        if (cafe.ManagerId == callerUserId)
        {
            return;
        }

        // CafeStaff: có dòng trong CafeStaff với cafeId + staffUserId
        var isStaff = await _cafeRepository.IsStaffMemberExistsAsync(cafeId, callerUserId);
        if (isStaff)
        {
            return;
        }

        throw new ForbiddenException(ApiErrorMessages.Cafe.ManagerForbidden(cafeId));
    }

    public async Task<CafeShiftResponseDto?> GetCurrentShiftAsync(Guid cafeId, Guid callerUserId, bool isAdmin)
    {
        await EnsureCallerCanReadCafeShiftsAsync(cafeId, callerUserId, isAdmin);

        var shift = await _shiftRepository.GetCurrentOpenShiftAsync(cafeId);
        return shift == null ? null : MapToDto(shift);
    }

    public async Task<CafeShiftHistoryResponseDto> GetShiftHistoryAsync(Guid cafeId, int page, int pageSize, Guid callerUserId, bool isAdmin)
    {
        await EnsureCallerCanReadCafeShiftsAsync(cafeId, callerUserId, isAdmin);

        var shifts = await _shiftRepository.GetHistoryAsync(cafeId, page, pageSize);
        var totalCount = await _shiftRepository.GetHistoryCountAsync(cafeId);

        return new CafeShiftHistoryResponseDto
        {
            Shifts = shifts.Select(MapToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    private static CafeShiftResponseDto MapToDto(CafeShift shift) => new()
    {
        Id = shift.Id,
        CafeId = shift.CafeId,
        OpenedByUserId = shift.OpenedByUserId,
        ClosedByUserId = shift.ClosedByUserId,
        OpenedAt = shift.OpenedAt,
        ClosedAt = shift.ClosedAt,
        OpeningCashBalance = shift.OpeningCashBalance,
        ClosingCashBalance = shift.ClosingCashBalance,
        TotalRevenue = shift.TotalRevenue,
        TotalSessions = shift.TotalSessions,
        Status = shift.Status
    };
}
