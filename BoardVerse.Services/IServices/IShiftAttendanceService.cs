using BoardVerse.Core.DTOs.StaffSchedule;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Service quản lý check-in/out ca làm việc của staff.
/// Tự động tính LateMinutes / EarlyLeaveMinutes dựa trên StaffSchedule.StartTime / EndTime.
/// </summary>
public interface IShiftAttendanceService
{
    /// <summary>Check-in ca làm việc.</summary>
    Task<ShiftAttendanceResponseDto> CheckInAsync(Guid scheduleId, Guid staffUserId, CheckInAttendanceDto dto, CancellationToken cancellationToken = default);

    /// <summary>Check-out ca làm việc.</summary>
    Task<ShiftAttendanceResponseDto> CheckOutAsync(Guid attendanceId, Guid staffUserId, CheckOutAttendanceDto dto, CancellationToken cancellationToken = default);

    /// <summary>Lấy danh sách điểm danh của staff trong khoảng ngày.</summary>
    Task<IReadOnlyList<ShiftAttendanceResponseDto>> GetByStaffAsync(Guid staffUserId, Guid cafeId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
}