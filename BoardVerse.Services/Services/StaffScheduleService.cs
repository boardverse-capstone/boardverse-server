using BoardVerse.Core.DTOs.StaffSchedule;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

/// <summary>
/// StaffScheduleService — quản lý lịch làm việc của staff (nhiều ca/ngày, ca qua đêm, copy template).
/// </summary>
public class StaffScheduleService : IStaffScheduleService
{
    private readonly IStaffScheduleRepository _scheduleRepository;
    private readonly IShiftAttendanceRepository _attendanceRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly IPushNotificationService _pushNotificationService;
    private readonly ILogger<StaffScheduleService> _logger;

    public StaffScheduleService(
        IStaffScheduleRepository scheduleRepository,
        IShiftAttendanceRepository attendanceRepository,
        ICafeRepository cafeRepository,
        IPushNotificationService pushNotificationService,
        ILogger<StaffScheduleService> logger)
    {
        _scheduleRepository = scheduleRepository;
        _attendanceRepository = attendanceRepository;
        _cafeRepository = cafeRepository;
        _pushNotificationService = pushNotificationService;
        _logger = logger;
    }

    public async Task<StaffScheduleResponseDto> CreateAsync(Guid cafeId, CreateStaffScheduleRequestDto dto, CancellationToken cancellationToken = default)
    {
        ValidateShiftTimes(dto.StartTime, dto.EndTime);
        var schedule = await BuildScheduleAsync(cafeId, dto, excludeScheduleId: null, cancellationToken);

        await _scheduleRepository.AddAsync(schedule, cancellationToken);
        await _scheduleRepository.SaveChangesAsync(cancellationToken);

        await NotifyScheduleChangedAsync(schedule, "created", cancellationToken);

        return await MapToDtoAsync(schedule, cancellationToken);
    }

    public async Task<List<StaffScheduleResponseDto>> BulkCreateAsync(Guid cafeId, BulkCreateStaffScheduleRequestDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Schedules == null || dto.Schedules.Count == 0)
            throw new BadRequestException("Danh sách lịch không được rỗng.");

        // Phase 1 — validate hết (không SaveChanges): nếu có item nào fail thì không ghi gì cả.
        var created = new List<StaffSchedule>(dto.Schedules.Count);
        foreach (var item in dto.Schedules)
        {
            ValidateShiftTimes(item.StartTime, item.EndTime);
            var schedule = await BuildScheduleAsync(cafeId, item, excludeScheduleId: null, cancellationToken);
            created.Add(schedule);
        }

        // Phase 2 — persist all (1 SaveChanges).
        await _scheduleRepository.AddRangeAsync(created, cancellationToken);
        await _scheduleRepository.SaveChangesAsync(cancellationToken);

        // Phase 3 — notify (best-effort, lỗi notification không chặn response).
        foreach (var s in created)
        {
            await NotifyScheduleChangedAsync(s, "created", cancellationToken);
        }

