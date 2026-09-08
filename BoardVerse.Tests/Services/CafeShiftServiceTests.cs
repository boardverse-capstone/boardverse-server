using BoardVerse.Core.DTOs.CafeShift;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho CafeShiftService — quản lý ca làm việc của quán (BR-CAFE-SHIFT).
/// Open shift + Close shift + GetCurrentShift (authz: Admin/Manager/Staff).
/// </summary>
public class CafeShiftServiceTests
{
    private readonly Mock<ICafeShiftRepository> _shiftRepo = new();
    private readonly Mock<ICafeRepository> _cafeRepo = new();

    private CafeShiftService CreateService() => new(
        _shiftRepo.Object,
        _cafeRepo.Object);

    private static Cafe BuildCafe(Guid id, Guid? managerId = null) => new()
    {
        Id = id,
        Name = "Test Cafe",
        Address = "123 Test Street",
        IsActive = true,
        ManagerId = managerId ?? Guid.NewGuid()
    };

    private static CafeShift BuildOpenShift(Guid cafeId) => new()
    {
        Id = Guid.NewGuid(),
        CafeId = cafeId,
        OpenedByUserId = Guid.NewGuid(),
        OpenedAt = DateTime.UtcNow.AddHours(-2),
        OpeningCashBalance = 500_000m,
        ClosingCashBalance = 0,
        TotalRevenue = 1_200_000m,
        TotalSessions = 5,
        Status = ShiftStatus.Open
    };

    #region OpenShiftAsync

