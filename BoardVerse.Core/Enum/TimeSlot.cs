namespace BoardVerse.Core.Enum;

/// <summary>
/// Khung giờ cố định cho lobby (BR-NEW-15 §7.1, đồng bộ docs/time-slot-fixed-end-design (1).md §13).
/// Không cho phép cafe config thêm khung mới — chỉ override tên hiển thị + giờ qua <c>CafeScheduleOverride</c>.
/// </summary>
/// <remarks>
/// Mapping (boardverse.mdc + docs/time-slot-fixed-end-design (1).md):
/// <list type="bullet">
/// <item><description><c>Morning</c> (06:00 – 12:00): Phiên sáng. 6 tiếng.</description></item>
/// <item><description><c>Afternoon</c> (12:00 – 17:00): Phiên chiều. 5 tiếng.</description></item>
/// <item><description><c>Evening</c> (17:00 – 23:00): Phiên tối. 6 tiếng.</description></item>
/// <item><description><c>LateNight</c> (23:00 – 06:00): Phiên khuya qua đêm, cover 24/7.</description></item>
/// </list>
/// </remarks>
public enum TimeSlot
{
    /// <summary>06:00 – 12:00 (Phiên sáng).</summary>
    Morning = 0,

    /// <summary>12:00 – 17:00 (Phiên chiều).</summary>
    Afternoon = 1,

    /// <summary>17:00 – 23:00 (Phiên tối).</summary>
    Evening = 2,

    /// <summary>23:00 – 06:00 (Phiên khuya qua đêm, endTime = 06:00 ngày hôm sau).</summary>
    LateNight = 3
}