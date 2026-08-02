namespace BoardVerse.Core.Enum;

/// <summary>
/// Khung giờ cố định cho lobby (BR-NEW-15 §7.1).
/// Không cho phép cafe config thêm khung mới — chỉ override tên hiển thị.
/// </summary>
public enum TimeSlot
{
    /// <summary>09:00 – 13:00 (Phiên sáng).</summary>
    Morning = 0,

    /// <summary>13:00 – 18:00 (Phiên chiều).</summary>
    Afternoon = 1,

    /// <summary>18:00 – 23:00 (Phiên tối).</summary>
    Evening = 2,

    /// <summary>19:00 – 24:00 (Phiên khuya).</summary>
    Night = 3
}
