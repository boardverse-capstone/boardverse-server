using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Override lịch mặc định của <c>CafeSchedule</c> cho từng cafe.
/// Cho phép cafe tự bật/tắt <see cref="TimeSlot"/> hoặc đổi giờ bắt đầu/kết thúc
/// mà không phải sửa enum. Resolve qua <c>ResolveScheduleAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// Mapping (BR-NEW-15, cover 24/7):
/// <list type="bullet">
/// <item><description><c>Morning</c>:    mặc định 06:00 – 12:00.</description></item>
/// <item><description><c>Afternoon</c>: mặc định 12:00 – 17:00.</description></item>
/// <item><description><c>Evening</c>:   mặc định 17:00 – 23:00.</description></item>
/// <item><description><c>LateNight</c>: mặc định 23:00 – 06:00 (qua đêm).</description></item>
/// </list>
/// </para>
/// <para>
/// Nếu <see cref="IsClosed"/> = true, cafe không nhận reservation cho slot này.
/// Nếu <see cref="StartTime"/> / <see cref="EndTime"/> null, dùng giá trị mặc định trong <c>CafeSchedule</c>.
/// <see cref="EffectiveFrom"/> / <see cref="EffectiveTo"/> optional — nếu set, override chỉ áp dụng
/// trong khoảng ngày đó (vd: cafe thử nghiệm mở cửa sớm tháng 9).
/// </para>
/// </remarks>
public class CafeScheduleOverride
{
    public Guid Id { get; set; }

    /// <summary>FK Cafe.</summary>
    public Guid CafeId { get; set; }

    /// <summary>Slot bị override.</summary>
    public TimeSlot TimeSlot { get; set; }

    /// <summary>Giờ bắt đầu override. null = dùng default (<c>CafeSchedule.GetStartTime(slot)</c>).</summary>
    public TimeOnly? StartTime { get; set; }

    /// <summary>Giờ kết thúc override. null = dùng default (<c>CafeSchedule.GetEndTime(slot)</c>).</summary>
    public TimeOnly? EndTime { get; set; }

    /// <summary>true = cafe đóng slot này (vd: cafe mở 08:00-23:00, đóng Night).</summary>
    public bool IsClosed { get; set; }

    /// <summary>Optional: override chỉ áp dụng từ ngày này (inclusive). null = không giới hạn.</summary>
    public DateOnly? EffectiveFrom { get; set; }

    /// <summary>Optional: override chỉ áp dụng đến ngày này (inclusive). null = không giới hạn.</summary>
    public DateOnly? EffectiveTo { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Cafe? Cafe { get; set; }
}
