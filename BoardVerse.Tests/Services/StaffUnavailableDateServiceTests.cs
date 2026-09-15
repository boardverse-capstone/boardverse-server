using BoardVerse.Core.DTOs.StaffSchedule;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho StaffUnavailableDateService — focus vào GAP C6 fix:
/// authorization khi xóa ngày nghỉ (caller phải là chủ nhân của record).
/// </summary>
public class StaffUnavailableDateServiceTests
{
    private readonly Mock<IStaffUnavailableDateRepository> _unavailableRepo = new();
    private readonly Mock<ICafeRepository> _cafeRepo = new();

    private BoardVerse.Services.Services.StaffUnavailableDateService CreateService() => new(
        _unavailableRepo.Object,
        _cafeRepo.Object);

    [Fact]
    public async Task DeleteAsync_RecordBelongsToCaller_DeletesSuccessfully()
    {
        var staffId = Guid.NewGuid();
        var record = new StaffUnavailableDate
        {
            Id = Guid.NewGuid(),
            StaffUserId = staffId,
            CafeId = Guid.NewGuid(),
            Date = new DateOnly(2026, 9, 20),
            CreatedAt = DateTime.UtcNow
        };
        _unavailableRepo.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var sut = CreateService();
        await sut.DeleteAsync(record.Id, staffId);

        _unavailableRepo.Verify(r => r.DeleteAsync(record.Id, It.IsAny<CancellationToken>()), Times.Once);
        _unavailableRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_RecordBelongsToDifferentStaff_ThrowsForbidden()
    {
        // GAP C6 fix: staff A không được xóa ngày nghỉ của staff B.
        var ownerStaffId = Guid.NewGuid();
        var callerStaffId = Guid.NewGuid();
        var record = new StaffUnavailableDate
        {
            Id = Guid.NewGuid(),
            StaffUserId = ownerStaffId,
            CafeId = Guid.NewGuid(),
            Date = new DateOnly(2026, 9, 20),
            CreatedAt = DateTime.UtcNow
        };
        _unavailableRepo.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var sut = CreateService();
        await Assert.ThrowsAsync<ForbiddenException>(() => sut.DeleteAsync(record.Id, callerStaffId));

        _unavailableRepo.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_RecordNotFound_ThrowsNotFound()
    {
        var id = Guid.NewGuid();
        _unavailableRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StaffUnavailableDate?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() => sut.DeleteAsync(id, Guid.NewGuid()));

        _unavailableRepo.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_CafeNotFound_ThrowsNotFound()
    {
        // GAP-VALIDATION-05 fix: cafeId random → 404.
        var cafeId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cafe?)null);

        var sut = CreateService();
        var dto = new CreateUnavailableDateDto { Date = new DateOnly(2026, 9, 20) };

        await Assert.ThrowsAsync<NotFoundException>(() => sut.CreateAsync(cafeId, Guid.NewGuid(), dto));
    }

    [Fact]
    public async Task CreateAsync_AlreadyExists_ThrowsConflict()
    {
        var cafeId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsStaffMemberExistsAsync(cafeId, staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _unavailableRepo.Setup(r => r.ExistsAsync(staffId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateService();
        var dto = new CreateUnavailableDateDto { Date = new DateOnly(2026, 9, 20) };

        await Assert.ThrowsAsync<ConflictException>(() => sut.CreateAsync(cafeId, staffId, dto));
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
        var dto = new CreateUnavailableDateDto { Date = new DateOnly(2026, 9, 20) };

        await Assert.ThrowsAsync<BadRequestException>(() => sut.CreateAsync(cafeId, staffId, dto));
    }

    private static Cafe BuildCafe(Guid id) => new()
    {
        Id = id,
        Name = "Test Cafe",
        Address = "123 Test Street"
    };
}
