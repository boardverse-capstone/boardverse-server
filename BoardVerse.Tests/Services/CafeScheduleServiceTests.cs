using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.CafeSchedule;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho CafeScheduleService — quản lý CafeScheduleOverride.
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot - dùng ApplyDate/OpenTime/CloseTime.
/// GAP-2: validate input ngày quá khứ + open=close.
/// GAP-8: single query authz (IsManagerOrStaffAsync).
/// </summary>
public class CafeScheduleServiceTests
{
    private readonly Mock<ICafeScheduleOverrideRepository> _overrideRepo = new();
    private readonly Mock<ICafeRepository> _cafeRepo = new();

    private static BoardVerse.Data.BoardVerseDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<BoardVerse.Data.BoardVerseDbContext>()
            .UseInMemoryDatabase($"CafeScheduleServiceTests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new BoardVerse.Data.BoardVerseDbContext(options);
    }

    private CafeScheduleService CreateService(BoardVerse.Data.BoardVerseDbContext? db = null) =>
        new(_overrideRepo.Object, _cafeRepo.Object, db ?? CreateInMemoryDb());

    private static Cafe BuildCafe(Guid id) => new()
    {
        Id = id,
        Name = "Test Cafe",
        Address = "123 Test Street",
        IsActive = true
    };