    [Fact]
    public async Task OpenShiftAsync_CafeNotFound_ThrowsNotFound()
    {
        var cafeId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cafe?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.OpenShiftAsync(cafeId, Guid.NewGuid(), 1_000_000m));
    }

    [Fact]
    public async Task OpenShiftAsync_AlreadyOpenShift_ThrowsConflict()
    {
        var cafeId = Guid.NewGuid();
        var existingShiftId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _shiftRepo.Setup(r => r.GetCurrentOpenShiftAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CafeShift { Id = existingShiftId, CafeId = cafeId });

        var sut = CreateService();
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            sut.OpenShiftAsync(cafeId, Guid.NewGuid(), 0m));

        Assert.Contains(existingShiftId.ToString(), ex.Message);
    }

    [Fact]
    public async Task OpenShiftAsync_NoOpenShift_PersistsAndReturnsDto()
    {
        var cafeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _shiftRepo.Setup(r => r.GetCurrentOpenShiftAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CafeShift?)null);

        var sut = CreateService();
        var dto = await sut.OpenShiftAsync(cafeId, userId, openingCashBalance: 1_500_000m);

        Assert.Equal(cafeId, dto.CafeId);
        Assert.Equal(userId, dto.OpenedByUserId);
        Assert.Equal(1_500_000m, dto.OpeningCashBalance);
        Assert.Equal(0m, dto.ClosingCashBalance);
        Assert.Equal(ShiftStatus.Open, dto.Status);

        _shiftRepo.Verify(r => r.AddAsync(It.Is<CafeShift>(s =>
            s.CafeId == cafeId &&
            s.OpenedByUserId == userId &&
            s.OpeningCashBalance == 1_500_000m &&
            s.Status == ShiftStatus.Open), It.IsAny<CancellationToken>()), Times.Once);
        _shiftRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OpenShiftAsync_ZeroOpeningCash_Allowed()
    {
        var cafeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _shiftRepo.Setup(r => r.GetCurrentOpenShiftAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CafeShift?)null);

        var sut = CreateService();
        var dto = await sut.OpenShiftAsync(cafeId, userId, 0m);

        Assert.Equal(0m, dto.OpeningCashBalance);
    }

    #endregion

    #region CloseShiftAsync

    [Fact]
    public async Task CloseShiftAsync_ShiftNotFound_ThrowsNotFound()
    {
        var shiftId = Guid.NewGuid();
        _shiftRepo.Setup(r => r.GetByIdAsync(shiftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CafeShift?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.CloseShiftAsync(shiftId, Guid.NewGuid(), 0m));
    }

    [Fact]
    public async Task CloseShiftAsync_AlreadyClosed_ThrowsConflict()
    {
        var shiftId = Guid.NewGuid();
        _shiftRepo.Setup(r => r.GetByIdAsync(shiftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CafeShift
            {
                Id = shiftId,
                CafeId = Guid.NewGuid(),
                Status = ShiftStatus.Closed,
                OpenedAt = DateTime.UtcNow.AddHours(-5),
                ClosedAt = DateTime.UtcNow
            });

        var sut = CreateService();
        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.CloseShiftAsync(shiftId, Guid.NewGuid(), 2_000_000m));
    }

    [Fact]
    public async Task CloseShiftAsync_OpenShift_ClosesAndPersists()
    {
        var shiftId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var existing = new CafeShift
        {
            Id = shiftId,
            CafeId = Guid.NewGuid(),
            OpenedAt = DateTime.UtcNow.AddHours(-3),
            OpeningCashBalance = 1_000_000m,
            Status = ShiftStatus.Open
        };
        _shiftRepo.Setup(r => r.GetByIdAsync(shiftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = CreateService();
        var dto = await sut.CloseShiftAsync(shiftId, userId, closingCashBalance: 2_500_000m);

        Assert.Equal(shiftId, dto.Id);
        Assert.Equal(userId, dto.ClosedByUserId);
        Assert.Equal(2_500_000m, dto.ClosingCashBalance);
        Assert.Equal(ShiftStatus.Closed, dto.Status);
        Assert.NotNull(dto.ClosedAt);

        _shiftRepo.Verify(r => r.UpdateAsync(It.Is<CafeShift>(s =>
            s.Status == ShiftStatus.Closed &&
            s.ClosedByUserId == userId &&
            s.ClosingCashBalance == 2_500_000m), It.IsAny<CancellationToken>()), Times.Once);
        _shiftRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetCurrentShiftAsync — authz

    [Fact]
    public async Task GetCurrentShiftAsync_AdminBypassesAuthz_ReturnsOpenShift()
    {
        var cafeId = Guid.NewGuid();
        var shift = BuildOpenShift(cafeId);
        _shiftRepo.Setup(r => r.GetCurrentOpenShiftAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shift);

        var sut = CreateService();
        var dto = await sut.GetCurrentShiftAsync(cafeId, Guid.NewGuid(), isAdmin: true);

        Assert.NotNull(dto);
        Assert.Equal(shift.Id, dto!.Id);
        // admin path never queries cafe repo for authz
        _cafeRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetCurrentShiftAsync_NotAdminCafeNotFound_ThrowsNotFound()
    {
        var cafeId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cafe?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GetCurrentShiftAsync(cafeId, callerId, isAdmin: false));
    }

    [Fact]
    public async Task GetCurrentShiftAsync_ManagerOfCafe_Allowed()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var shift = BuildOpenShift(cafeId);
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, managerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _shiftRepo.Setup(r => r.GetCurrentOpenShiftAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shift);

        var sut = CreateService();
        var dto = await sut.GetCurrentShiftAsync(cafeId, managerId, isAdmin: false);

        Assert.NotNull(dto);
        Assert.Equal(shift.Id, dto!.Id);
    }

    [Fact]
    public async Task GetCurrentShiftAsync_CafeStaff_Allowed()
    {
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var shift = BuildOpenShift(cafeId);
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _shiftRepo.Setup(r => r.GetCurrentOpenShiftAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shift);

        var sut = CreateService();
        var dto = await sut.GetCurrentShiftAsync(cafeId, staffId, isAdmin: false);

        Assert.NotNull(dto);
    }

    [Fact]
    public async Task GetCurrentShiftAsync_RandomUser_ThrowsForbidden()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, callerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateService();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.GetCurrentShiftAsync(cafeId, callerId, isAdmin: false));
    }

    [Fact]
    public async Task GetCurrentShiftAsync_NoOpenShift_ReturnsNull()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId, managerId));
        _shiftRepo.Setup(r => r.GetCurrentOpenShiftAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CafeShift?)null);

        var sut = CreateService();
        var dto = await sut.GetCurrentShiftAsync(cafeId, managerId, isAdmin: true);

        Assert.Null(dto);
    }

    #endregion

    #region GetShiftHistoryAsync — authz

    [Fact]
    public async Task GetShiftHistoryAsync_AdminSkipsAuthz_ReturnsPagedResult()
    {
        var cafeId = Guid.NewGuid();
        var shifts = new List<CafeShift> { BuildOpenShift(cafeId) };
        _shiftRepo.Setup(r => r.GetHistoryAsync(cafeId, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shifts);
        _shiftRepo.Setup(r => r.GetHistoryCountAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = CreateService();
        var result = await sut.GetShiftHistoryAsync(cafeId, 1, 20, Guid.NewGuid(), isAdmin: true);

        Assert.Single(result.Shifts);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
    }

    [Fact]
    public async Task GetShiftHistoryAsync_NonAdminForbidden_Throws()
    {
        var cafeId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, callerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateService();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.GetShiftHistoryAsync(cafeId, 1, 20, callerId, isAdmin: false));
    }

    #endregion
}
