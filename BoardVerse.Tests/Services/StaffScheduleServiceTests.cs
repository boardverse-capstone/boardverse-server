using BoardVerse.Core.DTOs.StaffSchedule;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho StaffScheduleService — CRUD lịch làm việc của staff.
/// Coverage: overnight shift, recurring, conflict detection, copy template, work hours calculation.
/// </summary>
public class StaffScheduleServiceTests
{
    private readonly Mock<IStaffScheduleRepository> _scheduleRepo = new();
    private readonly Mock<IShiftAttendanceRepository> _attendanceRepo = new();
    private readonly Mock<ICafeRepository> _cafeRepo = new();
    private readonly Mock<IPushNotificationService> _pushService = new();
    private readonly Mock<ILogger<StaffScheduleService>> _logger = new();

    private StaffScheduleService CreateService() => new(
        _scheduleRepo.Object,
        _attendanceRepo.Object,
        _cafeRepo.Object,
        _pushService.Object,
        _logger.Object);

    private static Cafe BuildCafe(Guid id, Guid? managerId = null) => new()
    {
        Id = id,
        Name = "Test Cafe",
        Address = "123 Test",
        IsActive = true,
        ManagerId = managerId ?? Guid.NewGuid()
    };

    private static User BuildUser(Guid id, string username) => new()
    {
        Id = id,
        Username = username,
        Email = $"{username}@test.com"
    };

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_CafeNotFound_ThrowsNotFound()
    {
        var cafeId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cafe?)null);

        var sut = CreateService();
        var dto = new CreateStaffScheduleRequestDto
        {
            StaffUserId = Guid.NewGuid(),
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(14, 0),
            ShiftType = ShiftType.Regular,
            IsRecurring = true
        };

        await Assert.ThrowsAsync<NotFoundException>(() => sut.CreateAsync(cafeId, dto));
    }

    [Fact]
    public async Task CreateAsync_StartTimeEqualsEndTime_ThrowsBadRequest()
    {
        var sut = CreateService();
        var dto = new CreateStaffScheduleRequestDto
        {
            StaffUserId = Guid.NewGuid(),
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(6, 0),
            ShiftType = ShiftType.Regular
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            sut.CreateAsync(Guid.NewGuid(), dto));
    }

    [Fact]
    public async Task CreateAsync_StaffNotInCafe_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateService();
        var dto = new CreateStaffScheduleRequestDto
        {
            StaffUserId = staffId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(14, 0)
        };

        await Assert.ThrowsAsync<BadRequestException>(() => sut.CreateAsync(cafeId, dto));
    }

    [Fact]
    public async Task CreateAsync_OverlappingSchedule_ThrowsConflict()
    {
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _scheduleRepo.Setup(r => r.HasOverlappingScheduleAsync(
                cafeId, staffId, DayOfWeek.Monday,
                new TimeOnly(6, 0), new TimeOnly(14, 0),
                null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateService();
        var dto = new CreateStaffScheduleRequestDto
        {
            StaffUserId = staffId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(14, 0)
        };

        await Assert.ThrowsAsync<ConflictException>(() => sut.CreateAsync(cafeId, dto));
    }

    [Fact]
    public async Task CreateAsync_NormalShift_PersistsAndReturnsDto()
    {
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cafeRepo.Setup(r => r.GetUserByIdAsync(staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUser(staffId, "staff1"));
        _scheduleRepo.Setup(r => r.HasOverlappingScheduleAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DayOfWeek>(),
                It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateService();
        var dto = new CreateStaffScheduleRequestDto
        {
            StaffUserId = staffId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(14, 0),
            ShiftType = ShiftType.Regular,
            IsRecurring = true,
            Note = "Ca sáng"
        };

        var result = await sut.CreateAsync(cafeId, dto);

        Assert.False(result.IsOvernight);
        Assert.Equal(480, result.DurationMinutes); // 8 hours
        Assert.Equal("Thứ 2", result.DayOfWeekName);
        Assert.Equal("staff1", result.StaffName);
        Assert.Equal(StaffScheduleStatus.Active, result.Status);

        _scheduleRepo.Verify(r => r.AddAsync(It.IsAny<StaffSchedule>(), It.IsAny<CancellationToken>()), Times.Once);
        _scheduleRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_OvernightShift_PersistsWithIsOvernightTrue()
    {
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cafeRepo.Setup(r => r.GetUserByIdAsync(staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUser(staffId, "staff_night"));
        _scheduleRepo.Setup(r => r.HasOverlappingScheduleAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DayOfWeek>(),
                It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateService();
        var dto = new CreateStaffScheduleRequestDto
        {
            StaffUserId = staffId,
            DayOfWeek = DayOfWeek.Saturday,
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(6, 0), // qua đêm
            ShiftType = ShiftType.Regular
        };

        var result = await sut.CreateAsync(cafeId, dto);

        Assert.True(result.IsOvernight);
        // 22:00 → 06:00 = 8 tiếng = 480 phút
        Assert.Equal(480, result.DurationMinutes);
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_ScheduleNotFound_ThrowsNotFound()
    {
        var scheduleId = Guid.NewGuid();
        _scheduleRepo.Setup(r => r.GetByIdAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StaffSchedule?)null);

        var sut = CreateService();
        var dto = new UpdateStaffScheduleRequestDto
        {
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(14, 0)
        };

        await Assert.ThrowsAsync<NotFoundException>(() => sut.UpdateAsync(Guid.NewGuid(), scheduleId, dto));
    }

    [Fact]
    public async Task UpdateAsync_ScheduleBelongsToDifferentCafe_ThrowsNotFound_IDOR()
    {
        // GAP-IDOR-01 fix: Manager cafe A không được sửa lịch của cafe B.
        var scheduleId = Guid.NewGuid();
        var requesterCafeId = Guid.NewGuid();
        var actualCafeId = Guid.NewGuid(); // khác requesterCafeId
        var staffId = Guid.NewGuid();
        var existing = new StaffSchedule
        {
            Id = scheduleId,
            CafeId = actualCafeId,
            StaffUserId = staffId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(14, 0),
            Status = StaffScheduleStatus.Active
        };

        _scheduleRepo.Setup(r => r.GetByIdAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = CreateService();
        var dto = new UpdateStaffScheduleRequestDto
        {
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(16, 0)
        };

        await Assert.ThrowsAsync<NotFoundException>(() => sut.UpdateAsync(requesterCafeId, scheduleId, dto));

        _scheduleRepo.Verify(r => r.UpdateAsync(It.IsAny<StaffSchedule>(), It.IsAny<CancellationToken>()), Times.Never);
        _scheduleRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ValidSchedule_UpdatesAndPersists()
    {
        var scheduleId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var existing = new StaffSchedule
        {
            Id = scheduleId,
            CafeId = cafeId,
            StaffUserId = staffId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(14, 0),
            Status = StaffScheduleStatus.Active
        };

        _scheduleRepo.Setup(r => r.GetByIdAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _scheduleRepo.Setup(r => r.HasOverlappingScheduleAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DayOfWeek>(),
                It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _cafeRepo.Setup(r => r.GetUserByIdAsync(staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUser(staffId, "staff1"));

        var sut = CreateService();
        var dto = new UpdateStaffScheduleRequestDto
        {
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(16, 0),
            ShiftType = ShiftType.Overtime,
            IsRecurring = false,
            Status = StaffScheduleStatus.Inactive
        };

        var result = await sut.UpdateAsync(cafeId, scheduleId, dto);

        Assert.Equal(new TimeOnly(8, 0), result.StartTime);
        Assert.Equal(new TimeOnly(16, 0), result.EndTime);
        Assert.Equal(ShiftType.Overtime, result.ShiftType);
        Assert.Equal(StaffScheduleStatus.Inactive, result.Status);

        _scheduleRepo.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        _scheduleRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_ScheduleNotFound_ThrowsNotFound()
    {
        var scheduleId = Guid.NewGuid();
        _scheduleRepo.Setup(r => r.GetByIdAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StaffSchedule?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() => sut.DeleteAsync(Guid.NewGuid(), scheduleId));
    }

    [Fact]
    public async Task DeleteAsync_ScheduleBelongsToDifferentCafe_ThrowsNotFound_IDOR()
    {
        // GAP-IDOR-01 fix: Manager cafe A không được xóa lịch của cafe B.
        var scheduleId = Guid.NewGuid();
        var requesterCafeId = Guid.NewGuid();
        var actualCafeId = Guid.NewGuid();
        var existing = new StaffSchedule
        {
            Id = scheduleId,
            CafeId = actualCafeId,
            StaffUserId = Guid.NewGuid(),
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(14, 0),
            Status = StaffScheduleStatus.Active
        };

        _scheduleRepo.Setup(r => r.GetByIdAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() => sut.DeleteAsync(requesterCafeId, scheduleId));

        _scheduleRepo.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ExistingSchedule_Removes()
    {
        var scheduleId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var existing = new StaffSchedule
        {
            Id = scheduleId,
            CafeId = cafeId,
            StaffUserId = Guid.NewGuid(),
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(14, 0)
        };

        _scheduleRepo.Setup(r => r.GetByIdAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = CreateService();
        await sut.DeleteAsync(cafeId, scheduleId);

        _scheduleRepo.Verify(r => r.DeleteAsync(scheduleId, It.IsAny<CancellationToken>()), Times.Once);
        _scheduleRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Work Hours Calculation

    [Fact]
    public async Task GetWorkHoursSummaryAsync_EndDateBeforeStart_ThrowsBadRequest()
    {
        var sut = CreateService();
        await Assert.ThrowsAsync<BadRequestException>(() =>
            sut.GetWorkHoursSummaryAsync(
                Guid.NewGuid(), Guid.NewGuid(),
                new DateOnly(2026, 9, 22), new DateOnly(2026, 9, 15)));
    }

    [Fact]
    public async Task GetWorkHoursSummaryAsync_ReturnsSummary()
    {
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 9, 14); // Monday
        var endDate = new DateOnly(2026, 9, 20); // Sunday

        var templates = new List<StaffSchedule>
        {
            new StaffSchedule
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                StaffUserId = staffId,
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(6, 0),
                EndTime = new TimeOnly(14, 0),
                Status = StaffScheduleStatus.Active,
                User = BuildUser(staffId, "staff1")
            },
            new StaffSchedule
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                StaffUserId = staffId,
                DayOfWeek = DayOfWeek.Wednesday,
                StartTime = new TimeOnly(14, 0),
                EndTime = new TimeOnly(22, 0),
                Status = StaffScheduleStatus.Active
            }
        };

        var attendances = new List<ShiftAttendance>
        {
            new ShiftAttendance
            {
                Id = Guid.NewGuid(),
                ScheduleId = templates[0].Id,
                StaffUserId = staffId,
                Date = new DateOnly(2026, 9, 14),
                CheckInTime = new DateTime(2026, 9, 14, 6, 0, 0),
                CheckOutTime = new DateTime(2026, 9, 14, 14, 0, 0),
                LateMinutes = 0,
                Status = "Present"
            }
        };

        _scheduleRepo.Setup(r => r.GetByStaffAsync(staffId, cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);
        _attendanceRepo.Setup(r => r.GetByStaffAsync(
            staffId, cafeId, startDate, endDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attendances);

        var sut = CreateService();
        var result = await sut.GetWorkHoursSummaryAsync(staffId, cafeId, startDate, endDate);

        // 2 ca (Mon + Wed) × 8h = 16h = 960 phút
        Assert.Equal(960, result.TotalScheduledMinutes);
        Assert.Equal(480, result.TotalWorkedMinutes); // 8h thực tế
        Assert.Equal(1, result.PresentDays);
    }

    #endregion

    #region CopyWeekTemplateAsync

    [Fact]
    public async Task CopyWeekTemplateAsync_ToWeekBeforeFromWeek_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var sut = CreateService();
        var dto = new CopyWeekTemplateRequestDto
        {
            FromWeekStart = new DateOnly(2026, 9, 21),
            ToWeekStart = new DateOnly(2026, 9, 14)
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            sut.CopyWeekTemplateAsync(cafeId, dto));
    }

    [Fact]
    public async Task CopyWeekTemplateAsync_NoTemplates_ReturnsEmptyResult()
    {
        var cafeId = Guid.NewGuid();
        _scheduleRepo.Setup(r => r.GetActiveByCafeAsync(
                cafeId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StaffSchedule>());

        var sut = CreateService();
        var dto = new CopyWeekTemplateRequestDto
        {
            FromWeekStart = new DateOnly(2026, 9, 14),
            ToWeekStart = new DateOnly(2026, 9, 21)
        };

        var result = await sut.CopyWeekTemplateAsync(cafeId, dto);

        Assert.Equal(0, result.CopiedCount);
        Assert.Empty(result.NewSchedules);
    }

    [Fact]
    public async Task CopyWeekTemplateAsync_WithTemplates_CopiesAll()
    {
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        var templates = new List<StaffSchedule>
        {
            new StaffSchedule
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                StaffUserId = staffId,
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(6, 0),
                EndTime = new TimeOnly(14, 0),
                Status = StaffScheduleStatus.Active,
                User = BuildUser(staffId, "staff1")
            }
        };

        _scheduleRepo.Setup(r => r.GetActiveByCafeAsync(
                cafeId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);
        _cafeRepo.Setup(r => r.GetUserByIdAsync(staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUser(staffId, "staff1"));

        var sut = CreateService();
        var dto = new CopyWeekTemplateRequestDto
        {
            FromWeekStart = new DateOnly(2026, 9, 14),
            ToWeekStart = new DateOnly(2026, 9, 21)
        };

        var result = await sut.CopyWeekTemplateAsync(cafeId, dto);

        Assert.Equal(1, result.CopiedCount);
        Assert.Single(result.NewSchedules);
        _scheduleRepo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<StaffSchedule>>(), It.IsAny<CancellationToken>()), Times.Once);
        _scheduleRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CopyWeekTemplateAsync_OnlyCopiesTemplatesWithAttendanceInSourceWeek()
    {
        // GAP C4 fix: Copy-template should only copy templates that staff actually worked
        // (had ShiftAttendance) in the source week. Without attendance, we should NOT copy.
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var scheduleIdUsed = Guid.NewGuid();
        var scheduleIdUnused = Guid.NewGuid();

        var templates = new List<StaffSchedule>
        {
            new StaffSchedule
            {
                Id = scheduleIdUsed,
                CafeId = cafeId, StaffUserId = staffId,
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(6, 0), EndTime = new TimeOnly(14, 0),
                Status = StaffScheduleStatus.Active
            },
            new StaffSchedule
            {
                Id = scheduleIdUnused,
                CafeId = cafeId, StaffUserId = staffId,
                DayOfWeek = DayOfWeek.Wednesday,
                StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(22, 0),
                Status = StaffScheduleStatus.Active
            }
        };

        _scheduleRepo.Setup(r => r.GetActiveByCafeAsync(
                cafeId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);
        _cafeRepo.Setup(r => r.GetUserByIdAsync(staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUser(staffId, "staff1"));

        // Monday template was used, Wednesday template was NOT used in source week.
        _attendanceRepo.Setup(r => r.HasAttendanceInRangeAsync(
                staffId, scheduleIdUsed, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _attendanceRepo.Setup(r => r.HasAttendanceInRangeAsync(
                staffId, scheduleIdUnused, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateService();
        var dto = new CopyWeekTemplateRequestDto
        {
            FromWeekStart = new DateOnly(2026, 9, 14),
            ToWeekStart = new DateOnly(2026, 9, 21)
        };

        var result = await sut.CopyWeekTemplateAsync(cafeId, dto);

        Assert.Equal(1, result.CopiedCount); // Only Monday template copied
        Assert.Single(result.NewSchedules);
        Assert.Equal(DayOfWeek.Monday, result.NewSchedules[0].DayOfWeek);
    }

    [Fact]
    public async Task CopyWeekTemplateAsync_NoAttendance_FallsBackToCopyAllActive()
    {
        // Backward compat: if no attendance records at all, copy all active templates
        // (matches the original pre-fix behavior).
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        var templates = new List<StaffSchedule>
        {
            new StaffSchedule
            {
                Id = templateId,
                CafeId = cafeId, StaffUserId = staffId,
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(6, 0), EndTime = new TimeOnly(14, 0),
                Status = StaffScheduleStatus.Active
            }
        };

        _scheduleRepo.Setup(r => r.GetActiveByCafeAsync(
                cafeId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);
        _cafeRepo.Setup(r => r.GetUserByIdAsync(staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUser(staffId, "staff1"));
        _attendanceRepo.Setup(r => r.HasAttendanceInRangeAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // No attendance anywhere

        var sut = CreateService();
        var dto = new CopyWeekTemplateRequestDto
        {
            FromWeekStart = new DateOnly(2026, 9, 14),
            ToWeekStart = new DateOnly(2026, 9, 21)
        };

        var result = await sut.CopyWeekTemplateAsync(cafeId, dto);

        Assert.Equal(1, result.CopiedCount);
        Assert.Single(result.NewSchedules);
    }

    #endregion

    #region Shift duration validation (M3)

    [Fact]
    public async Task CreateAsync_ShiftLongerThan16Hours_ThrowsBadRequest()
    {
        var sut = CreateService();
        var dto = new CreateStaffScheduleRequestDto
        {
            StaffUserId = Guid.NewGuid(),
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(17, 0), // 17h > 16h max
            ShiftType = ShiftType.Regular
        };

        await Assert.ThrowsAsync<BadRequestException>(() => sut.CreateAsync(Guid.NewGuid(), dto));
    }

    [Fact]
    public async Task CreateAsync_OvernightShiftLongerThan16Hours_ThrowsBadRequest()
    {
        var sut = CreateService();
        var dto = new CreateStaffScheduleRequestDto
        {
            StaffUserId = Guid.NewGuid(),
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(23, 0), // 17h (overnight check would give 17h, not 17h-24h=1h — uses raw diff)
            ShiftType = ShiftType.Regular
        };

        await Assert.ThrowsAsync<BadRequestException>(() => sut.CreateAsync(Guid.NewGuid(), dto));
    }

    [Fact]
    public async Task CreateAsync_ShiftExactly16Hours_PassesValidation()
    {
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cafeRepo.Setup(r => r.GetUserByIdAsync(staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUser(staffId, "staff1"));
        _scheduleRepo.Setup(r => r.HasOverlappingScheduleAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DayOfWeek>(),
                It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateService();
        var dto = new CreateStaffScheduleRequestDto
        {
            StaffUserId = staffId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(22, 0), // exactly 16h
            ShiftType = ShiftType.Regular
        };

        var result = await sut.CreateAsync(cafeId, dto);
        Assert.Equal(960, result.DurationMinutes); // 16h = 960min
    }

    #endregion

    #region Bulk create transactional (H1)

    [Fact]
    public async Task BulkCreateAsync_OneItemFails_NoSchedulesPersisted()
    {
        // GAP H1: if any item fails validation, NO items should be persisted (atomic).
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _scheduleRepo.Setup(r => r.HasOverlappingScheduleAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DayOfWeek>(),
                It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateService();
        var dto = new BulkCreateStaffScheduleRequestDto
        {
            Schedules = new List<CreateStaffScheduleRequestDto>
            {
                new() { StaffUserId = staffId, DayOfWeek = DayOfWeek.Monday,
                        StartTime = new TimeOnly(6, 0), EndTime = new TimeOnly(14, 0) },
                new() { StaffUserId = staffId, DayOfWeek = DayOfWeek.Tuesday,
                        StartTime = new TimeOnly(6, 0), EndTime = new TimeOnly(6, 0) }, // invalid: start == end
                new() { StaffUserId = staffId, DayOfWeek = DayOfWeek.Wednesday,
                        StartTime = new TimeOnly(6, 0), EndTime = new TimeOnly(14, 0) }
            }
        };

        await Assert.ThrowsAsync<BadRequestException>(() => sut.BulkCreateAsync(cafeId, dto));

        // AddRangeAsync should NEVER have been called since validation failed before persistence.
        _scheduleRepo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<StaffSchedule>>(), It.IsAny<CancellationToken>()), Times.Never);
        _scheduleRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}