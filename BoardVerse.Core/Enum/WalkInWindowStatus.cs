namespace BoardVerse.Core.Enum;

/// <summary>
/// §9.3 + §4.4: Trạng thái của một WalkInWindow (khoảng thời gian trống có thể bán cho walk-in).
/// </summary>
public enum WalkInWindowStatus
{
    /// <summary>Window có ghế trống, đang nhận đặt walk-in.</summary>
    Available = 0,

    /// <summary>Window có một số ghế đã được giữ nhưng chưa đầy.</summary>
    Partial = 1,

    /// <summary>Tất cả ghế trong window đã được giữ cho walk-in booking.</summary>
    Full = 2,

    /// <summary>Window đã hết hạn (WindowEnd &lt; now) nhưng chưa được đóng sạch.</summary>
    Expired = 3,

    /// <summary>Window đã được đóng sạch (bởi POS hoặc background job).</summary>
    Closed = 4
}