    private static UpsertCafeScheduleOverrideRequestDto BuildRequest(
        DateOnly? applyDate = null,
        TimeOnly? openTime = null,
        TimeOnly? closeTime = null,
        bool isClosed = false) =>
        new()
        {
            ApplyDate = applyDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)),
            OpenTime = openTime ?? CafeSchedule.DefaultOpenTime,
            CloseTime = closeTime ?? CafeSchedule.DefaultCloseTime,
            IsClosed = isClosed
        };

    #region GetScheduleAsync

    [Fact]
    public async Task GetScheduleAsync_CafeNotFound_ThrowsNotFound()
    {
        var cafeId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cafe?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GetScheduleAsync(cafeId));
    }

    [Fact]
    public async Task GetScheduleAsync_NoOverrides_ReturnsDefaultTimes()
    {
        var cafeId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _overrideRepo.Setup(r => r.ListByCafeAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeScheduleOverride>());

        var sut = CreateService();
        var dto = await sut.GetScheduleAsync(cafeId);

        Assert.Equal(cafeId, dto.CafeId);
        Assert.Equal(CafeSchedule.DefaultOpenTime, dto.DefaultOpenTime);
        Assert.Equal(CafeSchedule.DefaultCloseTime, dto.DefaultCloseTime);
        Assert.Empty(dto.Days);
    }

    [Fact]
    public async Task GetScheduleAsync_WithManagerUserId_CallsIsManagerOrStaffAuthz()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, managerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _overrideRepo.Setup(r => r.ListByCafeAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CafeScheduleOverride>());

        var sut = CreateService();
        await sut.GetScheduleAsync(cafeId, managerId);

        _cafeRepo.Verify(r => r.IsManagerOrStaffAsync(cafeId, managerId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetScheduleAsync_ManagerNotAuthorized_ThrowsForbidden()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, managerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateService();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.GetScheduleAsync(cafeId, managerId));
    }

    [Fact]
    public async Task GetScheduleAsync_WithOverrides_MapsDays()
    {
        var cafeId = Guid.NewGuid();
        var overrides = new List<CafeScheduleOverride>
        {
            new()
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                ApplyDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(2)),
                OpenTime = new TimeOnly(10, 0),
                CloseTime = new TimeOnly(22, 0),
                IsClosed = false,
                CreatedAt = DateTime.UtcNow
            }
        };
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _overrideRepo.Setup(r => r.ListByCafeAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(overrides);

        var sut = CreateService();
        var dto = await sut.GetScheduleAsync(cafeId);

        Assert.Single(dto.Days);
        Assert.Equal(new TimeOnly(10, 0), dto.Days[0].OpenTime);
        Assert.Equal(new TimeOnly(22, 0), dto.Days[0].CloseTime);
        Assert.True(dto.Days[0].HasOverride);
    }

    #endregion

    #region GetOverrideAsync

    [Fact]
    public async Task GetOverrideAsync_CafeNotFound_ThrowsNotFound()
    {
        var cafeId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Cafe?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GetOverrideAsync(cafeId, date));
    }

    [Fact]
    public async Task GetOverrideAsync_NoOverride_ReturnsNull()
    {
        var cafeId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCafe(cafeId));
        _overrideRepo.Setup(r => r.GetByApplyDateAsync(cafeId, date, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CafeScheduleOverride?)null);

        var sut = CreateService();
        var result = await sut.GetOverrideAsync(cafeId, date);

        Assert.Null(result);
    }

    #endregion

    #region UpsertOverrideAsync

    [Fact]
    public async Task UpsertOverrideAsync_NotManagerOrStaff_ThrowsForbidden()
    {
        var cafeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateService();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.UpsertOverrideAsync(cafeId, userId, BuildRequest()));
    }

    [Fact]
    public async Task UpsertOverrideAsync_PastDate_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateService();
        var request = BuildRequest(applyDate: DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1)));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            sut.UpsertOverrideAsync(cafeId, userId, request));
    }

    [Fact]
    public async Task UpsertOverrideAsync_OpenEqualsClose_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateService();
        var request = BuildRequest(openTime: new TimeOnly(10, 0), closeTime: new TimeOnly(10, 0));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            sut.UpsertOverrideAsync(cafeId, userId, request));
    }

    [Fact]
    public async Task UpsertOverrideAsync_NewOverride_AddsAndSaves()
    {
        var cafeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var applyDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(3));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _overrideRepo.Setup(r => r.GetByApplyDateAsync(cafeId, applyDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CafeScheduleOverride?)null);

        var sut = CreateService();
        var request = BuildRequest(applyDate: applyDate, openTime: new TimeOnly(9, 0), closeTime: new TimeOnly(21, 0));

        var result = await sut.UpsertOverrideAsync(cafeId, userId, request);

        Assert.Equal(cafeId, result.CafeId);
        Assert.Equal(applyDate, result.ApplyDate);
        Assert.Equal(new TimeOnly(9, 0), result.OpenTime);
        Assert.True(result.HasOverride);

        _overrideRepo.Verify(r => r.AddAsync(It.Is<CafeScheduleOverride>(o =>
            o.CafeId == cafeId && o.ApplyDate == applyDate), It.IsAny<CancellationToken>()), Times.Once);
        _overrideRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertOverrideAsync_ExistingOverride_Updates()
    {
        var cafeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var applyDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(2));
        var existing = new CafeScheduleOverride
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            ApplyDate = applyDate,
            OpenTime = new TimeOnly(8, 0),
            CloseTime = new TimeOnly(20, 0),
            IsClosed = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _overrideRepo.Setup(r => r.GetByApplyDateAsync(cafeId, applyDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = CreateService();
        var request = BuildRequest(applyDate: applyDate, openTime: new TimeOnly(10, 0), closeTime: new TimeOnly(22, 0));

        var result = await sut.UpsertOverrideAsync(cafeId, userId, request);

        Assert.Equal(new TimeOnly(10, 0), result.OpenTime);
        Assert.Equal(new TimeOnly(22, 0), result.CloseTime);
        _overrideRepo.Verify(r => r.UpdateAsync(It.Is<CafeScheduleOverride>(o =>
            o.Id == existing.Id &&
            o.OpenTime == new TimeOnly(10, 0)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertOverrideAsync_IsClosedTrue_BypassesTimeValidation()
    {
        var cafeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var applyDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(2));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _overrideRepo.Setup(r => r.GetByApplyDateAsync(cafeId, applyDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CafeScheduleOverride?)null);

        var sut = CreateService();
        var request = BuildRequest(applyDate: applyDate, isClosed: true);

        var result = await sut.UpsertOverrideAsync(cafeId, userId, request);

        Assert.True(result.IsClosed);
    }

    #endregion

    #region UpsertBulkOverridesAsync

    [Fact]
    public async Task UpsertBulkOverridesAsync_EmptyList_ReturnsEmpty()
    {
        var cafeId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var sut = CreateService();
        var result = await sut.UpsertBulkOverridesAsync(cafeId, userId, new List<UpsertCafeScheduleOverrideRequestDto>());

        Assert.Empty(result);
        _cafeRepo.Verify(r => r.IsManagerOrStaffAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpsertBulkOverridesAsync_InvalidEntry_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateService();
        var requests = new List<UpsertCafeScheduleOverrideRequestDto>
        {
            BuildRequest(applyDate: DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1)))
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            sut.UpsertBulkOverridesAsync(cafeId, userId, requests));
    }

    [Fact]
    public async Task UpsertBulkOverridesAsync_NotAuthorized_ThrowsForbidden()
    {
        var cafeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateService();
        var requests = new List<UpsertCafeScheduleOverrideRequestDto> { BuildRequest() };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.UpsertBulkOverridesAsync(cafeId, userId, requests));
    }

    [Fact]
    public async Task UpsertBulkOverridesAsync_TwoNewEntries_BothPersisted()
    {
        var cafeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _overrideRepo.Setup(r => r.GetByApplyDateAsync(cafeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CafeScheduleOverride?)null);

        var sut = CreateService();
        var requests = new List<UpsertCafeScheduleOverrideRequestDto>
        {
            BuildRequest(applyDate: DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1))),
            BuildRequest(applyDate: DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(2)))
        };

        var result = await sut.UpsertBulkOverridesAsync(cafeId, userId, requests);

        Assert.Equal(2, result.Count);
        _overrideRepo.Verify(r => r.AddAsync(It.IsAny<CafeScheduleOverride>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _overrideRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeleteOverrideAsync

    [Fact]
    public async Task DeleteOverrideAsync_NotManagerOrStaff_ThrowsForbidden()
    {
        var cafeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateService();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.DeleteOverrideAsync(cafeId, userId, DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1))));
    }

    [Fact]
    public async Task DeleteOverrideAsync_NoExisting_IsIdempotentNoOp()
    {
        var cafeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _overrideRepo.Setup(r => r.GetByApplyDateAsync(cafeId, date, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CafeScheduleOverride?)null);

        var sut = CreateService();
        await sut.DeleteOverrideAsync(cafeId, userId, date);

        _overrideRepo.Verify(r => r.DeleteByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteOverrideAsync_Existing_Deletes()
    {
        var cafeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));
        var existing = new CafeScheduleOverride { Id = Guid.NewGuid(), CafeId = cafeId, ApplyDate = date };
        _cafeRepo.Setup(r => r.IsManagerOrStaffAsync(cafeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _overrideRepo.Setup(r => r.GetByApplyDateAsync(cafeId, date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = CreateService();
        await sut.DeleteOverrideAsync(cafeId, userId, date);

        _overrideRepo.Verify(r => r.DeleteByIdAsync(existing.Id, It.IsAny<CancellationToken>()), Times.Once);
        _overrideRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}
