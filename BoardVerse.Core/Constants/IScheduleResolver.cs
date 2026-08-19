using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Constants;

/// <summary>
/// Resolved lịch mở cửa cafe cho 1 <see cref="TimeSlot"/> cụ thể trên 1 <see cref="DateOnly"/>.
/// Dùng cho logic check-in window, validate quote, scheduler.
/// </summary>
/// <param name="StartTime">Giờ bắt đầu slot (sau khi áp override).</param>
/// <param name="EndTime">Giờ kết thúc slot (sau khi áp override).</param>
/// <param name="IsClosed">true nếu cafe đóng slot này.</param>
/// <param name="HasOverride">true nếu giá trị lấy từ <c>CafeScheduleOverride</c>, false nếu lấy từ <c>CafeSchedule</c> default.</param>
public readonly record struct ResolvedSchedule(TimeOnly StartTime, TimeOnly EndTime, bool IsClosed, bool HasOverride);

/// <summary>
/// Contract resolve lịch cafe từ <c>CafeSchedule</c> (default) + <c>CafeScheduleOverride</c> (optional).
/// Triển khai mặc định: <c>CafeScheduleResolver</c>.
/// </summary>
public interface IScheduleResolver
{
    /// <summary>
    /// Resolve lịch cho (cafe, slot, playDate).
    /// Nếu cafe có <c>CafeScheduleOverride</c> còn hiệu lực cho slot này, dùng override.
    /// Ngược lại fallback về <c>CafeSchedule.GetStartTime/GetEndTime</c>.
    /// </summary>
    /// <returns>
    /// <see cref="ResolvedSchedule"/> với StartTime, EndTime, IsClosed.
    /// Caller phải check <c>IsClosed</c> trước khi dùng.
    /// </returns>
    Task<ResolvedSchedule> ResolveAsync(Guid cafeId, DateOnly playDate, TimeSlot slot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronous fallback khi caller không có <c>cafeId</c> (vd: scheduler tổng quát).
    /// Trả về giá trị default từ <c>CafeSchedule</c>.
    /// </summary>
    ResolvedSchedule GetDefault(TimeSlot slot);
}
