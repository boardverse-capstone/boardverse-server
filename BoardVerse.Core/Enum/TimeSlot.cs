namespace BoardVerse.Core.Enum;

/// <summary>
/// Khung giờ cố định cho lobby (BR-NEW-15 §7.1, cập nhật cover 24h).
/// Không cho phép cafe config thêm khung mới — chỉ override tên hiển thị + giờ qua <c>CafeScheduleOverride</c>.
/// </summary>
/// <remarks>
/// Mapping (boardverse.mdc + BR-NEW-15):
/// <list type="bullet">
/// <item><description><c>Morning</c> (08:00 – 13:00): Phiên sáng.</description></item>
/// <item><description><c>Afternoon</c> (13:00 – 18:00): Phiên chiều.</description></item>
/// <item><description><c>Evening</c> (18:00 – 24:00): Phiên tối.</description></item>
/// <item><description><c>Night</c> (00:00 – 08:00): Phiên khuya qua đêm (scheduledTime = playDate 00:00, endTime = playDate+1 08:00).</description></item>
/// </list>
/// </remarks>
public enum TimeSlot
{
    /// <summary>08:00 – 13:00 (Phiên sáng).</summary>
    Morning = 0,

    /// <summary>13:00 – 18:00 (Phiên chiều).</summary>
    Afternoon = 1,

    /// <summary>18:00 – 24:00 (Phiên tối).</summary>
    Evening = 2,

    /// <summary>00:00 – 08:00 (Phiên khuya, qua đêm).</summary>
    Night = 3
}
