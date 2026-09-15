using BoardVerse.Core.DTOs.StaffSchedule;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Services.Services;

/// <summary>
/// ShiftAttendanceService — quản lý check-in/out ca làm việc của staff.
/// Tự động tính LateMinutes / EarlyLeaveMinutes dựa trên StaffSchedule.StartTime / EndTime,
/// có xử lý đúng cho ca qua đêm (EndTime &lt; StartTime).
/// </summary>
public class ShiftAttendanceService : IShiftAttendanceService
{
    private readonly IShiftAttendanceRepository _attendanceRepository;
    private readonly IStaffScheduleRepository _scheduleRepository;

    public ShiftAttendanceService(
        IShiftAttendanceRepository attendanceRepository,
        IStaffScheduleRepository scheduleRepository)
    {
        _attendanceRepository = attendanceRepository;
        _scheduleRepository = scheduleRepository;
    }

    public async Task<ShiftAttendanceResponseDto> CheckInAsync(Guid scheduleId, Guid staffUserId, CheckInAttendanceDto dto, CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleRepository.GetByIdAsync(scheduleId, cancellationToken);
        if (schedule == null)
            throw new NotFoundException(ApiErrorMessages.StaffSchedule.ScheduleNotFoundById(scheduleId));

        if (schedule.StaffUserId != staffUserId)
            throw new ForbiddenException("Bạn không có quyền điểm danh cho lịch làm việc này.");

        var checkInTime = dto.CheckInTime == default ? DateTime.UtcNow : dto.CheckInTime;

        // GAP-OVERNIGHT-09 fix: resolve shift date theo logic đúng cho ca qua đêm.
        // - Ca thường (EndTime >= StartTime): attendance.Date = today (UTC).
        // - Ca qua đêm (EndTime < StartTime): attendance.Date là NGÀY BẮT ĐẦU ca (DayOfWeek của schedule).
        //   • Nếu check-inTime rơi vào tối nay (UTC.Date == schedule.DayOfWeek) → attendance.Date = today, shiftStartedToday = true.
        //   • Nếu check-inTime rơi vào sáng mai (UTC.Date = (DayOfWeek+1)%7) → attendance.Date = today-1, shiftStartedYesterday = true.
        var isOvernight = schedule.EndTime < schedule.StartTime;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly shiftDate;
        bool shiftStartedToday;

        if (!isOvernight)
        {
            shiftDate = today;
            shiftStartedToday = true;
        }
        else
        {
            // Ca qua đêm: attendance.Date = ngày có DayOfWeek == schedule.DayOfWeek (lùi về quá khứ nếu cần).
            var todayDow = today.DayOfWeek;
            var targetDow = schedule.DayOfWeek;
            var daysBack = (todayDow - targetDow + 7) % 7;
            // Nếu todayDow == targetDow → daysBack = 0 (đang trong ngày bắt đầu ca)
            // Nếu todayDow == targetDow + 1 → daysBack = 1 (đang trong ngày kết thúc ca)
            // Nếu daysBack > 1 → ca đã qua, reject
            shiftDate = today.AddDays(-daysBack);
            shiftStartedToday = daysBack == 0;
        }

        var existing = await _attendanceRepository.GetByScheduleAndDateAsync(scheduleId, shiftDate, cancellationToken);
        if (existing != null && existing.CheckInTime.HasValue)
            throw new ConflictException(ApiErrorMessages.StaffSchedule.AttendanceAlreadyCheckedIn);

        // Reject nếu client gửi CheckInTime ở ngày khác shiftDate.
        // Với ca qua đêm: cho phép check-in date = shiftDate (tối) HOẶC shiftDate + 1 (sáng hôm sau).
        if (dto.CheckInTime != default)
        {
            var checkInDate = DateOnly.FromDateTime(checkInTime);
            var dateMatch = checkInDate == shiftDate;
            if (!dateMatch && isOvernight)
            {
                dateMatch = checkInDate == shiftDate.AddDays(1);
            }
            if (!dateMatch)
                throw new BadRequestException(ApiErrorMessages.StaffSchedule.AttendanceNotForToday);
        }

        // Tính lateMinutes dựa trên thời điểm trễ tối đa cho phép (lateBaseline).
        // - Ca thường: lateBaseline = shiftDate.ToDateTime(StartTime) → đi sau giờ bắt đầu = trễ.
        // - Ca qua đêm + check-in tối (shiftStartedToday): lateBaseline = shiftDate.ToDateTime(StartTime).
        // - Ca qua đêm + check-in sáng (shiftStartedYesterday): lateBaseline = scheduledEnd (ngày hôm sau)
        //   → check-in trong khung ca [shiftStart..shiftEnd] = không trễ.
        DateTime lateBaseline;
        if (!isOvernight)
        {
            lateBaseline = shiftDate.ToDateTime(schedule.StartTime);
        }
        else if (shiftStartedToday)
        {
            lateBaseline = shiftDate.ToDateTime(schedule.StartTime);
        }
        else
        {
            // Check-in sáng hôm sau của ca qua đêm hôm qua → so với giờ kết thúc ca.
            lateBaseline = shiftDate.AddDays(1).ToDateTime(schedule.EndTime);
        }

        var lateMinutes = checkInTime > lateBaseline
            ? (int)(checkInTime - lateBaseline).TotalMinutes
            : 0;

        var attendance = new ShiftAttendance
        {
            Id = Guid.NewGuid(),
            ScheduleId = scheduleId,
            StaffUserId = staffUserId,
            Date = shiftDate,
            CheckInTime = checkInTime,
            LateMinutes = lateMinutes,
            Status = lateMinutes > 0 ? "Late" : "Present",
            Note = dto.Note,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            if (existing != null)
            {
                existing.CheckInTime = attendance.CheckInTime;
                existing.LateMinutes = attendance.LateMinutes;
                existing.Status = attendance.Status;
                existing.Note = dto.Note;
                existing.UpdatedAt = DateTime.UtcNow;
                await _attendanceRepository.UpdateAsync(existing, cancellationToken);
            }
            else
            {
                await _attendanceRepository.AddAsync(attendance, cancellationToken);
            }
            await _attendanceRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Concurrent request beat us to creating the row — translate to 409 instead of 500.
            throw new ConflictException(ApiErrorMessages.StaffSchedule.AttendanceAlreadyCheckedIn);
        }

        return await MapToDtoAsync(attendance, cancellationToken);
    }

