using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;

namespace BoardVerse.Core.Constants;

/// <summary>
/// Triển khai mặc định cho <see cref="IScheduleResolver"/>.
/// Query <c>CafeScheduleOverride</c> qua repository, fallback về <c>CafeSchedule</c> default.
/// </summary>
/// <remarks>
/// Đăng ký DI: <c>services.AddScoped&lt;IScheduleResolver, CafeScheduleResolver&gt;()</c>.
/// </remarks>
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
        TimeSlot slot,
        CancellationToken cancellationToken = default)
    {
        var overrideEntry = await _overrideRepository.GetActiveAsync(cafeId, slot, playDate);
        if (overrideEntry != null)
        {
            var start = overrideEntry.StartTime ?? CafeSchedule.GetStartTime(slot);
            var end = overrideEntry.EndTime ?? CafeSchedule.GetEndTime(slot);

            if (overrideEntry.IsClosed)
            {
                return new ResolvedSchedule(start, end, IsClosed: true, HasOverride: true);
            }

            // Validate override thỏa mãn range hợp lệ.
            if (start == end)
            {
                return new ResolvedSchedule(start, end, IsClosed: true, HasOverride: true);
            }

            return new ResolvedSchedule(start, end, IsClosed: false, HasOverride: true);
        }

        return GetDefault(slot);
    }

    public ResolvedSchedule GetDefault(TimeSlot slot)
    {
        return new ResolvedSchedule(
            CafeSchedule.GetStartTime(slot),
            CafeSchedule.GetEndTime(slot),
            IsClosed: false,
            HasOverride: false);
    }
}
