using BoardVerse.Core.DTOs.StaffSchedule;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho TimeOffRequestService — focus vào GAP-VALIDATION-04 fix:
/// validate cafe tồn tại TRƯỚC khi xử lý các logic khác.
/// </summary>
public class TimeOffRequestServiceTests
{
    private readonly Mock<ITimeOffRequestRepository> _requestRepo = new();
    private readonly Mock<IStaffUnavailableDateRepository> _unavailableRepo = new();
    private readonly Mock<ICafeRepository> _cafeRepo = new();
    private readonly Mock<IPushNotificationService> _pushService = new();
    private readonly Mock<ILogger<TimeOffRequestService>> _logger = new();

    private BoardVerse.Services.Services.TimeOffRequestService CreateService() => new(
        _requestRepo.Object,
        _unavailableRepo.Object,
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
    public async Task CreateAsync_EndDateBeforeStart_ThrowsBadRequest()
    {
        var sut = CreateService();
        var dto = new CreateTimeOffRequestDto
        {
            StartDate = new DateOnly(2026, 9, 22),
            EndDate = new DateOnly(2026, 9, 15)
        };

        await Assert.ThrowsAsync<BadRequestException>(() => sut.CreateAsync(
            Guid.NewGuid(), Guid.NewGuid(), dto));
    }

    [Fact]
    public async Task CreateAsync_CafeNotFound_ThrowsNotFound()
    {
        // GAP-VALIDATION-04 fix: cafeId random GUID → 404 thay vì 400 "không phải nhân viên".
        var cafeId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cafe?)null);

        var sut = CreateService();
        var dto = new CreateTimeOffRequestDto
        {
            StartDate = new DateOnly(2026, 9, 15),
            EndDate = new DateOnly(2026, 9, 17)
        };

        await Assert.ThrowsAsync<NotFoundException>(() => sut.CreateAsync(
            cafeId, Guid.NewGuid(), dto));
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
        var dto = new CreateTimeOffRequestDto
        {
            StartDate = new DateOnly(2026, 9, 15),
            EndDate = new DateOnly(2026, 9, 17)
        };

