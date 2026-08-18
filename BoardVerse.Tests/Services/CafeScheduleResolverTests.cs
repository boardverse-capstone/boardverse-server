using BoardVerse.Core.Constants;
using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="CafeScheduleResolver"/>.
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot - dùng ApplyDate/OpenTime/CloseTime.
/// </summary>
public class CafeScheduleResolverTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.Date);

    [Fact]
    public async Task ResolveAsync_NoOverride_ReturnsDefault()
    {
        var sut = new CafeScheduleResolver(new NullOverrideRepository());

        var schedule = await sut.ResolveAsync(Guid.NewGuid(), Today);

        Assert.False(schedule.HasOverride);
        Assert.False(schedule.IsClosed);
        Assert.Equal(CafeSchedule.DefaultOpenTime, schedule.OpenTime);
        Assert.Equal(CafeSchedule.DefaultCloseTime, schedule.CloseTime);
    }

    [Fact]
    public async Task ResolveAsync_ClosedDay_ReturnsIsClosedTrue()
    {
        var ovr = new CafeScheduleOverride
        {
            Id = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            ApplyDate = Today,
            IsClosed = true
        };
        var sut = new CafeScheduleResolver(new FixedOverrideRepository(ovr));

        var schedule = await sut.ResolveAsync(ovr.CafeId, Today);

        Assert.True(schedule.HasOverride);
        Assert.True(schedule.IsClosed);
        Assert.Equal(CafeSchedule.DefaultOpenTime, schedule.OpenTime);
        Assert.Equal(CafeSchedule.DefaultCloseTime, schedule.CloseTime);
    }

    [Fact]
    public async Task ResolveAsync_CustomOpenClose_ReturnsOverriddenTimes()
    {
        var customOpen = new TimeOnly(8, 0);
        var customClose = new TimeOnly(22, 0);
        var ovr = new CafeScheduleOverride
        {
            Id = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            ApplyDate = Today,
            OpenTime = customOpen,
            CloseTime = customClose,
            IsClosed = false
        };
        var sut = new CafeScheduleResolver(new FixedOverrideRepository(ovr));

        var schedule = await sut.ResolveAsync(ovr.CafeId, Today);

        Assert.True(schedule.HasOverride);
        Assert.False(schedule.IsClosed);
        Assert.Equal(customOpen, schedule.OpenTime);
        Assert.Equal(customClose, schedule.CloseTime);
    }

    [Fact]
    public async Task ResolveAsync_DifferentDate_ReturnsDefault()
    {
        var ovr = new CafeScheduleOverride
        {
            Id = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            ApplyDate = Today,
            IsClosed = true
        };
        var sut = new CafeScheduleResolver(new FixedOverrideRepository(ovr));

        var schedule = await sut.ResolveAsync(ovr.CafeId, Today.AddDays(1));

        Assert.False(schedule.HasOverride);
        Assert.False(schedule.IsClosed);
    }

    [Fact]
    public async Task ResolveAsync_DifferentCafe_ReturnsDefault()
    {
        var ovr = new CafeScheduleOverride
        {
            Id = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            ApplyDate = Today,
            IsClosed = true
        };
        var sut = new CafeScheduleResolver(new FixedOverrideRepository(ovr));

        var schedule = await sut.ResolveAsync(Guid.NewGuid(), Today);

        Assert.False(schedule.HasOverride);
    }

    // ===== Test doubles =====

    private sealed class NullOverrideRepository : ICafeScheduleOverrideRepository
    {
        public Task<CafeScheduleOverride?> GetByApplyDateAsync(Guid cafeId, DateOnly applyDate)
            => Task.FromResult<CafeScheduleOverride?>(null);

        public Task<IReadOnlyList<CafeScheduleOverride>> ListByCafeAsync(Guid cafeId)
            => Task.FromResult<IReadOnlyList<CafeScheduleOverride>>(Array.Empty<CafeScheduleOverride>());

        public Task AddAsync(CafeScheduleOverride overrideEntity) => Task.CompletedTask;
        public Task UpdateAsync(CafeScheduleOverride overrideEntity) => Task.CompletedTask;
        public Task DeleteAsync(Guid cafeId, DateOnly applyDate) => Task.CompletedTask;
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

        public Task<CafeScheduleOverride?> GetByApplyDateAsync(Guid cafeId, DateOnly applyDate)
        {
            if (_entry.CafeId != cafeId || _entry.ApplyDate != applyDate)
            {
                return Task.FromResult<CafeScheduleOverride?>(null);
            }

            return Task.FromResult<CafeScheduleOverride?>(_entry);
        }

        public Task<IReadOnlyList<CafeScheduleOverride>> ListByCafeAsync(Guid cafeId)
            => Task.FromResult<IReadOnlyList<CafeScheduleOverride>>(new[] { _entry });

        public Task AddAsync(CafeScheduleOverride overrideEntity) => Task.CompletedTask;
        public Task UpdateAsync(CafeScheduleOverride overrideEntity) => Task.CompletedTask;
        public Task DeleteAsync(Guid cafeId, DateOnly applyDate) => Task.CompletedTask;
        public Task DeleteByIdAsync(Guid overrideId) => Task.CompletedTask;
        public Task SaveChangesAsync() => Task.CompletedTask;
    }
}
