namespace BoardVerse.Core.Entities;

/// <summary>
/// Tồn kho ghế theo cafe × playDate × timeSlot (BR-RESERVATION-01 §V + §19.11).
/// BR-NEW-15 (2026-08-18): Dùng ScheduledStartTime/ScheduledEndTime (TimeOnly) thay vì TimeSlot enum.
/// </summary>
public class SeatInventory
{
    public Guid Id { get; set; }

    /// <summary>FK Cafe.</summary>
    public Guid CafeId { get; set; }

    /// <summary>BR-NEW-04: ngày dự kiến chơi (chỉ ngày).</summary>
    public DateOnly PlayDate { get; set; }

    /// <summary>BR-NEW-15: Giờ bắt đầu dự kiến (thay vì TimeSlot enum).</summary>
    public TimeOnly ScheduledStartTime { get; set; }

    /// <summary>BR-NEW-15: Giờ kết thúc dự kiến.</summary>
    public TimeOnly ScheduledEndTime { get; set; }

    /// <summary>Tổng số ghế của cafe trong khung này (snapshot từ CafeCapacity).</summary>
    public int TotalSeats { get; set; }

    /// <summary>Đang bị giữ cho reservation/lobby chưa check-in.</summary>
    public int HeldSeats { get; set; }

    /// <summary>Đã check-in (ActiveSession in_use).</summary>
    public int InUseSeats { get; set; }

    /// <summary>Ghế khả dụng = Total - Held - InUse.</summary>
    public int AvailableSeats => TotalSeats - HeldSeats - InUseSeats;

    /// <summary>Optimistic concurrency token (uint — tăng mỗi UPDATE).</summary>
    public uint RowVersion { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Cafe? Cafe { get; set; }
}
