namespace BoardVerse.Core.Enum;

/// <summary>
/// Loại tham gia của user với reservation.
/// Dùng để phân biệt reservation user host với reservation user join.
/// </summary>
public enum ReservationParticipationType
{
    /// <summary>User là host (người tạo) của reservation này.</summary>
    Host = 0,

    /// <summary>User là member (người tham gia) của reservation này.</summary>
    Member = 1
}
