namespace BoardVerse.Core.Constants;

/// <summary>
/// Resolved lịch mở cửa cafe cho 1 ngày.
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot - dùng OpenTime/CloseTime.
/// </summary>
/// <param name="OpenTime">Giờ mở cửa.</param>
/// <param name="CloseTime">Giờ đóng cửa.</param>
/// <param name="IsClosed">true nếu cafe đóng cửa ngày này.</param>
/// <param name="HasOverride">true nếu giá trị lấy từ CafeScheduleOverride.</param>
public readonly record struct ResolvedSchedule(TimeOnly OpenTime, TimeOnly CloseTime, bool IsClosed, bool HasOverride);

/// <summary>
/// Contract resolve lịch cafe từ CafeSchedule (default) + CafeScheduleOverride (optional).
/// </summary>
public interface IScheduleResolver
{
    /// <summary>
    /// Resolve lịch cho (cafe, playDate).
    /// </summary>
    Task<ResolvedSchedule> ResolveAsync(Guid cafeId, DateOnly playDate, CancellationToken cancellationToken = default);
}