        var mapped = new List<StaffScheduleResponseDto>(created.Count);
        foreach (var s in created)
        {
            mapped.Add(await MapToDtoAsync(s, cancellationToken));
        }
        return mapped;
    }

    public async Task<StaffScheduleResponseDto> UpdateAsync(Guid cafeId, Guid scheduleId, UpdateStaffScheduleRequestDto dto, CancellationToken cancellationToken = default)
    {
        ValidateShiftTimes(dto.StartTime, dto.EndTime);

        var schedule = await _scheduleRepository.GetByIdAsync(scheduleId, cancellationToken);
        if (schedule == null)
            throw new NotFoundException(ApiErrorMessages.StaffSchedule.ScheduleNotFoundById(scheduleId));

        // GAP-IDOR-01 fix: chặn Manager cafe A sửa lịch của cafe B bằng cách truyền cafeId đúng ở URL nhưng scheduleId của cafe khác.
        if (schedule.CafeId != cafeId)
            throw new NotFoundException(ApiErrorMessages.StaffSchedule.ScheduleNotFoundById(scheduleId));

        var overlap = await _scheduleRepository.HasOverlappingScheduleAsync(
            schedule.CafeId, schedule.StaffUserId, schedule.DayOfWeek, dto.StartTime, dto.EndTime, scheduleId, cancellationToken);
        if (overlap)
            throw new ConflictException(ApiErrorMessages.StaffSchedule.OverlappingSchedule);

        schedule.StartTime = dto.StartTime;
        schedule.EndTime = dto.EndTime;
        schedule.ShiftType = dto.ShiftType;
        schedule.IsRecurring = dto.IsRecurring;
        schedule.Note = dto.Note;
        schedule.Status = dto.Status;
        schedule.UpdatedAt = DateTime.UtcNow;

        await _scheduleRepository.UpdateAsync(schedule, cancellationToken);
        await _scheduleRepository.SaveChangesAsync(cancellationToken);

        await NotifyScheduleChangedAsync(schedule, "updated", cancellationToken);

        return await MapToDtoAsync(schedule, cancellationToken);
    }

    public async Task DeleteAsync(Guid cafeId, Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleRepository.GetByIdAsync(scheduleId, cancellationToken);
        if (schedule == null)
            throw new NotFoundException(ApiErrorMessages.StaffSchedule.ScheduleNotFoundById(scheduleId));

        // GAP-IDOR-01 fix: chặn Manager cafe A xóa lịch của cafe B.
        if (schedule.CafeId != cafeId)
            throw new NotFoundException(ApiErrorMessages.StaffSchedule.ScheduleNotFoundById(scheduleId));

        await _scheduleRepository.DeleteAsync(scheduleId, cancellationToken);
        await _scheduleRepository.SaveChangesAsync(cancellationToken);

        await NotifyScheduleChangedAsync(schedule, "deleted", cancellationToken);
    }

    public async Task<int> DeleteByStaffAsync(Guid cafeId, Guid staffUserId, CancellationToken cancellationToken = default)
    {
        var count = await _scheduleRepository.DeleteByStaffAsync(cafeId, staffUserId, cancellationToken);
        if (count > 0)
        {
            await _scheduleRepository.SaveChangesAsync(cancellationToken);
        }
        return count;
    }

    public async Task<StaffScheduleResponseDto?> GetByIdAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await _scheduleRepository.GetByIdAsync(scheduleId, cancellationToken);
        if (schedule == null) return null;
        return await MapToDtoAsync(schedule, cancellationToken);
    }

    public async Task<StaffScheduleListResponseDto> GetByCafeAsync(Guid cafeId, Guid? staffUserId = null, DayOfWeek? dayOfWeek = null, CancellationToken cancellationToken = default)
    {
        var all = await _scheduleRepository.GetByCafeAsync(cafeId, cancellationToken);
        var filtered = all.AsEnumerable();
        if (staffUserId.HasValue)
            filtered = filtered.Where(s => s.StaffUserId == staffUserId.Value);
        if (dayOfWeek.HasValue)
            filtered = filtered.Where(s => s.DayOfWeek == dayOfWeek.Value);

        var mapped = new List<StaffScheduleResponseDto>();
        foreach (var s in filtered.ToList())
        {
            mapped.Add(await MapToDtoAsync(s, cancellationToken));
        }

        return new StaffScheduleListResponseDto
        {
            CafeId = cafeId,
            FilterStaffUserId = staffUserId,
            FilterDayOfWeek = dayOfWeek,
            TotalCount = mapped.Count,
            Schedules = mapped
        };
    }

    public async Task<StaffScheduleListResponseDto> GetMyScheduleAsync(Guid staffUserId, Guid cafeId, CancellationToken cancellationToken = default)
    {
        var schedules = await _scheduleRepository.GetByStaffAsync(staffUserId, cafeId, cancellationToken);
        var mapped = new List<StaffScheduleResponseDto>();
        foreach (var s in schedules)
        {
            mapped.Add(await MapToDtoAsync(s, cancellationToken));
        }

        return new StaffScheduleListResponseDto
        {
            CafeId = cafeId,
            FilterStaffUserId = staffUserId,
            TotalCount = mapped.Count,
            Schedules = mapped
        };
    }

    public async Task<StaffScheduleListResponseDto> GetMyScheduleForDateRangeAsync(Guid staffUserId, Guid cafeId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            throw new BadRequestException("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.");

        var templates = await _scheduleRepository.GetByStaffAsync(staffUserId, cafeId, cancellationToken);
        var mapped = new List<StaffScheduleResponseDto>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var dow = date.DayOfWeek;
            foreach (var t in templates.Where(s => s.DayOfWeek == dow && s.Status == StaffScheduleStatus.Active))
            {
                var dto = await MapToDtoAsync(t, cancellationToken);
                mapped.Add(dto);
            }
        }

        return new StaffScheduleListResponseDto
        {
            CafeId = cafeId,
            FilterStaffUserId = staffUserId,
            TotalCount = mapped.Count,
            Schedules = mapped
        };
    }

    public async Task<CopyTemplateResultDto> CopyWeekTemplateAsync(Guid cafeId, CopyWeekTemplateRequestDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.ToWeekStart <= dto.FromWeekStart)
            throw new BadRequestException("Ngày bắt đầu tuần đích phải sau tuần nguồn.");

        // StaffSchedule là template recurring theo DayOfWeek → không có "ngày cụ thể".
        // Để copy-template có ý nghĩa thực tế: chỉ copy các template mà staff đã THỰC SỰ đi làm
        // (có ShiftAttendance) trong tuần nguồn. Nếu không có attendance nào trong tuần nguồn,
        // fallback copy toàn bộ Active templates (giữ backward-compatible).
        var sourceWeekEnd = dto.FromWeekStart.AddDays(6);
        var activeTemplates = await _scheduleRepository.GetActiveByCafeAsync(cafeId, dto.FromWeekStart, sourceWeekEnd, cancellationToken);

        if (dto.StaffUserIdFilter.HasValue)
        {
            activeTemplates = activeTemplates
                .Where(s => s.StaffUserId == dto.StaffUserIdFilter.Value)
                .ToList();
        }

        // Lấy attendance trong tuần nguồn để biết template nào đã được dùng.
        // Lưu ý: attendance trong tuần nguồn có thể thuộc về schedule KHÔNG còn tồn tại
        // (do FK cascade) — nên lấy theo staffUserId + date range, không theo scheduleId.
        var staffIds = activeTemplates.Select(t => t.StaffUserId).Distinct().ToList();
        var usedTemplateKeys = new HashSet<(Guid staffId, DayOfWeek dow)>();
        foreach (var staffId in staffIds)
        {
            // Lấy attendance của staff trong tuần nguồn bằng cách enumerate từng schedule.
            // (N+1 nhỏ, chỉ chạy khi copy-template, không trong hot-path.)
            var staffTemplates = activeTemplates.Where(t => t.StaffUserId == staffId).ToList();
            foreach (var t in staffTemplates)
            {
                var hasAttendance = await _attendanceRepository.HasAttendanceInRangeAsync(
                    staffId, t.Id, dto.FromWeekStart, sourceWeekEnd, cancellationToken);
                if (hasAttendance)
                {
                    usedTemplateKeys.Add((staffId, t.DayOfWeek));
                }
            }
        }

        // Nếu không tìm thấy attendance nào → fallback copy tất cả Active.
        var sourceSchedules = usedTemplateKeys.Count > 0
            ? activeTemplates.Where(t => usedTemplateKeys.Contains((t.StaffUserId, t.DayOfWeek))).ToList()
            : activeTemplates.ToList();

        var newSchedules = new List<StaffSchedule>();
        foreach (var src in sourceSchedules)
        {
            // Bỏ qua các ca Inactive/Cancelled — chỉ copy Active
            if (src.Status != StaffScheduleStatus.Active) continue;

            var newSchedule = new StaffSchedule
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                StaffUserId = src.StaffUserId,
                DayOfWeek = src.DayOfWeek,
                StartTime = src.StartTime,
                EndTime = src.EndTime,
                ShiftType = src.ShiftType,
                IsRecurring = src.IsRecurring,
                Note = src.Note,
                Status = StaffScheduleStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            newSchedules.Add(newSchedule);
        }

        if (newSchedules.Count > 0)
        {
            await _scheduleRepository.AddRangeAsync(newSchedules, cancellationToken);
            await _scheduleRepository.SaveChangesAsync(cancellationToken);

            // Thông báo cho từng staff bị ảnh hưởng (best-effort).
            var distinctStaff = newSchedules.Select(s => s.StaffUserId).Distinct();
            foreach (var staffId in distinctStaff)
            {
                var representative = newSchedules.First(s => s.StaffUserId == staffId);
                await NotifyScheduleChangedAsync(representative, "created", cancellationToken);
            }
        }

        var mapped = new List<StaffScheduleResponseDto>();
        foreach (var s in newSchedules)
        {
            mapped.Add(await MapToDtoAsync(s, cancellationToken));
        }

        return new CopyTemplateResultDto
        {
            CopiedCount = newSchedules.Count,
            FromWeekStart = dto.FromWeekStart,
            ToWeekStart = dto.ToWeekStart,
            NewSchedules = mapped
        };
    }

    public async Task<WorkHoursSummaryDto> GetWorkHoursSummaryAsync(Guid staffUserId, Guid cafeId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
            throw new BadRequestException("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.");

        var templates = await _scheduleRepository.GetByStaffAsync(staffUserId, cafeId, cancellationToken);
        var attendances = await _attendanceRepository.GetByStaffAsync(staffUserId, cafeId, startDate, endDate, cancellationToken);

        var presentStatuses = new[] { "Present", "Late" };

        var totalScheduledMinutes = 0;
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var dow = date.DayOfWeek;
            foreach (var t in templates.Where(s => s.DayOfWeek == dow && s.Status == StaffScheduleStatus.Active))
            {
                totalScheduledMinutes += CalculateShiftDurationMinutes(t.StartTime, t.EndTime);
            }
        }

        var summary = new WorkHoursSummaryDto
        {
            StaffUserId = staffUserId,
            StaffName = templates.FirstOrDefault()?.User?.Username ?? string.Empty,
            StartDate = startDate,
            EndDate = endDate,
            TotalScheduledMinutes = totalScheduledMinutes,
            TotalWorkedMinutes = (int)attendances
                .Where(a => a.CheckInTime.HasValue && a.CheckOutTime.HasValue)
                .Sum(a => (a.CheckOutTime!.Value - a.CheckInTime!.Value).TotalMinutes),
            TotalLateMinutes = attendances.Sum(a => a.LateMinutes),
            TotalEarlyLeaveMinutes = attendances.Sum(a => a.EarlyLeaveMinutes),
            PresentDays = attendances.Count(a => !string.IsNullOrEmpty(a.Status) && presentStatuses.Contains(a.Status)),
            AbsentDays = attendances.Count(a => a.Status == "Absent"),
            InProgressDays = attendances.Count(a => a.CheckInTime.HasValue && !a.CheckOutTime.HasValue)
        };

        return summary;
    }

    #region Private helpers

    private const int MaxShiftMinutes = 16 * 60; // 16 giờ — giới hạn trên cho 1 ca

    private static void ValidateShiftTimes(TimeOnly startTime, TimeOnly endTime)
    {
        if (startTime == endTime)
            throw new BadRequestException(ApiErrorMessages.StaffSchedule.StartTimeEqualsEndTime);

        var durationMinutes = endTime > startTime
            ? (int)(endTime - startTime).TotalMinutes
            : (int)((TimeOnly.MaxValue - startTime).TotalMinutes + 1 + (endTime - TimeOnly.MinValue).TotalMinutes);

        if (durationMinutes > MaxShiftMinutes)
            throw new BadRequestException(ApiErrorMessages.StaffSchedule.ShiftTooLong);
    }

    /// <summary>
    /// Validate + tạo StaffSchedule entity (chưa SaveChanges). Tái sử dụng cho Create và BulkCreate.
    /// </summary>
    private async Task<StaffSchedule> BuildScheduleAsync(
        Guid cafeId,
        CreateStaffScheduleRequestDto dto,
        Guid? excludeScheduleId,
        CancellationToken cancellationToken)
    {
        var cafe = await _cafeRepository.GetByIdAsync(cafeId, cancellationToken);
        if (cafe == null)
            throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        var isStaff = await _cafeRepository.IsStaffMemberExistsAsync(cafeId, dto.StaffUserId, cancellationToken);
        if (!isStaff && cafe.ManagerId != dto.StaffUserId)
            throw new BadRequestException(ApiErrorMessages.StaffSchedule.NotCafeStaff);

        var overlap = await _scheduleRepository.HasOverlappingScheduleAsync(
            cafeId, dto.StaffUserId, dto.DayOfWeek, dto.StartTime, dto.EndTime, excludeScheduleId, cancellationToken);
        if (overlap)
            throw new ConflictException(ApiErrorMessages.StaffSchedule.OverlappingSchedule);

        return new StaffSchedule
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            StaffUserId = dto.StaffUserId,
            DayOfWeek = dto.DayOfWeek,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            ShiftType = dto.ShiftType,
            IsRecurring = dto.IsRecurring,
            Note = dto.Note,
            Status = StaffScheduleStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static int CalculateShiftDurationMinutes(TimeOnly startTime, TimeOnly endTime)
    {
        if (endTime > startTime) return (int)(endTime - startTime).TotalMinutes;
        // Ca qua đêm: endTime < startTime → wrap around 24h
        return (int)((TimeOnly.MaxValue - startTime).TotalMinutes + 1 + (endTime - TimeOnly.MinValue).TotalMinutes);
    }

    private async Task<StaffScheduleResponseDto> MapToDtoAsync(StaffSchedule schedule, CancellationToken cancellationToken)
    {
        var staffName = schedule.User?.Username ?? string.Empty;
        if (string.IsNullOrEmpty(staffName))
        {
            var user = await _cafeRepository.GetUserByIdAsync(schedule.StaffUserId, cancellationToken);
            staffName = user?.Username ?? string.Empty;
        }

        return new StaffScheduleResponseDto
        {
            Id = schedule.Id,
            CafeId = schedule.CafeId,
            StaffUserId = schedule.StaffUserId,
            StaffName = staffName,
            DayOfWeek = schedule.DayOfWeek,
            DayOfWeekName = GetVietnameseDayOfWeekName(schedule.DayOfWeek),
            StartTime = schedule.StartTime,
            EndTime = schedule.EndTime,
            IsOvernight = schedule.EndTime < schedule.StartTime,
            DurationMinutes = CalculateShiftDurationMinutes(schedule.StartTime, schedule.EndTime),
            ShiftType = schedule.ShiftType,
            IsRecurring = schedule.IsRecurring,
            Note = schedule.Note,
            Status = schedule.Status,
            CreatedAt = schedule.CreatedAt,
            UpdatedAt = schedule.UpdatedAt
        };
    }

    private static string GetVietnameseDayOfWeekName(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday => "Thứ 2",
        DayOfWeek.Tuesday => "Thứ 3",
        DayOfWeek.Wednesday => "Thứ 4",
        DayOfWeek.Thursday => "Thứ 5",
        DayOfWeek.Friday => "Thứ 6",
        DayOfWeek.Saturday => "Thứ 7",
        DayOfWeek.Sunday => "Chủ nhật",
        _ => dow.ToString()
    };

    private async Task NotifyScheduleChangedAsync(StaffSchedule schedule, string changeType, CancellationToken cancellationToken)
    {
        try
        {
            var dayName = GetVietnameseDayOfWeekName(schedule.DayOfWeek);
            var message = changeType switch
            {
                "created" => $"Bạn vừa được phân ca mới: {dayName} ({schedule.StartTime:HH\\:mm}-{schedule.EndTime:HH\\:mm}).",
                "updated" => $"Lịch làm việc {dayName} ({schedule.StartTime:HH\\:mm}-{schedule.EndTime:HH\\:mm}) đã được cập nhật.",
                "deleted" => $"Lịch làm việc {dayName} ({schedule.StartTime:HH\\:mm}-{schedule.EndTime:HH\\:mm}) đã bị xóa.",
                _ => $"Lịch làm việc của bạn đã thay đổi."
            };

            await _pushNotificationService.SendAsync(
                schedule.StaffUserId,
                "Lịch làm việc",
                message,
                new Dictionary<string, string>
                {
                    { "type", $"staff_schedule_{changeType}" },
                    { "scheduleId", schedule.Id.ToString() },
                    { "cafeId", schedule.CafeId.ToString() }
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[StaffSchedule] Failed to send notification for schedule {ScheduleId} changeType={ChangeType}", schedule.Id, changeType);
        }
    }

    #endregion
}