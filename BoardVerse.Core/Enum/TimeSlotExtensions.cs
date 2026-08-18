namespace BoardVerse.Core.Enum;

/// <summary>
/// Extension methods for TimeSlot enum.
/// BR-NEW-15 (2026-08-18): TimeSlot enum is kept for backward compatibility with legacy lobbies/reservations.
/// New code should use PreferredStartTime/PreferredEndTime (TimeOnly) instead.
/// </summary>
public static class TimeSlotExtensions
{
    /// <summary>
    /// Get the default start time for a TimeSlot.
    /// Morning: 06:00, Afternoon: 12:00, Evening: 17:00, LateNight: 23:00.
    /// </summary>
    public static TimeOnly GetStartTime(this TimeSlot slot) => slot switch
    {
        TimeSlot.Morning => new TimeOnly(6, 0),
        TimeSlot.Afternoon => new TimeOnly(12, 0),
        TimeSlot.Evening => new TimeOnly(17, 0),
        TimeSlot.LateNight => new TimeOnly(23, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown TimeSlot value.")
    };

    /// <summary>
    /// Get the default end time for a TimeSlot.
    /// Morning: 12:00, Afternoon: 17:00, Evening: 23:00, LateNight: 06:00 (next day).
    /// </summary>
    public static TimeOnly GetEndTime(this TimeSlot slot) => slot switch
    {
        TimeSlot.Morning => new TimeOnly(12, 0),
        TimeSlot.Afternoon => new TimeOnly(17, 0),
        TimeSlot.Evening => new TimeOnly(23, 0),
        TimeSlot.LateNight => new TimeOnly(6, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown TimeSlot value.")
    };

    /// <summary>
    /// Get a human-readable name for the TimeSlot.
    /// </summary>
    public static string GetDisplayName(this TimeSlot slot) => slot switch
    {
        TimeSlot.Morning => "Sáng (06:00-12:00)",
        TimeSlot.Afternoon => "Chiều (12:00-17:00)",
        TimeSlot.Evening => "Tối (17:00-23:00)",
        TimeSlot.LateNight => "Khuya (23:00-06:00)",
        _ => slot.ToString()
    };

    /// <summary>
    /// Check if this TimeSlot represents an overnight session (LateNight crosses midnight).
    /// </summary>
    public static bool IsOvernight(this TimeSlot slot) => slot == TimeSlot.LateNight;

    /// <summary>
    /// Get the default duration in minutes for a TimeSlot.
    /// Morning: 360, Afternoon: 300, Evening: 360, LateNight: 420.
    /// </summary>
    public static int GetDurationMinutes(this TimeSlot slot) => slot switch
    {
        TimeSlot.Morning => 360,
        TimeSlot.Afternoon => 300,
        TimeSlot.Evening => 360,
        TimeSlot.LateNight => 420,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown TimeSlot value.")
    };
}
