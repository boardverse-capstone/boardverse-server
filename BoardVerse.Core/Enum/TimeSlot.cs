namespace BoardVerse.Core.Enum;

/// <summary>
/// Khung giờ cố định cho lobby (BR-NEW-15 §7.1, đồng bộ docs/time-slot-fixed-end-design (1).md §13).
/// Không cho phép cafe config thêm khung mới — chỉ override tên hiển thị + giờ qua <c>CafeScheduleOverride</c>.
/// </summary>
/// <remarks>
/// Mapping (boardverse.mdc + docs/time-slot-fixed-end-design (1).md):
/// <list type="bullet">
/// <item><description><c>Morning</c> (09:00 – 13:00): Phiên sáng. 4 tiếng.</description></item>
/// <item><description><c>Afternoon</c> (13:00 – 18:00): Phiên chiều. 5 tiếng.</description></item>
/// <item><description><c>Evening</c> (18:00 – 23:00): Phiên tối. 5 tiếng.</description></item>
/// <item><description><c>Night</c> (19:00 – 24:00): Phiên khuya, endTime = 24:00 cùng ngày (BR-RES-08).</description></item>
/// </list>
/// </remarks>
public enum TimeSlot
{
    /// <summary>09:00 – 13:00 (Phiên sáng).</summary>
    Morning = 0,

    /// <summary>13:00 – 18:00 (Phiên chiều).</summary>
    Afternoon = 1,

    /// <summary>18:00 – 23:00 (Phiên tối).</summary>
    Evening = 2,

    /// <summary>19:00 – 24:00 (Phiên khuya, endTime cùng ngày — BR-RES-08).</summary>
    Night = 3
}