    public async Task<ShiftAttendanceResponseDto> CheckOutAsync(Guid attendanceId, Guid staffUserId, CheckOutAttendanceDto dto, CancellationToken cancellationToken = default)
    {
        var attendance = await _attendanceRepository.GetByIdAsync(attendanceId, cancellationToken);
        if (attendance == null)
            throw new NotFoundException($"Không tìm thấy bản ghi điểm danh '{attendanceId}'.");

        if (attendance.StaffUserId != staffUserId)
            throw new ForbiddenException("Bạn không có quyền check-out cho bản ghi này.");

        if (!attendance.CheckInTime.HasValue)
            throw new ConflictException(ApiErrorMessages.StaffSchedule.AttendanceNotCheckedIn);

        if (attendance.CheckOutTime.HasValue)
            throw new ConflictException(ApiErrorMessages.StaffSchedule.AttendanceAlreadyCheckedOut);

        var checkOutTime = dto.CheckOutTime == default ? DateTime.UtcNow : dto.CheckOutTime;
        var schedule = attendance.Schedule ?? await _scheduleRepository.GetByIdAsync(attendance.ScheduleId, cancellationToken);

        if (schedule != null)
        {
            var scheduledEnd = ResolveScheduledEnd(attendance.Date, schedule.StartTime, schedule.EndTime);
            // Early-leave: chỉ tính khi check-out xảy ra trước scheduled end.
            // Với ca qua đêm, ResolveScheduledEnd đã trả về DateTime của ngày hôm sau nếu cần.
            var earlyLeaveMinutes = checkOutTime < scheduledEnd
                ? (int)(scheduledEnd - checkOutTime).TotalMinutes
                : 0;
            attendance.EarlyLeaveMinutes = earlyLeaveMinutes > 0 ? earlyLeaveMinutes : 0;
        }

        attendance.CheckOutTime = checkOutTime;
        if (!string.IsNullOrWhiteSpace(dto.Note))
        {
            attendance.Note = string.IsNullOrEmpty(attendance.Note)
                ? dto.Note
                : $"{attendance.Note} | {dto.Note}";
        }
        attendance.UpdatedAt = DateTime.UtcNow;

        await _attendanceRepository.UpdateAsync(attendance, cancellationToken);
        await _attendanceRepository.SaveChangesAsync(cancellationToken);

        return await MapToDtoAsync(attendance, cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftAttendanceResponseDto>> GetByStaffAsync(Guid staffUserId, Guid cafeId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            throw new BadRequestException("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.");

        var list = await _attendanceRepository.GetByStaffAsync(staffUserId, cafeId, startDate, endDate, cancellationToken);
        var mapped = new List<ShiftAttendanceResponseDto>(list.Count);
        foreach (var a in list)
        {
            mapped.Add(await MapToDtoAsync(a, cancellationToken));
        }
        return mapped;
    }

    #region Private helpers

    /// <summary>
    /// Tính DateTime kết thúc ca làm việc cho 1 ngày attendance cụ thể.
    /// Với ca qua đêm (EndTime &lt; StartTime), attendance.Date được hiểu là ngày BẮT ĐẦU ca
    /// → kết thúc ca rơi vào ngày hôm sau.
    /// </summary>
    private static DateTime ResolveScheduledEnd(DateOnly attendanceDate, TimeOnly startTime, TimeOnly endTime)
    {
        var isOvernight = endTime < startTime;
        if (!isOvernight) return attendanceDate.ToDateTime(endTime);

        // Ca qua đêm: attendanceDate là ngày bắt đầu → end = attendanceDate + 1 ngày.
        return attendanceDate.AddDays(1).ToDateTime(endTime);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // Postgres SQLSTATE 23505 = unique_violation
        var inner = ex.InnerException;
        return inner != null && (
            inner.Message.Contains("23505", StringComparison.OrdinalIgnoreCase) ||
            inner.Message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) ||
            inner.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ShiftAttendanceResponseDto> MapToDtoAsync(ShiftAttendance a, CancellationToken cancellationToken)
    {
        var staffName = a.User?.Username ?? string.Empty;
        if (string.IsNullOrEmpty(staffName))
        {
            // Load lại để có User (không phụ thuộc Schedule nav — User riêng biệt)
            var fresh = await _attendanceRepository.GetByIdAsync(a.Id, cancellationToken);
            staffName = fresh?.User?.Username ?? string.Empty;
        }

        return new ShiftAttendanceResponseDto
        {
            Id = a.Id,
            ScheduleId = a.ScheduleId,
            StaffUserId = a.StaffUserId,
            StaffName = staffName,
            Date = a.Date,
            CheckInTime = a.CheckInTime,
            CheckOutTime = a.CheckOutTime,
            LateMinutes = a.LateMinutes,
            EarlyLeaveMinutes = a.EarlyLeaveMinutes,
            Status = a.Status,
            Note = a.Note
        };
    }

    #endregion
}