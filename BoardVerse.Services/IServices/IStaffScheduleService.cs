using BoardVerse.Core.DTOs.StaffSchedule;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Service quản lý lịch làm việc của staff theo ngày trong tuần.
/// Hỗ trợ nhiều ca/ngày, ca qua đêm, copy template tuần, và tổng hợp số giờ làm.
/// </summary>
public interface IStaffScheduleService
{
    /// <summary>Tạo mới 1 lịch làm việc.</summary>
    Task<StaffScheduleResponseDto> CreateAsync(Guid cafeId, CreateStaffScheduleRequestDto dto, CancellationToken cancellationToken = default);

    /// <summary>Tạo nhiều lịch cùng lúc (bulk).</summary>
    Task<List<StaffScheduleResponseDto>> BulkCreateAsync(Guid cafeId, BulkCreateStaffScheduleRequestDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật lịch làm việc. [Role: Manager]
    /// <para>GAP-IDOR-01 fix: validate <paramref name="cafeId"/> khớp với <c>schedule.CafeId</c> — Manager của cafe A không thể sửa lịch của cafe B.</para>
    /// </summary>
    /// <param name="cafeId">Mã cafe từ URL (phải khớp với schedule.CafeId).</param>
    /// <param name="scheduleId">Mã lịch làm việc.</param>
    /// <param name="dto">Thông tin cập nhật.</param>
    Task<StaffScheduleResponseDto> UpdateAsync(Guid cafeId, Guid scheduleId, UpdateStaffScheduleRequestDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xóa 1 lịch làm việc. [Role: Manager]
    /// <para>GAP-IDOR-01 fix: validate <paramref name="cafeId"/> khớp với <c>schedule.CafeId</c>.</para>
    /// </summary>
    /// <param name="cafeId">Mã cafe từ URL (phải khớp với schedule.CafeId).</param>
    /// <param name="scheduleId">Mã lịch làm việc.</param>
    Task DeleteAsync(Guid cafeId, Guid scheduleId, CancellationToken cancellationToken = default);

    /// <summary>Xóa tất cả lịch của 1 staff trong 1 cafe.</summary>
    Task<int> DeleteByStaffAsync(Guid cafeId, Guid staffUserId, CancellationToken cancellationToken = default);

    /// <summary>Lấy 1 lịch theo id.</summary>
    Task<StaffScheduleResponseDto?> GetByIdAsync(Guid scheduleId, CancellationToken cancellationToken = default);

    /// <summary>Lấy danh sách lịch của cafe, có filter theo staff và dayOfWeek.</summary>
    Task<StaffScheduleListResponseDto> GetByCafeAsync(Guid cafeId, Guid? staffUserId = null, DayOfWeek? dayOfWeek = null, CancellationToken cancellationToken = default);

    /// <summary>Lấy lịch của 1 staff (role CafeStaff).</summary>
    Task<StaffScheduleListResponseDto> GetMyScheduleAsync(Guid staffUserId, Guid cafeId, CancellationToken cancellationToken = default);

    /// <summary>Lấy lịch của 1 staff trong khoảng ngày.</summary>
    Task<StaffScheduleListResponseDto> GetMyScheduleForDateRangeAsync(Guid staffUserId, Guid cafeId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);

    /// <summary>Copy lịch từ tuần nguồn sang tuần đích.</summary>
    Task<CopyTemplateResultDto> CopyWeekTemplateAsync(Guid cafeId, CopyWeekTemplateRequestDto dto, CancellationToken cancellationToken = default);

    /// <summary>Tổng hợp số giờ làm của staff trong khoảng thời gian.</summary>
    Task<WorkHoursSummaryDto> GetWorkHoursSummaryAsync(Guid staffUserId, Guid cafeId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
}