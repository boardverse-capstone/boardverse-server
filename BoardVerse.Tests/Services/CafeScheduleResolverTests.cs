using BoardVerse.Core.Constants;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="CafeScheduleResolver"/>:
/// - Default time slot (BR-NEW-15, cập nhật cover 24h).
/// - Override đóng slot.
/// - Override startTime/endTime khác default.
/// - EffectiveFrom/EffectiveTo filter theo playDate.
/// </summary>
public class CafeScheduleResolverTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.Date);

    [Fact]
    public void GetDefault_Morning_ReturnsCorrectRange()
    {
        var sut = new CafeScheduleResolver(new NullOverrideRepository());

        var schedule = sut.GetDefault(TimeSlot.Morning);

        Assert.Equal(new TimeOnly(6, 0), schedule.StartTime);
        Assert.Equal(new TimeOnly(12, 0), schedule.EndTime);
        Assert.False(schedule.IsClosed);
        Assert.False(schedule.HasOverride);
    }

    [Fact]
    public void GetDefault_LateNight_Returns23To6Overnight()
    {
        var sut = new CafeScheduleResolver(new NullOverrideRepository());

        var schedule = sut.GetDefault(TimeSlot.LateNight);

        Assert.Equal(new TimeOnly(23, 0), schedule.StartTime);
        Assert.Equal(new TimeOnly(6, 0), schedule.EndTime);
        Assert.False(schedule.IsClosed);
    }

    [Fact]
    public void GetDefault_Evening_Returns17To23()
    {
        var sut = new CafeScheduleResolver(new NullOverrideRepository());

        var schedule = sut.GetDefault(TimeSlot.Evening);

        Assert.Equal(new TimeOnly(17, 0), schedule.StartTime);
        Assert.Equal(new TimeOnly(23, 0), schedule.EndTime);
    }

    [Fact]
    public void GetDefault_Afternoon_Returns12To17()
    {
        var sut = new CafeScheduleResolver(new NullOverrideRepository());

        var schedule = sut.GetDefault(TimeSlot.Afternoon);

        Assert.Equal(new TimeOnly(12, 0), schedule.StartTime);
        Assert.Equal(new TimeOnly(17, 0), schedule.EndTime);
    }

    [Fact]
    public async Task ResolveAsync_NoOverride_ReturnsDefault()
    {
        var sut = new CafeScheduleResolver(new NullOverrideRepository());

        var schedule = await sut.ResolveAsync(Guid.NewGuid(), Today, TimeSlot.Morning);

        Assert.False(schedule.HasOverride);
        Assert.False(schedule.IsClosed);
        Assert.Equal(new TimeOnly(6, 0), schedule.StartTime);
    }

    [Fact]
    public async Task ResolveAsync_ClosedSlot_ReturnsIsClosedTrue()
    {
        var ovr = new CafeScheduleOverride
        {
            Id = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            TimeSlot = TimeSlot.LateNight,
            IsClosed = true
        };
        var sut = new CafeScheduleResolver(new FixedOverrideRepository(ovr));

        var schedule = await sut.ResolveAsync(ovr.CafeId, Today, TimeSlot.LateNight);

        Assert.True(schedule.HasOverride);
        Assert.True(schedule.IsClosed);
    }

    [Fact]
    public async Task ResolveAsync_CustomStartEnd_ReturnsOverriddenRange()
    {
        var ovr = new CafeScheduleOverride
        {
            Id = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            TimeSlot = TimeSlot.Morning,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(12, 0),
            IsClosed = false
        };
        var sut = new CafeScheduleResolver(new FixedOverrideRepository(ovr));

        var schedule = await sut.ResolveAsync(ovr.CafeId, Today, TimeSlot.Morning);

        Assert.True(schedule.HasOverride);
        Assert.False(schedule.IsClosed);
        Assert.Equal(new TimeOnly(6, 0), schedule.StartTime);
        Assert.Equal(new TimeOnly(12, 0), schedule.EndTime);
    }

    [Fact]
    public async Task ResolveAsync_EffectiveFrom_FiltersOutsideRange()
    {
        var ovr = new CafeScheduleOverride
        {
            Id = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            TimeSlot = TimeSlot.Morning,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(12, 0),
            EffectiveFrom = Today.AddDays(7)
        };
        var sut = new CafeScheduleResolver(new FixedOverrideRepository(ovr));

        // playDate before EffectiveFrom → revert to default
        var schedule = await sut.ResolveAsync(ovr.CafeId, Today, TimeSlot.Morning);

        Assert.False(schedule.HasOverride);
        Assert.Equal(new TimeOnly(6, 0), schedule.StartTime);
    }

    [Fact]
    public async Task ResolveAsync_EffectiveTo_FiltersOutsideRange()
    {
        var ovr = new CafeScheduleOverride
        {
            Id = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            TimeSlot = TimeSlot.Morning,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(12, 0),
            EffectiveTo = Today.AddDays(-1)
        };
        var sut = new CafeScheduleResolver(new FixedOverrideRepository(ovr));

        // playDate after EffectiveTo → revert to default
        var schedule = await sut.ResolveAsync(ovr.CafeId, Today, TimeSlot.Morning);

        Assert.False(schedule.HasOverride);
    }

    [Fact]
    public async Task ResolveAsync_EffectiveRangeMatches_AppliesOverride()
    {
        var ovr = new CafeScheduleOverride
        {
            Id = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            TimeSlot = TimeSlot.Morning,
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(12, 0),
            EffectiveFrom = Today.AddDays(-3),
            EffectiveTo = Today.AddDays(3)
        };
        var sut = new CafeScheduleResolver(new FixedOverrideRepository(ovr));

        var schedule = await sut.ResolveAsync(ovr.CafeId, Today, TimeSlot.Morning);

        Assert.True(schedule.HasOverride);
        Assert.Equal(new TimeOnly(6, 0), schedule.StartTime);
    }

    // ===== Test doubles =====

    private sealed class NullOverrideRepository : ICafeScheduleOverrideRepository
    {
        public Task<CafeScheduleOverride?> GetActiveAsync(Guid cafeId, TimeSlot slot, DateOnly playDate)
            => Task.FromResult<CafeScheduleOverride?>(null);

        public Task<IReadOnlyList<CafeScheduleOverride>> ListByCafeAsync(Guid cafeId)
            => Task.FromResult<IReadOnlyList<CafeScheduleOverride>>(Array.Empty<CafeScheduleOverride>());

        public Task<CafeScheduleOverride?> GetByCafeAndSlotAsync(Guid cafeId, TimeSlot slot)
            => Task.FromResult<CafeScheduleOverride?>(null);

        public Task AddAsync(CafeScheduleOverride overrideEntity) => Task.CompletedTask;
        public Task UpdateAsync(CafeScheduleOverride overrideEntity) => Task.CompletedTask;
        public Task DeleteAsync(Guid cafeId, TimeSlot slot) => Task.CompletedTask;
        public Task DeleteByIdAsync(Guid overrideId) => Task.CompletedTask;
        public Task SaveChangesAsync() => Task.CompletedTask;
    }

    private sealed class FixedOverrideRepository : ICafeScheduleOverrideRepository
    {
        private readonly CafeScheduleOverride _entry;

        public FixedOverrideRepository(CafeScheduleOverride entry)
        {
            _entry = entry;
        }

        public Task<CafeScheduleOverride?> GetActiveAsync(Guid cafeId, TimeSlot slot, DateOnly playDate)
        {
            if (_entry.CafeId != cafeId || _entry.TimeSlot != slot)
            {
                return Task.FromResult<CafeScheduleOverride?>(null);
            }

            if (_entry.EffectiveFrom.HasValue && playDate < _entry.EffectiveFrom.Value)
            {
                return Task.FromResult<CafeScheduleOverride?>(null);
            }

            if (_entry.EffectiveTo.HasValue && playDate > _entry.EffectiveTo.Value)
            {
                return Task.FromResult<CafeScheduleOverride?>(null);
            }

            return Task.FromResult<CafeScheduleOverride?>(_entry);
        }

        public Task<IReadOnlyList<CafeScheduleOverride>> ListByCafeAsync(Guid cafeId)
            => Task.FromResult<IReadOnlyList<CafeScheduleOverride>>(new[] { _entry });

        public Task<CafeScheduleOverride?> GetByCafeAndSlotAsync(Guid cafeId, TimeSlot slot)
        {
            if (_entry.CafeId != cafeId || _entry.TimeSlot != slot)
            {
                return Task.FromResult<CafeScheduleOverride?>(null);
            }
            return Task.FromResult<CafeScheduleOverride?>(_entry);
        }

        public Task AddAsync(CafeScheduleOverride overrideEntity) => Task.CompletedTask;
        public Task UpdateAsync(CafeScheduleOverride overrideEntity) => Task.CompletedTask;
        public Task DeleteAsync(Guid cafeId, TimeSlot slot) => Task.CompletedTask;
        public Task DeleteByIdAsync(Guid overrideId) => Task.CompletedTask;
        public Task SaveChangesAsync() => Task.CompletedTask;
    }
}
