using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Lobby;

/// <summary>
/// Request body cho BR-NEW-14 (b): đổi timeSlot và/hoặc preferred times của lobby.
/// </summary>
public class ChangeTimeSlotRequestDto
{
    /// <summary>
    /// Khung giờ mới: morning, afternoon, evening, night.
    /// Nếu null → giữ nguyên TimeSlot hiện tại.
    /// </summary>
    public TimeSlot? NewTimeSlot { get; set; }

    /// <summary>
    /// Giờ bắt đầu ưu tiên mới (HH:mm).
    /// Phải nằm trong [timeSlot.startTime, timeSlot.endTime].
    /// Nếu null → giữ nguyên giá trị hiện tại.
    /// </summary>
    public TimeOnly? PreferredStartTime { get; set; }

    /// <summary>
    /// Giờ kết thúc ưu tiên mới (HH:mm).
    /// Phải nằm trong [preferredStartTime, timeSlot.endTime].
    /// Nếu null → giữ nguyên giá trị hiện tại.
    /// </summary>
    public TimeOnly? PreferredEndTime { get; set; }
}
