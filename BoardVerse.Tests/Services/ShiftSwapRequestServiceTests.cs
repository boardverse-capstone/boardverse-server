using BoardVerse.Core.DTOs.StaffSchedule;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho ShiftSwapRequestService — focus vào GAP-VALIDATION-02/03 fix:
/// validate cafe ownership của schedules + target staff thuộc cafe.
/// </summary>
public class ShiftSwapRequestServiceTests
{
    private readonly Mock<IShiftSwapRequestRepository> _swapRepo = new();
    private readonly Mock<IStaffScheduleRepository> _scheduleRepo = new();
    private readonly Mock<ICafeRepository> _cafeRepo = new();
    private readonly Mock<IPushNotificationService> _pushService = new();
    private readonly Mock<ILogger<ShiftSwapRequestService>> _logger = new();

    private BoardVerse.Services.Services.ShiftSwapRequestService CreateService() => new(
        _swapRepo.Object,
        _scheduleRepo.Object,
        _cafeRepo.Object,
        _pushService.Object,
        _logger.Object);

    private static Cafe BuildCafe(Guid id, Guid? managerId = null) => new()
    {
        Id = id,
        Name = "Test Cafe",
        Address = "123 Test Street",
        ManagerId = managerId ?? Guid.NewGuid()
    };

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_CafeNotFound_ThrowsNotFound()
    {
        // GAP-VALIDATION-02 fix: cafeId random GUID → 404.
        var cafeId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cafe?)null);

        var sut = CreateService();
        var dto = new CreateShiftSwapRequestDto
        {
            TargetStaffId = Guid.NewGuid(),
            RequesterScheduleId = Guid.NewGuid(),
            TargetScheduleId = Guid.NewGuid()
        };

        await Assert.ThrowsAsync<NotFoundException>(() => sut.CreateAsync(
            cafeId, Guid.NewGuid(), dto));
    }

    [Fact]
    public async Task CreateAsync_SameSchedule_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var requesterScheduleId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));

        var sut = CreateService();
        var dto = new CreateShiftSwapRequestDto
        {
            TargetStaffId = Guid.NewGuid(),
            RequesterScheduleId = requesterScheduleId,
            TargetScheduleId = requesterScheduleId // same
        };

        await Assert.ThrowsAsync<BadRequestException>(() => sut.CreateAsync(
            cafeId, Guid.NewGuid(), dto));
    }

    [Fact]
    public async Task CreateAsync_SameStaff_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));

        var sut = CreateService();
        var dto = new CreateShiftSwapRequestDto
        {
            TargetStaffId = staffId, // same as requester
            RequesterScheduleId = Guid.NewGuid(),
            TargetScheduleId = Guid.NewGuid()
        };

        await Assert.ThrowsAsync<BadRequestException>(() => sut.CreateAsync(
            cafeId, staffId, dto));
    }

    [Fact]
    public async Task CreateAsync_RequesterScheduleBelongsToDifferentCafe_ThrowsNotFound()
    {
        // GAP-VALIDATION-02 fix: schedule thuộc cafe khác → 404 (không phải 400).
        var cafeId = Guid.NewGuid();
        var otherCafeId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var requesterScheduleId = Guid.NewGuid();

        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _scheduleRepo.Setup(r => r.GetByIdAsync(requesterScheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffSchedule
            {
                Id = requesterScheduleId,
                CafeId = otherCafeId, // schedule thuộc cafe khác
                StaffUserId = requesterId,
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(6, 0),
                EndTime = new TimeOnly(14, 0)
            });

        var sut = CreateService();
        var dto = new CreateShiftSwapRequestDto
        {
            TargetStaffId = Guid.NewGuid(),
            RequesterScheduleId = requesterScheduleId,
            TargetScheduleId = Guid.NewGuid()
        };

        await Assert.ThrowsAsync<NotFoundException>(() => sut.CreateAsync(
            cafeId, requesterId, dto));
    }

    [Fact]
    public async Task CreateAsync_TargetScheduleBelongsToDifferentCafe_ThrowsNotFound()
    {
        // GAP-VALIDATION-02 fix: target schedule thuộc cafe khác → 404.
        var cafeId = Guid.NewGuid();
        var otherCafeId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var targetStaffId = Guid.NewGuid();
        var requesterScheduleId = Guid.NewGuid();
        var targetScheduleId = Guid.NewGuid();

        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _scheduleRepo.Setup(r => r.GetByIdAsync(requesterScheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffSchedule
            {
                Id = requesterScheduleId,
                CafeId = cafeId,
                StaffUserId = requesterId,
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(6, 0),
                EndTime = new TimeOnly(14, 0)
            });
        _scheduleRepo.Setup(r => r.GetByIdAsync(targetScheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffSchedule
            {
                Id = targetScheduleId,
                CafeId = otherCafeId, // target thuộc cafe khác
                StaffUserId = targetStaffId,
                DayOfWeek = DayOfWeek.Tuesday,
                StartTime = new TimeOnly(14, 0),
                EndTime = new TimeOnly(22, 0)
            });

        var sut = CreateService();
        var dto = new CreateShiftSwapRequestDto
        {
            TargetStaffId = targetStaffId,
            RequesterScheduleId = requesterScheduleId,
            TargetScheduleId = targetScheduleId
        };

        await Assert.ThrowsAsync<NotFoundException>(() => sut.CreateAsync(
            cafeId, requesterId, dto));
    }

    [Fact]
    public async Task CreateAsync_TargetStaffNotInCafe_ThrowsBadRequest()
    {
        // GAP-VALIDATION-03 fix: target staff GUID random không thuộc cafe → 400.
        var cafeId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var targetStaffId = Guid.NewGuid();
        var requesterScheduleId = Guid.NewGuid();
        var targetScheduleId = Guid.NewGuid();

        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _scheduleRepo.Setup(r => r.GetByIdAsync(requesterScheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffSchedule
            {
                Id = requesterScheduleId,
                CafeId = cafeId,
                StaffUserId = requesterId,
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(6, 0),
                EndTime = new TimeOnly(14, 0)
            });
        _scheduleRepo.Setup(r => r.GetByIdAsync(targetScheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffSchedule
            {
                Id = targetScheduleId,
                CafeId = cafeId,
                StaffUserId = targetStaffId,
                DayOfWeek = DayOfWeek.Tuesday,
                StartTime = new TimeOnly(14, 0),
                EndTime = new TimeOnly(22, 0)
            });
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, targetStaffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // target staff KHÔNG thuộc cafe

        var sut = CreateService();
        var dto = new CreateShiftSwapRequestDto
        {
            TargetStaffId = targetStaffId,
            RequesterScheduleId = requesterScheduleId,
            TargetScheduleId = targetScheduleId
        };

        await Assert.ThrowsAsync<BadRequestException>(() => sut.CreateAsync(
            cafeId, requesterId, dto));
    }

    [Fact]
    public async Task CreateAsync_RequesterScheduleNotOwnedByRequester_ThrowsBadRequest()
    {
        // Schedule thuộc staff khác → không thể swap.
        var cafeId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var otherStaffId = Guid.NewGuid();
        var targetStaffId = Guid.NewGuid();
        var requesterScheduleId = Guid.NewGuid();
        var targetScheduleId = Guid.NewGuid();

        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _scheduleRepo.Setup(r => r.GetByIdAsync(requesterScheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffSchedule
            {
                Id = requesterScheduleId,
                CafeId = cafeId,
                StaffUserId = otherStaffId, // schedule của staff khác
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(6, 0),
                EndTime = new TimeOnly(14, 0)
            });
        _scheduleRepo.Setup(r => r.GetByIdAsync(targetScheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffSchedule
            {
                Id = targetScheduleId,
                CafeId = cafeId,
                StaffUserId = targetStaffId,
                DayOfWeek = DayOfWeek.Tuesday,
                StartTime = new TimeOnly(14, 0),
                EndTime = new TimeOnly(22, 0)
            });

        var sut = CreateService();
        var dto = new CreateShiftSwapRequestDto
        {
            TargetStaffId = targetStaffId,
            RequesterScheduleId = requesterScheduleId,
            TargetScheduleId = targetScheduleId
        };

        await Assert.ThrowsAsync<BadRequestException>(() => sut.CreateAsync(
            cafeId, requesterId, dto));
    }

    [Fact]
    public async Task CreateAsync_ValidSwap_PersistsAndReturns()
    {
        var cafeId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var targetStaffId = Guid.NewGuid();
        var requesterScheduleId = Guid.NewGuid();
        var targetScheduleId = Guid.NewGuid();

        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _scheduleRepo.Setup(r => r.GetByIdAsync(requesterScheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffSchedule
            {
                Id = requesterScheduleId,
                CafeId = cafeId,
                StaffUserId = requesterId,
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(6, 0),
                EndTime = new TimeOnly(14, 0)
            });
        _scheduleRepo.Setup(r => r.GetByIdAsync(targetScheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffSchedule
            {
                Id = targetScheduleId,
                CafeId = cafeId,
                StaffUserId = targetStaffId,
                DayOfWeek = DayOfWeek.Tuesday,
                StartTime = new TimeOnly(14, 0),
                EndTime = new TimeOnly(22, 0)
            });
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, targetStaffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _swapRepo.Setup(r => r.AddAsync(It.IsAny<ShiftSwapRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _swapRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _swapRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken ct) => new ShiftSwapRequest
            {
                Id = id,
                CafeId = cafeId,
                RequesterId = requesterId,
                TargetStaffId = targetStaffId,
                RequesterScheduleId = requesterScheduleId,
                TargetScheduleId = targetScheduleId,
                Status = ShiftSwapStatus.Pending
            });

        var sut = CreateService();
        var dto = new CreateShiftSwapRequestDto
        {
            TargetStaffId = targetStaffId,
            RequesterScheduleId = requesterScheduleId,
            TargetScheduleId = targetScheduleId
        };

        var result = await sut.CreateAsync(cafeId, requesterId, dto);

        Assert.Equal(ShiftSwapStatus.Pending, result.Status);
        Assert.Equal(requesterId, result.RequesterId);
        _swapRepo.Verify(r => r.AddAsync(It.IsAny<ShiftSwapRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ReviewAsync

    [Fact]
    public async Task ReviewAsync_RequestNotFound_ThrowsNotFound()
    {
        var requestId = Guid.NewGuid();
        _swapRepo.Setup(r => r.GetByIdAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftSwapRequest?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() => sut.ReviewAsync(
            requestId, Guid.NewGuid(),
            new ReviewShiftSwapRequestDto { Status = ShiftSwapStatus.Approved }));
    }

    [Fact]
    public async Task ReviewAsync_AlreadyReviewed_ThrowsConflict()
    {
        var requestId = Guid.NewGuid();
        var request = new ShiftSwapRequest
        {
            Id = requestId,
            Status = ShiftSwapStatus.Approved // đã duyệt
        };
        _swapRepo.Setup(r => r.GetByIdAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var sut = CreateService();
        await Assert.ThrowsAsync<ConflictException>(() => sut.ReviewAsync(
            requestId, Guid.NewGuid(),
            new ReviewShiftSwapRequestDto { Status = ShiftSwapStatus.Rejected }));
    }

    [Fact]
    public async Task ReviewAsync_Approved_SwapsStaffUserIdBetweenSchedules()
    {
        var requestId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var targetStaffId = Guid.NewGuid();
        var requesterScheduleId = Guid.NewGuid();
        var targetScheduleId = Guid.NewGuid();

        var requesterSchedule = new StaffSchedule
        {
            Id = requesterScheduleId,
            StaffUserId = requesterId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(14, 0)
        };
        var targetSchedule = new StaffSchedule
        {
            Id = targetScheduleId,
            StaffUserId = targetStaffId,
            DayOfWeek = DayOfWeek.Tuesday,
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(22, 0)
        };
        var request = new ShiftSwapRequest
        {
            Id = requestId,
            CafeId = Guid.NewGuid(),
            RequesterId = requesterId,
            TargetStaffId = targetStaffId,
            RequesterScheduleId = requesterScheduleId,
            TargetScheduleId = targetScheduleId,
            Status = ShiftSwapStatus.Pending
        };
        _swapRepo.Setup(r => r.GetByIdAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        _scheduleRepo.Setup(r => r.GetByIdAsync(requesterScheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(requesterSchedule);
        _scheduleRepo.Setup(r => r.GetByIdAsync(targetScheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetSchedule);

        var sut = CreateService();
        await sut.ReviewAsync(requestId, Guid.NewGuid(),
            new ReviewShiftSwapRequestDto { Status = ShiftSwapStatus.Approved });

        // Sau khi swap: requesterSchedule giờ thuộc targetStaffId, targetSchedule thuộc requesterId.
        Assert.Equal(targetStaffId, requesterSchedule.StaffUserId);
        Assert.Equal(requesterId, targetSchedule.StaffUserId);

        _scheduleRepo.Verify(r => r.UpdateAsync(requesterSchedule, It.IsAny<CancellationToken>()), Times.Once);
        _scheduleRepo.Verify(r => r.UpdateAsync(targetSchedule, It.IsAny<CancellationToken>()), Times.Once);
        _swapRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReviewAsync_Rejected_DoesNotSwapStaffUserId()
    {
        // Khi reject, KHÔNG swap StaffUserId giữa 2 schedules — staff vẫn giữ ca ban đầu.
        var requestId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var targetStaffId = Guid.NewGuid();
        var requesterScheduleId = Guid.NewGuid();
        var targetScheduleId = Guid.NewGuid();

        var requesterSchedule = new StaffSchedule
        {
            Id = requesterScheduleId,
            StaffUserId = requesterId, // ban đầu của requester
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(14, 0)
        };
        var targetSchedule = new StaffSchedule
        {
            Id = targetScheduleId,
            StaffUserId = targetStaffId, // ban đầu của target
            DayOfWeek = DayOfWeek.Tuesday,
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(22, 0)
        };
        var request = new ShiftSwapRequest
        {
            Id = requestId,
            CafeId = Guid.NewGuid(),
            RequesterId = requesterId,
            TargetStaffId = targetStaffId,
            RequesterScheduleId = requesterScheduleId,
            TargetScheduleId = targetScheduleId,
            Status = ShiftSwapStatus.Pending
        };
        _swapRepo.Setup(r => r.GetByIdAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        _scheduleRepo.Setup(r => r.GetByIdAsync(requesterScheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(requesterSchedule);
        _scheduleRepo.Setup(r => r.GetByIdAsync(targetScheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetSchedule);

        var sut = CreateService();
        await sut.ReviewAsync(requestId, Guid.NewGuid(),
            new ReviewShiftSwapRequestDto { Status = ShiftSwapStatus.Rejected });

        // QUAN TRỌNG: StaffUserId KHÔNG bị swap.
        Assert.Equal(requesterId, requesterSchedule.StaffUserId);
        Assert.Equal(targetStaffId, targetSchedule.StaffUserId);

        // Không gọi UpdateAsync trên schedule — chỉ gọi trên swap request.
        _scheduleRepo.Verify(r => r.UpdateAsync(It.IsAny<StaffSchedule>(), It.IsAny<CancellationToken>()), Times.Never);
        _swapRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}