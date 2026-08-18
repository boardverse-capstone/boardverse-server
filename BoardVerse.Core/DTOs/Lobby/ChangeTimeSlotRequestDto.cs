namespace BoardVerse.Core.DTOs.Lobby;

/// <summary>
/// Request body cho vi\u1ec7c \u0111\u1ed5i preferred start/end time c\u1ee7a lobby.
/// BR-NEW-15 (2026-08-18): B\u1ecf TimeSlot enum, d\u00f9ng preferredStartTime/preferredEndTime tr\u1ef1c ti\u1ebfp.
/// </summary>
public class ChangeTimeSlotRequestDto
{
    /// <summary>
    /// Gi\u1edd b\u1eaft \u0111\u1ea7u \u01b0u ti\u00ean m\u1edbi (HH:mm).
    /// Ph\u1ea3i n\u1eb1m trong khung gi\u1edd m\u1edf c\u1eedra c\u1ee7a cafe.
    /// N\u1ebfu null \u2192 gi\u1eef nguy\u00ean gi\u00e1 tr\u1ecb hi\u1ec7n t\u1ea1i.
    /// </summary>
    public TimeOnly? PreferredStartTime { get; set; }

    /// <summary>
    /// Gi\u1edd k\u1ebft th\u00fac \u01b0u ti\u00ean m\u1edbi (HH:mm).
    /// Ph\u1ea3i n\u1eb1m trong khung gi\u1edd m\u1edf c\u1eedra c\u1ee7a cafe, &gt; preferredStartTime.
    /// N\u1ebfu null \u2192 gi\u1eef nguy\u00ean gi\u00e1 tr\u1ecb hi\u1ec7n t\u1ea1i.
    /// </summary>
    public TimeOnly? PreferredEndTime { get; set; }
}