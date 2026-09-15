using BoardVerse.Core.DTOs.StaffSchedule;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Service quản lý ngày nghỉ cố định của staff.
/// Hệ thống sẽ cảnh báo Manager khi lên lịch ca trùng ngày này.
/// </summary>
public interface IStaffUnavailableDateService
{
    /// <summary>Thêm ngày nghỉ mới.</summary>
    Task<UnavailableDateResponseDto> CreateAsync(Guid cafeId, Guid staffUserId, CreateUnavailableDateDto dto, CancellationToken cancellationToken = default);

    /// <summary>Xóa ngày nghỉ.</summary>
    Task DeleteAsync(Guid id, Guid staffUserId, CancellationToken cancellationToken = default);

    /// <summary>Staff lấy danh sách ngày nghỉ của mình.</summary>
    Task<IReadOnlyList<UnavailableDateResponseDto>> GetMyUnavailableDatesAsync(Guid staffUserId, Guid cafeId, DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default);

    /// <summary>Kiểm tra staff có unavailable trong ngày cụ thể không.</summary>
    Task<bool> IsUnavailableAsync(Guid staffUserId, DateOnly date, CancellationToken cancellationToken = default);
}