using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.TimeSlotOverride;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="TimeSlotService"/> â€” quáº£n lÃ½ TimeSlot máº·c Ä‘á»‹nh + override theo cafe.
/// </summary>
public class TimeSlotServiceTests
{
    private readonly Mock<ICafeScheduleOverrideRepository> _overrideRepo = new();
    private readonly Mock<ICafeRepository> _cafeRepo = new();
    private readonly Mock<ILogger<TimeSlotService>> _logger = new();

    private TimeSlotService CreateService() => new(
        _overrideRepo.Object,
        _cafeRepo.Object,
        _logger.Object);

    private static Cafe BuildCafe(Guid cafeId, Guid managerId, string name = "Cafe Test")
    {
        return new Cafe
        {
            Id = cafeId,
            ManagerId = managerId,
            Name = name,
            Address = "123 Test Street",
            TotalSeats = 30
        };
    }

    private static CafeScheduleOverride BuildOverride(
        Guid cafeId,
        TimeSlot slot,
        TimeOnly? startTime = null,
        TimeOnly? endTime = null,
        bool isClosed = false,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null)
    {
        var now = DateTime.UtcNow;
        return new CafeScheduleOverride
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            TimeSlot = slot,
            StartTime = startTime,
            EndTime = endTime,
            IsClosed = isClosed,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    #region GetDefaultTimeSlotsAsync

    [Fact]
    public async Task GetDefaultTimeSlotsAsync_ReturnsAll4Slots()
    {
        var svc = CreateService();

        var result = await svc.GetDefaultTimeSlotsAsync();

        Assert.Equal(4, result.Count);
        Assert.Equal(nameof(TimeSlot.Morning), result[0].Slot);
        Assert.Equal(nameof(TimeSlot.Afternoon), result[1].Slot);
        Assert.Equal(nameof(TimeSlot.Evening), result[2].Slot);
        Assert.Equal(nameof(TimeSlot.LateNight), result[3].Slot);
    }

    [Fact]
    public async Task GetDefaultTimeSlotsAsync_HasCorrectTimes()
    {
        var svc = CreateService();

        var result = await svc.GetDefaultTimeSlotsAsync();

        var morning = result.Single(s => s.Slot == nameof(TimeSlot.Morning));
        Assert.Equal(new TimeOnly(6, 0), morning.DefaultStartTime);
        Assert.Equal(new TimeOnly(12, 0), morning.DefaultEndTime);
        Assert.Equal(360, morning.DurationMinutes);

        var lateNight = result.Single(s => s.Slot == nameof(TimeSlot.LateNight));
        Assert.Equal(new TimeOnly(23, 0), lateNight.DefaultStartTime);
        Assert.Equal(new TimeOnly(6, 0), lateNight.DefaultEndTime);
        Assert.Equal(420, lateNight.DurationMinutes); // overnight
    }

    [Fact]
    public async Task GetDefaultTimeSlotsAsync_HasDisplayNames()
    {
        var svc = CreateService();

        var result = await svc.GetDefaultTimeSlotsAsync();

        Assert.Contains("SÃ¡ng", result.Single(s => s.Slot == nameof(TimeSlot.Morning)).DisplayName);
        Assert.Contains("Chiá»u", result.Single(s => s.Slot == nameof(TimeSlot.Afternoon)).DisplayName);
        Assert.Contains("Tá»‘i", result.Single(s => s.Slot == nameof(TimeSlot.Evening)).DisplayName);
        Assert.Contains("Khuya", result.Single(s => s.Slot == nameof(TimeSlot.LateNight)).DisplayName);
    }

    #endregion

    #region GetCafeTimeSlotsAsync â€” ownership

    [Fact]
    public async Task GetCafeTimeSlotsAsync_CafeNotFound_ThrowsNotFound()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync((Cafe?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.GetCafeTimeSlotsAsync(cafeId, managerId));
    }

    [Fact]
    public async Task GetCafeTimeSlotsAsync_NotManagerOfCafe_ThrowsForbidden()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var otherManagerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, otherManagerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(Array.Empty<Cafe>());

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.GetCafeTimeSlotsAsync(cafeId, managerId));
    }

    [Fact]
    public async Task GetCafeTimeSlotsAsync_ManagerOwnsCafe_ReturnsAll4Slots()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.ListByCafeAsync(cafeId))
            .ReturnsAsync(Array.Empty<CafeScheduleOverride>());

        var svc = CreateService();

        var result = await svc.GetCafeTimeSlotsAsync(cafeId, managerId);

        Assert.Equal(4, result.Count);
        Assert.All(result, dto => Assert.False(dto.HasOverride));
        Assert.All(result, dto => Assert.Equal(cafeId, dto.CafeId));
    }

    [Fact]
    public async Task GetCafeTimeSlotsAsync_WithOverrides_MergesCorrectly()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });

        var overrides = new[]
        {
            BuildOverride(cafeId, TimeSlot.Morning, new TimeOnly(7, 0), new TimeOnly(13, 0)),
            BuildOverride(cafeId, TimeSlot.LateNight, isClosed: true)
        };
        _overrideRepo.Setup(r => r.ListByCafeAsync(cafeId)).ReturnsAsync(overrides);

        var svc = CreateService();

        var result = await svc.GetCafeTimeSlotsAsync(cafeId, managerId);

        var morning = result.Single(s => s.TimeSlot == nameof(TimeSlot.Morning));
        Assert.True(morning.HasOverride);
        Assert.Equal(new TimeOnly(7, 0), morning.StartTime);
        Assert.Equal(new TimeOnly(13, 0), morning.EndTime);
        Assert.True(morning.IsCustomized);

        var lateNight = result.Single(s => s.TimeSlot == nameof(TimeSlot.LateNight));
        Assert.True(lateNight.HasOverride);
        Assert.True(lateNight.IsClosed);
        Assert.True(lateNight.IsCustomized);

        var afternoon = result.Single(s => s.TimeSlot == nameof(TimeSlot.Afternoon));
        Assert.False(afternoon.HasOverride);
        Assert.False(afternoon.IsCustomized);
        Assert.Equal(new TimeOnly(12, 0), afternoon.StartTime);
    }

    #endregion

    #region GetCafeTimeSlotAsync

    [Fact]
    public async Task GetCafeTimeSlotAsync_InvalidSlotName_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });

        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.GetCafeTimeSlotAsync(cafeId, managerId, "InvalidSlot"));
    }

    [Fact]
    public async Task GetCafeTimeSlotAsync_EmptySlotName_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });

        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.GetCafeTimeSlotAsync(cafeId, managerId, ""));
    }

    [Fact]
    public async Task GetCafeTimeSlotAsync_NoOverride_ReturnsDefault()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Morning))
            .ReturnsAsync((CafeScheduleOverride?)null);

        var svc = CreateService();

        var result = await svc.GetCafeTimeSlotAsync(cafeId, managerId, "Morning");

        Assert.Equal(TimeSlot.Morning.ToString(), result.TimeSlot);
        Assert.False(result.HasOverride);
        Assert.Equal(new TimeOnly(6, 0), result.StartTime);
        Assert.Equal(new TimeOnly(12, 0), result.EndTime);
    }

    [Fact]
    public async Task GetCafeTimeSlotAsync_WithOverride_ReturnsMerged()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var overrideEntry = BuildOverride(cafeId, TimeSlot.Evening, new TimeOnly(18, 0), new TimeOnly(23, 30));
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Evening))
            .ReturnsAsync(overrideEntry);

        var svc = CreateService();

        var result = await svc.GetCafeTimeSlotAsync(cafeId, managerId, "Evening");

        Assert.True(result.HasOverride);
        Assert.Equal(new TimeOnly(18, 0), result.StartTime);
        Assert.Equal(new TimeOnly(23, 30), result.EndTime);
        Assert.Equal(new TimeOnly(17, 0), result.DefaultStartTime);
        Assert.Equal(new TimeOnly(23, 0), result.DefaultEndTime);
    }

    #endregion

    #region CreateOverrideAsync

    [Fact]
    public async Task CreateOverrideAsync_CafeNotFound_ThrowsNotFound()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync((Cafe?)null);

        var svc = CreateService();

        var request = new CreateTimeSlotOverrideRequestDto
        {
            TimeSlot = "Morning",
            StartTime = new TimeOnly(7, 0),
            EndTime = new TimeOnly(13, 0)
        };

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.CreateOverrideAsync(cafeId, managerId, request));
    }

    [Fact]
    public async Task CreateOverrideAsync_NotManager_ThrowsForbidden()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var otherManagerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, otherManagerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(Array.Empty<Cafe>());

        var svc = CreateService();

        var request = new CreateTimeSlotOverrideRequestDto
        {
            TimeSlot = "Morning",
            StartTime = new TimeOnly(7, 0),
            EndTime = new TimeOnly(13, 0)
        };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.CreateOverrideAsync(cafeId, managerId, request));
    }

    [Fact]
    public async Task CreateOverrideAsync_InvalidSlotName_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });

        var svc = CreateService();

        var request = new CreateTimeSlotOverrideRequestDto
        {
            TimeSlot = "NotASlot",
            StartTime = new TimeOnly(7, 0),
            EndTime = new TimeOnly(13, 0)
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.CreateOverrideAsync(cafeId, managerId, request));
    }

    [Fact]
    public async Task CreateOverrideAsync_StartEqualsEnd_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });

        var svc = CreateService();

        var request = new CreateTimeSlotOverrideRequestDto
        {
            TimeSlot = "Morning",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(8, 0),
            IsClosed = false
        };

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.CreateOverrideAsync(cafeId, managerId, request));

        Assert.Contains("Giá» báº¯t Ä‘áº§u", ex.Message);
    }

    [Fact]
    public async Task CreateOverrideAsync_EffectiveFromAfterEffectiveTo_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });

        var svc = CreateService();

        var request = new CreateTimeSlotOverrideRequestDto
        {
            TimeSlot = "Morning",
            StartTime = new TimeOnly(7, 0),
            EndTime = new TimeOnly(13, 0),
            EffectiveFrom = new DateOnly(2026, 8, 20),
            EffectiveTo = new DateOnly(2026, 8, 10)
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.CreateOverrideAsync(cafeId, managerId, request));
    }

    [Fact]
    public async Task CreateOverrideAsync_AlreadyExists_ThrowsConflict()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Morning))
            .ReturnsAsync(BuildOverride(cafeId, TimeSlot.Morning));

        var svc = CreateService();

        var request = new CreateTimeSlotOverrideRequestDto
        {
            TimeSlot = "Morning",
            StartTime = new TimeOnly(7, 0),
            EndTime = new TimeOnly(13, 0)
        };

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateOverrideAsync(cafeId, managerId, request));

        Assert.Contains("Ä‘Ã£ cÃ³ override", ex.Message);
    }

    [Fact]
    public async Task CreateOverrideAsync_IsClosedSkipsStartEndValidation()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Morning))
            .ReturnsAsync((CafeScheduleOverride?)null);

        CafeScheduleOverride? captured = null;
        _overrideRepo.Setup(r => r.AddAsync(It.IsAny<CafeScheduleOverride>()))
            .Callback<CafeScheduleOverride>(o => captured = o)
            .Returns(Task.CompletedTask);

        var svc = CreateService();

        var request = new CreateTimeSlotOverrideRequestDto
        {
            TimeSlot = "Morning",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(8, 0), // equal â€” but IsClosed=true so OK
            IsClosed = true
        };

        var result = await svc.CreateOverrideAsync(cafeId, managerId, request);

        Assert.NotNull(captured);
        Assert.True(captured!.IsClosed);
        Assert.True(result.IsClosed);
        Assert.True(result.HasOverride);
        _overrideRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateOverrideAsync_Valid_CreatesAndPersists()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Morning))
            .ReturnsAsync((CafeScheduleOverride?)null);

        CafeScheduleOverride? captured = null;
        _overrideRepo.Setup(r => r.AddAsync(It.IsAny<CafeScheduleOverride>()))
            .Callback<CafeScheduleOverride>(o => captured = o)
            .Returns(Task.CompletedTask);

        var svc = CreateService();

        var request = new CreateTimeSlotOverrideRequestDto
        {
            TimeSlot = "Morning",
            StartTime = new TimeOnly(7, 0),
            EndTime = new TimeOnly(13, 0),
            IsClosed = false,
            EffectiveFrom = new DateOnly(2026, 8, 15),
            EffectiveTo = new DateOnly(2026, 9, 15)
        };

        var result = await svc.CreateOverrideAsync(cafeId, managerId, request);

        Assert.NotNull(captured);
        Assert.Equal(cafeId, captured!.CafeId);
        Assert.Equal(TimeSlot.Morning, captured.TimeSlot);
        Assert.Equal(new TimeOnly(7, 0), captured.StartTime);
        Assert.Equal(new TimeOnly(13, 0), captured.EndTime);
        Assert.False(captured.IsClosed);
        Assert.Equal(new DateOnly(2026, 8, 15), captured.EffectiveFrom);
        Assert.Equal(new DateOnly(2026, 9, 15), captured.EffectiveTo);
        Assert.NotEqual(Guid.Empty, captured.Id);

        Assert.Equal(new TimeOnly(7, 0), result.StartTime);
        Assert.Equal(new TimeOnly(13, 0), result.EndTime);
        Assert.True(result.HasOverride);
        Assert.True(result.IsCustomized);

        _overrideRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateOverrideAsync_NullsEffectiveDates_Allowed()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Afternoon))
            .ReturnsAsync((CafeScheduleOverride?)null);

        var svc = CreateService();

        var request = new CreateTimeSlotOverrideRequestDto
        {
            TimeSlot = "Afternoon",
            StartTime = new TimeOnly(13, 0),
            EndTime = new TimeOnly(18, 0),
            IsClosed = false,
            EffectiveFrom = null,
            EffectiveTo = null
        };

        var result = await svc.CreateOverrideAsync(cafeId, managerId, request);

        Assert.Null(result.EffectiveFrom);
        Assert.Null(result.EffectiveTo);
    }

    #endregion

    #region UpdateOverrideAsync

    [Fact]
    public async Task UpdateOverrideAsync_NotFound_ThrowsNotFound()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Morning))
            .ReturnsAsync((CafeScheduleOverride?)null);

        var svc = CreateService();

        var request = new UpdateTimeSlotOverrideRequestDto
        {
            StartTime = new TimeOnly(7, 0)
        };

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.UpdateOverrideAsync(cafeId, managerId, "Morning", request));
    }

    [Fact]
    public async Task UpdateOverrideAsync_AllFieldsNull_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var existing = BuildOverride(cafeId, TimeSlot.Morning, new TimeOnly(7, 0), new TimeOnly(13, 0));
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Morning))
            .ReturnsAsync(existing);

        var svc = CreateService();

        var request = new UpdateTimeSlotOverrideRequestDto();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.UpdateOverrideAsync(cafeId, managerId, "Morning", request));

        Assert.Equal(ApiErrorMessages.System.TimeSlotOverrideNoFieldsToUpdate, ex.Message);
    }

    [Fact]
    public async Task UpdateOverrideAsync_PartialUpdate_MergesExistingValues()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var existing = BuildOverride(cafeId, TimeSlot.Morning,
            new TimeOnly(7, 0), new TimeOnly(13, 0), isClosed: false);
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Morning))
            .ReturnsAsync(existing);

        var svc = CreateService();

        // Only update EndTime â€” StartTime should remain (7, 0)
        var request = new UpdateTimeSlotOverrideRequestDto
        {
            EndTime = new TimeOnly(14, 0)
        };

        var result = await svc.UpdateOverrideAsync(cafeId, managerId, "Morning", request);

        Assert.Equal(new TimeOnly(7, 0), existing.StartTime); // unchanged
        Assert.Equal(new TimeOnly(14, 0), existing.EndTime); // updated
        Assert.Equal(new TimeOnly(14, 0), result.EndTime);
        _overrideRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateOverrideAsync_InvalidRangeAfterMerge_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var existing = BuildOverride(cafeId, TimeSlot.Morning, new TimeOnly(7, 0), new TimeOnly(13, 0));
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Morning))
            .ReturnsAsync(existing);

        var svc = CreateService();

        var request = new UpdateTimeSlotOverrideRequestDto
        {
            StartTime = new TimeOnly(13, 0), // matches existing EndTime â†’ invalid
            EndTime = new TimeOnly(13, 0)
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.UpdateOverrideAsync(cafeId, managerId, "Morning", request));
    }

    [Fact]
    public async Task UpdateOverrideAsync_InvalidEffectiveRange_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var existing = BuildOverride(cafeId, TimeSlot.Morning, new TimeOnly(7, 0), new TimeOnly(13, 0));
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Morning))
            .ReturnsAsync(existing);

        var svc = CreateService();

        var request = new UpdateTimeSlotOverrideRequestDto
        {
            EffectiveFrom = new DateOnly(2026, 9, 1),
            EffectiveTo = new DateOnly(2026, 8, 1)
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.UpdateOverrideAsync(cafeId, managerId, "Morning", request));
    }

    [Fact]
    public async Task UpdateOverrideAsync_InvalidSlotName_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });

        var svc = CreateService();

        var request = new UpdateTimeSlotOverrideRequestDto
        {
            StartTime = new TimeOnly(7, 0)
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.UpdateOverrideAsync(cafeId, managerId, "NotASlot", request));
    }

    [Fact]
    public async Task UpdateOverrideAsync_ToggleIsClosedToTrue_AllowsEqualStartEnd()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var existing = BuildOverride(cafeId, TimeSlot.Morning, new TimeOnly(7, 0), new TimeOnly(13, 0));
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Morning))
            .ReturnsAsync(existing);

        var svc = CreateService();

        var request = new UpdateTimeSlotOverrideRequestDto
        {
            IsClosed = true
        };

        var result = await svc.UpdateOverrideAsync(cafeId, managerId, "Morning", request);

        Assert.True(existing.IsClosed);
        Assert.True(result.IsClosed);
    }

    [Fact]
    public async Task UpdateOverrideAsync_ToggleIsClosedToFalse_ReopensSlot()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        // Override Ä‘Ã£ tá»“n táº¡i vá»›i IsClosed=true (Ä‘Ã£ Ä‘Ã³ng tá»« trÆ°á»›c).
        var existing = BuildOverride(cafeId, TimeSlot.Morning, new TimeOnly(7, 0), new TimeOnly(13, 0), isClosed: true);
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Morning))
            .ReturnsAsync(existing);

        var svc = CreateService();

        var request = new UpdateTimeSlotOverrideRequestDto
        {
            IsClosed = false  // Manager muá»‘n má»Ÿ láº¡i slot
        };

        var result = await svc.UpdateOverrideAsync(cafeId, managerId, "Morning", request);

        Assert.False(existing.IsClosed);
        Assert.False(result.IsClosed);
    }

    [Fact]
    public async Task UpdateOverrideAsync_NoFieldsToUpdate_DoesNotCallRepository()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var existing = BuildOverride(cafeId, TimeSlot.Morning, new TimeOnly(7, 0), new TimeOnly(13, 0));
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Morning))
            .ReturnsAsync(existing);

        var svc = CreateService();

        var request = new UpdateTimeSlotOverrideRequestDto(); // all null

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.UpdateOverrideAsync(cafeId, managerId, "Morning", request));

        // Verify Update was NOT called (snapshot happened before validation, but SaveChangesAsync shouldn't run).
        _overrideRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
        _overrideRepo.Verify(r => r.UpdateAsync(It.IsAny<CafeScheduleOverride>()), Times.Never);
    }

    #endregion

    #region DeleteOverrideAsync

    [Fact]
    public async Task DeleteOverrideAsync_CafeNotFound_ThrowsNotFound()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync((Cafe?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.DeleteOverrideAsync(cafeId, managerId, "Morning"));
    }

    [Fact]
    public async Task DeleteOverrideAsync_NotManager_ThrowsForbidden()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var otherManagerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, otherManagerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(Array.Empty<Cafe>());

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.DeleteOverrideAsync(cafeId, managerId, "Morning"));
    }

    [Fact]
    public async Task DeleteOverrideAsync_InvalidSlotName_ThrowsBadRequest()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });

        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.DeleteOverrideAsync(cafeId, managerId, "Invalid"));
    }

    [Fact]
    public async Task DeleteOverrideAsync_OverrideExists_CallsDeleteAndSaves()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });

        var svc = CreateService();

        await svc.DeleteOverrideAsync(cafeId, managerId, "Morning");

        _overrideRepo.Verify(r => r.DeleteAsync(cafeId, TimeSlot.Morning), Times.Once);
        _overrideRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteOverrideAsync_OverrideNotExists_Idempotent()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });

        var svc = CreateService();

        // Idempotent: gá»i delete dÃ¹ chÆ°a cÃ³ override váº«n thÃ nh cÃ´ng.
        await svc.DeleteOverrideAsync(cafeId, managerId, "Evening");

        _overrideRepo.Verify(r => r.DeleteAsync(cafeId, TimeSlot.Evening), Times.Once);
        _overrideRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region Case-insensitive parsing

    [Theory]
    [InlineData("morning", TimeSlot.Morning)]
    [InlineData("Morning", TimeSlot.Morning)]
    [InlineData("MORNING", TimeSlot.Morning)]
    [InlineData("mOrNiNg", TimeSlot.Morning)]
    [InlineData("afternoon", TimeSlot.Afternoon)]
    [InlineData("evening", TimeSlot.Evening)]
    [InlineData("latenight", TimeSlot.LateNight)]
    [InlineData("LateNight", TimeSlot.LateNight)]
    public async Task GetCafeTimeSlotAsync_AcceptsCaseInsensitiveSlotName(string input, TimeSlot expected)
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, expected))
            .ReturnsAsync((CafeScheduleOverride?)null);

        var svc = CreateService();

        var result = await svc.GetCafeTimeSlotAsync(cafeId, managerId, input);

        Assert.Equal(expected.ToString(), result.TimeSlot);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    public async Task GetCafeTimeSlotAsync_AcceptsNumericSlotName(string input)
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, It.IsAny<TimeSlot>()))
            .ReturnsAsync((CafeScheduleOverride?)null);

        var svc = CreateService();

        var result = await svc.GetCafeTimeSlotAsync(cafeId, managerId, input);

        Assert.NotNull(result);
        Assert.Equal(cafeId, result.CafeId);
    }

    #endregion

    #region IsCustomized flag

    [Fact]
    public async Task CreateOverrideAsync_IsCustomized_TrueWhenTimesChange()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Morning))
            .ReturnsAsync((CafeScheduleOverride?)null);

        var svc = CreateService();

        var request = new CreateTimeSlotOverrideRequestDto
        {
            TimeSlot = "Morning",
            StartTime = new TimeOnly(7, 0), // khÃ¡c default 06:00
            EndTime = new TimeOnly(13, 0)   // khÃ¡c default 12:00
        };

        var result = await svc.CreateOverrideAsync(cafeId, managerId, request);

        Assert.True(result.IsCustomized);
    }

    [Fact]
    public async Task CreateOverrideAsync_IsCustomized_FalseWhenTimesEqualDefault()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Morning))
            .ReturnsAsync((CafeScheduleOverride?)null);

        var svc = CreateService();

        var request = new CreateTimeSlotOverrideRequestDto
        {
            TimeSlot = "Morning",
            StartTime = new TimeOnly(6, 0), // = default
            EndTime = new TimeOnly(12, 0),  // = default
            IsClosed = false
        };

        var result = await svc.CreateOverrideAsync(cafeId, managerId, request);

        Assert.False(result.IsCustomized);
    }

    #endregion

    #region Building response reflects schedule

    [Fact]
    public async Task GetDefaultTimeSlotsAsync_MatchesCafeScheduleConstants()
    {
        var svc = CreateService();

        var result = await svc.GetDefaultTimeSlotsAsync();

        foreach (var dto in result)
        {
            var slot = Enum.Parse<TimeSlot>(dto.Slot);
            Assert.Equal(CafeSchedule.GetStartTime(slot), dto.DefaultStartTime);
            Assert.Equal(CafeSchedule.GetEndTime(slot), dto.DefaultEndTime);
            Assert.Equal(CafeSchedule.GetDurationMinutes(slot), dto.DurationMinutes);
        }
    }

    [Fact]
    public async Task GetCafeTimeSlotAsync_NoOverride_TimestampsAreNull()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.GetByCafeAndSlotAsync(cafeId, TimeSlot.Morning))
            .ReturnsAsync((CafeScheduleOverride?)null);

        var svc = CreateService();

        var result = await svc.GetCafeTimeSlotAsync(cafeId, managerId, "Morning");

        Assert.False(result.HasOverride);
        Assert.Null(result.CreatedAt);
        Assert.Null(result.UpdatedAt);
    }

    [Fact]
    public async Task GetCafeTimeSlotsAsync_NoOverrides_AllTimestampsAreNull()
    {
        var cafeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _cafeRepo.Setup(r => r.GetByIdAsync(cafeId)).ReturnsAsync(BuildCafe(cafeId, managerId));
        _cafeRepo.Setup(r => r.GetCafesByManagerIdAsync(managerId))
            .ReturnsAsync(new[] { BuildCafe(cafeId, managerId) });
        _overrideRepo.Setup(r => r.ListByCafeAsync(cafeId))
            .ReturnsAsync(Array.Empty<CafeScheduleOverride>());

        var svc = CreateService();

        var result = await svc.GetCafeTimeSlotsAsync(cafeId, managerId);

        Assert.Equal(4, result.Count);
        Assert.All(result, dto =>
        {
            Assert.False(dto.HasOverride);
            Assert.Null(dto.CreatedAt);
            Assert.Null(dto.UpdatedAt);
        });
    }

    #endregion
}

