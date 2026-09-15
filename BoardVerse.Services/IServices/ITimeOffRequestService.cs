using BoardVerse.Core.DTOs.StaffSchedule;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Service quản lý yêu cầu nghỉ phép của staff.
/// Manager duyệt/từ chối — khi duyệt, tự động tạo StaffUnavailableDate trong khoảng StartDate..EndDate.
/// </summary>
public interface ITimeOffRequestService
{
    /// <summary>Tạo yêu cầu nghỉ phép mới.</summary>
    Task<TimeOffRequestResponseDto> CreateAsync(Guid cafeId, Guid staffUserId, CreateTimeOffRequestDto dto, CancellationToken cancellationToken = default);

    /// <summary>Manager duyệt / từ chối / staff tự hủy yêu cầu.</summary>
    Task<TimeOffRequestResponseDto> ReviewAsync(Guid requestId, Guid managerUserId, ReviewTimeOffRequestDto dto, CancellationToken cancellationToken = default);

    /// <summary>Manager lấy danh sách yêu cầu nghỉ phép của cafe.</summary>
    Task<IReadOnlyList<TimeOffRequestResponseDto>> GetByCafeAsync(Guid cafeId, Core.Enum.TimeOffStatus? status = null, CancellationToken cancellationToken = default);

    /// <summary>Staff lấy danh sách yêu cầu nghỉ phép của mình.</summary>
    Task<IReadOnlyList<TimeOffRequestResponseDto>> GetMyRequestsAsync(Guid staffUserId, CancellationToken cancellationToken = default);
}