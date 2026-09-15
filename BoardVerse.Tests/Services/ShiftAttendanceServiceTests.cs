using BoardVerse.Core.DTOs.StaffSchedule;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho ShiftAttendanceService — focus vào GAP-OVERNIGHT-09 fix:
/// check-in cho ca qua đêm phải ghi nhận đúng attendance.Date là ngày BẮT ĐẦU ca,
/// không phải ngày check-in thực tế.
/// </summary>
public class ShiftAttendanceServiceTests
{
    private readonly Mock<IShiftAttendanceRepository> _attendanceRepo = new();
    private readonly Mock<IStaffScheduleRepository> _scheduleRepo = new();

    private BoardVerse.Services.Services.ShiftAttendanceService CreateService() => new(
        _attendanceRepo.Object,
        _scheduleRepo.Object);

    private static User BuildUser(Guid id, string username) => new()
    {
        Id = id,
        Username = username,
        Email = $"{username}@test.com"
    };

    [Fact]
    public async Task CheckInAsync_ScheduleNotFound_ThrowsNotFound()
    {
        var scheduleId = Guid.NewGuid();
        _scheduleRepo.Setup(r => r.GetByIdAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StaffSchedule?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() => sut.CheckInAsync(
            scheduleId, Guid.NewGuid(), new CheckInAttendanceDto()));
    }

