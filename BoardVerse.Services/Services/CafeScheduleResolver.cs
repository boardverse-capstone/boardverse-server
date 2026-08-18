using BoardVerse.Core.IRepositories;

namespace BoardVerse.Core.Constants;

/// <summary>
/// Triển khai IScheduleResolver.
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot - dùng ApplyDate/OpenTime/CloseTime.
/// </summary>
public class CafeScheduleResolver : IScheduleResolver
{
    private readonly ICafeScheduleOverrideRepository _overrideRepository;

    public CafeScheduleResolver(ICafeScheduleOverrideRepository overrideRepository)
    {
        _overrideRepository = overrideRepository;
    }

    public async Task<ResolvedSchedule> ResolveAsync(
        Guid cafeId,
        DateOnly playDate,
        CancellationToken cancellationToken = default)
    {
        var overrideEntry = await _overrideRepository.GetByApplyDateAsync(cafeId, playDate);
        if (overrideEntry != null)
        {
            var openTime = overrideEntry.OpenTime ?? CafeSchedule.DefaultOpenTime;
            var closeTime = overrideEntry.CloseTime ?? CafeSchedule.DefaultCloseTime;

            return new ResolvedSchedule(openTime, closeTime, IsClosed: overrideEntry.IsClosed, HasOverride: true);
        }

        return new ResolvedSchedule(
            CafeSchedule.DefaultOpenTime,
            CafeSchedule.DefaultCloseTime,
            IsClosed: false,
            HasOverride: false);
    }
}
