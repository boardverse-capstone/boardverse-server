using BoardVerse.Core.DTOs.StaffSchedule;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Service quản lý yêu cầu đổi ca giữa 2 staff.
/// Manager duyệt — khi duyệt, 2 lịch StaffSchedule được hoán đổi StaffUserId.
/// </summary>
public interface IShiftSwapRequestService
{
    /// <summary>Tạo yêu cầu đổi ca mới.</summary>
    Task<ShiftSwapRequestResponseDto> CreateAsync(Guid cafeId, Guid requesterId, CreateShiftSwapRequestDto dto, CancellationToken cancellationToken = default);

    /// <summary>Manager duyệt / từ chối yêu cầu đổi ca.</summary>
    Task<ShiftSwapRequestResponseDto> ReviewAsync(Guid requestId, Guid managerUserId, ReviewShiftSwapRequestDto dto, CancellationToken cancellationToken = default);

    /// <summary>Manager lấy danh sách yêu cầu đổi ca của cafe.</summary>
    Task<IReadOnlyList<ShiftSwapRequestResponseDto>> GetByCafeAsync(Guid cafeId, Core.Enum.ShiftSwapStatus? status = null, CancellationToken cancellationToken = default);

    /// <summary>Staff lấy các yêu cầu đổi ca gửi đến mình.</summary>
    Task<IReadOnlyList<ShiftSwapRequestResponseDto>> GetIncomingForStaffAsync(Guid staffUserId, CancellationToken cancellationToken = default);

    /// <summary>Staff lấy các yêu cầu đổi ca mình đã gửi.</summary>
    Task<IReadOnlyList<ShiftSwapRequestResponseDto>> GetByRequesterAsync(Guid requesterId, CancellationToken cancellationToken = default);
}