    [Fact]
    public async Task CheckInAsync_BelongsToDifferentStaff_ThrowsForbidden()
    {
        var scheduleId = Guid.NewGuid();
        var schedule = new StaffSchedule
        {
            Id = scheduleId,
            StaffUserId = Guid.NewGuid(),
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(14, 0)
        };
        _scheduleRepo.Setup(r => r.GetByIdAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        var sut = CreateService();
        await Assert.ThrowsAsync<ForbiddenException>(() => sut.CheckInAsync(
            scheduleId, Guid.NewGuid(), new CheckInAttendanceDto()));
    }

    [Fact]
    public async Task CheckInAsync_AlreadyCheckedIn_ThrowsConflict()
    {
        var scheduleId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var schedule = new StaffSchedule
        {
            Id = scheduleId,
            StaffUserId = staffId,
            DayOfWeek = today.DayOfWeek,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(14, 0)
        };

        var existing = new ShiftAttendance
        {
            Id = Guid.NewGuid(),
            ScheduleId = scheduleId,
            StaffUserId = staffId,
            Date = today,
            CheckInTime = DateTime.UtcNow.AddMinutes(-30)
        };

        _scheduleRepo.Setup(r => r.GetByIdAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);
        _attendanceRepo.Setup(r => r.GetByScheduleAndDateAsync(scheduleId, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = CreateService();
        await Assert.ThrowsAsync<ConflictException>(() => sut.CheckInAsync(
            scheduleId, staffId, new CheckInAttendanceDto()));
    }

    [Fact]
    public async Task CheckInAsync_NormalShift_NoLate()
    {
        // Ca thường Mon 06:00-14:00, check-in 06:00 → lateMinutes = 0, status = Present.
        var scheduleId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var schedule = new StaffSchedule
        {
            Id = scheduleId,
            StaffUserId = staffId,
            DayOfWeek = today.DayOfWeek,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(14, 0)
        };

        _scheduleRepo.Setup(r => r.GetByIdAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);
        _attendanceRepo.Setup(r => r.GetByScheduleAndDateAsync(scheduleId, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftAttendance?)null);

        var sut = CreateService();
        var checkInTime = today.ToDateTime(new TimeOnly(6, 0));
        var result = await sut.CheckInAsync(scheduleId, staffId, new CheckInAttendanceDto
        {
            CheckInTime = checkInTime
        });

        Assert.Equal(0, result.LateMinutes);
        Assert.Equal("Present", result.Status);
        Assert.Equal(today, result.Date);
    }

    [Fact]
    public async Task CheckInAsync_NormalShift_LateCheckIn_CountsLateMinutes()
    {
        // Ca thường Mon 06:00-14:00, check-in 07:30 → lateMinutes = 90.
        var scheduleId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var schedule = new StaffSchedule
        {
            Id = scheduleId,
            StaffUserId = staffId,
            DayOfWeek = today.DayOfWeek,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(14, 0)
        };

        _scheduleRepo.Setup(r => r.GetByIdAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);
        _attendanceRepo.Setup(r => r.GetByScheduleAndDateAsync(scheduleId, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftAttendance?)null);

        var sut = CreateService();
        var checkInTime = today.ToDateTime(new TimeOnly(7, 30));
        var result = await sut.CheckInAsync(scheduleId, staffId, new CheckInAttendanceDto
        {
            CheckInTime = checkInTime
        });

        Assert.Equal(90, result.LateMinutes);
        Assert.Equal("Late", result.Status);
    }

    [Fact]
    public async Task CheckInAsync_OvernightShift_EveningCheckIn_CountsLateMinutes()
    {
        // Ca qua đêm Sat 22:00 → Sun 06:00.
        // Check-in Sat 22:30 → lateMinutes = 30, status = Late, Date = Saturday.
        var scheduleId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Giả sử schedule.DayOfWeek = today.DayOfWeek (đang check-in tối nay)
        var schedule = new StaffSchedule
        {
            Id = scheduleId,
            StaffUserId = staffId,
            DayOfWeek = today.DayOfWeek,
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(6, 0) // overnight
        };

        _scheduleRepo.Setup(r => r.GetByIdAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);
        _attendanceRepo.Setup(r => r.GetByScheduleAndDateAsync(scheduleId, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftAttendance?)null);

        var sut = CreateService();
        var checkInTime = today.ToDateTime(new TimeOnly(22, 30));
        var result = await sut.CheckInAsync(scheduleId, staffId, new CheckInAttendanceDto
        {
            CheckInTime = checkInTime
        });

        Assert.Equal(30, result.LateMinutes);
        Assert.Equal("Late", result.Status);
        Assert.Equal(today, result.Date); // attendance.Date = hôm nay (ngày bắt đầu ca)
    }

    [Fact]
    public async Task CheckInAsync_OvernightShift_MorningCheckIn_NoLate_DateIsYesterday()
    {
        // GAP-OVERNIGHT-09 fix: Ca qua đêm Sun 02:00 ← start Sat 22:00.
        // Staff check-in lúc Sun 02:30 sáng → lateMinutes = 0 (đang trong ca),
        // attendance.Date = SATURDAY (ngày bắt đầu ca), không phải hôm nay.
        //
        // Để test được: today.DayOfWeek phải là (schedule.DayOfWeek + 1) % 7.
        var scheduleId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var scheduleDow = today.DayOfWeek == DayOfWeek.Sunday
            ? DayOfWeek.Saturday
            : (DayOfWeek)(((int)today.DayOfWeek - 1 + 7) % 7);

        var schedule = new StaffSchedule
        {
            Id = scheduleId,
            StaffUserId = staffId,
            DayOfWeek = scheduleDow,
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(6, 0) // overnight
        };

        var expectedShiftDate = today.AddDays(-1);

        _scheduleRepo.Setup(r => r.GetByIdAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);
        _attendanceRepo.Setup(r => r.GetByScheduleAndDateAsync(scheduleId, expectedShiftDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftAttendance?)null);

        var sut = CreateService();
        // Check-in lúc hôm nay 02:30 sáng (đang trong ca bắt đầu hôm qua)
        var checkInTime = today.ToDateTime(new TimeOnly(2, 30));
        var result = await sut.CheckInAsync(scheduleId, staffId, new CheckInAttendanceDto
        {
            CheckInTime = checkInTime
        });

        // Không trễ (vẫn đang trong ca)
        Assert.Equal(0, result.LateMinutes);
        Assert.Equal("Present", result.Status);
        // attendance.Date là NGÀY BẮT ĐẦU CA (hôm qua), không phải hôm nay
        Assert.Equal(expectedShiftDate, result.Date);
    }

    [Fact]
    public async Task CheckOutAsync_AttendanceNotFound_ThrowsNotFound()
    {
        var attendanceId = Guid.NewGuid();
        _attendanceRepo.Setup(r => r.GetByIdAsync(attendanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftAttendance?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() => sut.CheckOutAsync(
            attendanceId, Guid.NewGuid(), new CheckOutAttendanceDto()));
    }

    [Fact]
    public async Task CheckOutAsync_NotCheckedIn_ThrowsConflict()
    {
        var attendanceId = Guid.NewGuid();
        var attendance = new ShiftAttendance
        {
            Id = attendanceId,
            StaffUserId = Guid.NewGuid(),
            ScheduleId = Guid.NewGuid(),
            Date = new DateOnly(2026, 9, 15),
            CheckInTime = null
        };
        _attendanceRepo.Setup(r => r.GetByIdAsync(attendanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attendance);

        var sut = CreateService();
        await Assert.ThrowsAsync<ConflictException>(() => sut.CheckOutAsync(
            attendanceId, attendance.StaffUserId, new CheckOutAttendanceDto()));
    }

    [Fact]
    public async Task CheckOutAsync_AlreadyCheckedOut_ThrowsConflict()
    {
        var attendanceId = Guid.NewGuid();
        var attendance = new ShiftAttendance
        {
            Id = attendanceId,
            StaffUserId = Guid.NewGuid(),
            ScheduleId = Guid.NewGuid(),
            Date = new DateOnly(2026, 9, 15),
            CheckInTime = new DateTime(2026, 9, 15, 6, 0, 0),
            CheckOutTime = new DateTime(2026, 9, 15, 14, 0, 0)
        };
        _attendanceRepo.Setup(r => r.GetByIdAsync(attendanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attendance);

        var sut = CreateService();
        await Assert.ThrowsAsync<ConflictException>(() => sut.CheckOutAsync(
            attendanceId, attendance.StaffUserId, new CheckOutAttendanceDto()));
    }

    [Fact]
    public async Task CheckOutAsync_NormalShift_EarlyLeave()
    {
        // Ca thường 06:00-14:00, check-in 06:00, check-out 13:00 → EarlyLeave = 60.
        var attendanceId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var date = new DateOnly(2026, 9, 15);
        var schedule = new StaffSchedule
        {
            Id = Guid.NewGuid(),
            StaffUserId = staffId,
            DayOfWeek = DayOfWeek.Tuesday,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(14, 0)
        };

        var attendance = new ShiftAttendance
        {
            Id = attendanceId,
            ScheduleId = schedule.Id,
            StaffUserId = staffId,
            Date = date,
            CheckInTime = date.ToDateTime(new TimeOnly(6, 0)),
            Schedule = schedule
        };

        _attendanceRepo.Setup(r => r.GetByIdAsync(attendanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attendance);

        var sut = CreateService();
        var checkOutTime = date.ToDateTime(new TimeOnly(13, 0));
        var result = await sut.CheckOutAsync(attendanceId, staffId, new CheckOutAttendanceDto
        {
            CheckOutTime = checkOutTime
        });

        Assert.Equal(60, result.EarlyLeaveMinutes);
        Assert.Equal(checkOutTime, result.CheckOutTime);
    }

    [Fact]
    public async Task CheckOutAsync_OvernightShift_CalculatesEarlyLeaveAcrossMidnight()
    {
        // Ca qua đêm Sat 22:00 → Sun 06:00.
        // Check-in Sat 22:00, check-out Sun 05:00 → EarlyLeave = 60 (1 tiếng trước 06:00 ngày hôm sau).
        var attendanceId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var date = new DateOnly(2026, 9, 19); // Saturday
        var schedule = new StaffSchedule
        {
            Id = Guid.NewGuid(),
            StaffUserId = staffId,
            DayOfWeek = DayOfWeek.Saturday,
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(6, 0) // overnight
        };

        var attendance = new ShiftAttendance
        {
            Id = attendanceId,
            ScheduleId = schedule.Id,
            StaffUserId = staffId,
            Date = date,
            CheckInTime = date.ToDateTime(new TimeOnly(22, 0)),
            Schedule = schedule
        };

        _attendanceRepo.Setup(r => r.GetByIdAsync(attendanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attendance);

        var sut = CreateService();
        var checkOutTime = date.AddDays(1).ToDateTime(new TimeOnly(5, 0));
        var result = await sut.CheckOutAsync(attendanceId, staffId, new CheckOutAttendanceDto
        {
            CheckOutTime = checkOutTime
        });

        Assert.Equal(60, result.EarlyLeaveMinutes);
    }

    [Fact]
    public async Task GetByStaffAsync_EndDateBeforeStart_ThrowsBadRequest()
    {
        var sut = CreateService();
        await Assert.ThrowsAsync<BadRequestException>(() => sut.GetByStaffAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 9, 22), new DateOnly(2026, 9, 15)));
    }
}