        await Assert.ThrowsAsync<BadRequestException>(() => sut.CreateAsync(
            cafeId, staffId, dto));
    }

    [Fact]
    public async Task CreateAsync_ManagerRequestingOwnTimeOff_PassesStaffCheck()
    {
        // Manager có thể tự tạo time-off request cho chính mình (Manager KHÔNG có trong staff member list
        // nhưng vẫn thuộc cafe vì cafe.ManagerId == managerId).
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, managerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Manager không có trong staff members list

        _requestRepo.Setup(r => r.AddAsync(It.IsAny<TimeOffRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _requestRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _requestRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken ct) => new TimeOffRequest
            {
                Id = id,
                CafeId = cafeId,
                StaffUserId = managerId,
                StartDate = new DateOnly(2026, 9, 15),
                EndDate = new DateOnly(2026, 9, 17),
                Status = TimeOffStatus.Pending
            });

        var sut = CreateService();
        var dto = new CreateTimeOffRequestDto
        {
            StartDate = new DateOnly(2026, 9, 15),
            EndDate = new DateOnly(2026, 9, 17)
        };

        var result = await sut.CreateAsync(cafeId, managerId, dto);
        Assert.NotNull(result);
        Assert.Equal(managerId, result.StaffUserId);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_PersistsAndReturns()
    {
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _requestRepo.Setup(r => r.AddAsync(It.IsAny<TimeOffRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _requestRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _requestRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken ct) => new TimeOffRequest
            {
                Id = id,
                CafeId = cafeId,
                StaffUserId = staffId,
                StartDate = new DateOnly(2026, 9, 15),
                EndDate = new DateOnly(2026, 9, 17),
                Status = TimeOffStatus.Pending
            });

        var sut = CreateService();
        var dto = new CreateTimeOffRequestDto
        {
            StartDate = new DateOnly(2026, 9, 15),
            EndDate = new DateOnly(2026, 9, 17),
            Reason = "Nghỉ ốm"
        };

        var result = await sut.CreateAsync(cafeId, staffId, dto);

        Assert.Equal(TimeOffStatus.Pending, result.Status);
        Assert.Equal(staffId, result.StaffUserId);
        _requestRepo.Verify(r => r.AddAsync(It.IsAny<TimeOffRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _requestRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ReviewAsync

    [Fact]
    public async Task ReviewAsync_RequestNotFound_ThrowsNotFound()
    {
        var requestId = Guid.NewGuid();
        _requestRepo.Setup(r => r.GetByIdAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeOffRequest?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() => sut.ReviewAsync(
            requestId, Guid.NewGuid(),
            new ReviewTimeOffRequestDto { Status = TimeOffStatus.Approved }));
    }

    [Fact]
    public async Task ReviewAsync_AlreadyReviewed_ThrowsConflict()
    {
        var requestId = Guid.NewGuid();
        var request = new TimeOffRequest
        {
            Id = requestId,
            Status = TimeOffStatus.Approved // đã duyệt rồi
        };
        _requestRepo.Setup(r => r.GetByIdAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var sut = CreateService();
        await Assert.ThrowsAsync<ConflictException>(() => sut.ReviewAsync(
            requestId, Guid.NewGuid(),
            new ReviewTimeOffRequestDto { Status = TimeOffStatus.Rejected }));
    }

    [Fact]
    public async Task ReviewAsync_Approved_CreatesStaffUnavailableDatesForRange()
    {
        // Khi duyệt 3 ngày nghỉ → tạo 3 bản ghi StaffUnavailableDate.
        var requestId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var cafeId = Guid.NewGuid();
        var request = new TimeOffRequest
        {
            Id = requestId,
            CafeId = cafeId,
            StaffUserId = staffId,
            StartDate = new DateOnly(2026, 9, 15),
            EndDate = new DateOnly(2026, 9, 17),
            Reason = "Nghỉ phép",
            Status = TimeOffStatus.Pending
        };
        _requestRepo.Setup(r => r.GetByIdAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        _unavailableRepo.Setup(r => r.ExistsAsync(staffId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // không có ngày nào trùng
        _unavailableRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<StaffUnavailableDate>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateService();
        var result = await sut.ReviewAsync(
            requestId, Guid.NewGuid(),
            new ReviewTimeOffRequestDto { Status = TimeOffStatus.Approved });

        Assert.Equal(TimeOffStatus.Approved, result.Status);
        _unavailableRepo.Verify(r => r.AddRangeAsync(
            It.Is<IEnumerable<StaffUnavailableDate>>(l => l.Count() == 3),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReviewAsync_Approved_DuplicateDateSkipped()
    {
        // Nếu staff đã tự đánh dấu 1 ngày nghỉ cố định trùng với khoảng nghỉ phép,
        // service KHÔNG tạo thêm bản ghi StaffUnavailableDate trùng ngày đó.
        var requestId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var request = new TimeOffRequest
        {
            Id = requestId,
            CafeId = Guid.NewGuid(),
            StaffUserId = staffId,
            StartDate = new DateOnly(2026, 9, 15),
            EndDate = new DateOnly(2026, 9, 17),
            Status = TimeOffStatus.Pending
        };
        _requestRepo.Setup(r => r.GetByIdAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        // 15/9 đã có sẵn (staff tự thêm), 16/9 và 17/9 chưa có.
        _unavailableRepo.Setup(r => r.ExistsAsync(staffId, new DateOnly(2026, 9, 15), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _unavailableRepo.Setup(r => r.ExistsAsync(staffId, new DateOnly(2026, 9, 16), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _unavailableRepo.Setup(r => r.ExistsAsync(staffId, new DateOnly(2026, 9, 17), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateService();
        await sut.ReviewAsync(requestId, Guid.NewGuid(),
            new ReviewTimeOffRequestDto { Status = TimeOffStatus.Approved });

        _unavailableRepo.Verify(r => r.AddRangeAsync(
            It.Is<IEnumerable<StaffUnavailableDate>>(l => l.Count() == 2), // 16/9 + 17/9
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReviewAsync_Rejected_DoesNotCreateUnavailableDates()
    {
        var requestId = Guid.NewGuid();
        var request = new TimeOffRequest
        {
            Id = requestId,
            CafeId = Guid.NewGuid(),
            StaffUserId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 9, 15),
            EndDate = new DateOnly(2026, 9, 17),
            Status = TimeOffStatus.Pending
        };
        _requestRepo.Setup(r => r.GetByIdAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var sut = CreateService();
        await sut.ReviewAsync(requestId, Guid.NewGuid(),
            new ReviewTimeOffRequestDto { Status = TimeOffStatus.Rejected });

        _unavailableRepo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<StaffUnavailableDate>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}