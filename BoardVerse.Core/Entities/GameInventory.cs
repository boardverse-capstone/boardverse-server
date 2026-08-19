namespace BoardVerse.Core.Entities;

/// <summary>
/// Tồn kho bản copy game theo cafe × playDate × timeSlot (BR-RESERVATION-02 §V + §19.11).
/// </summary>
public class GameInventory
{
    public Guid Id { get; set; }

    public Guid CafeId { get; set; }

    /// <summary>Game cụ thể (FK GameTemplate).</summary>
    public Guid GameId { get; set; }

    public DateOnly PlayDate { get; set; }

    public Core.Enum.TimeSlot TimeSlot { get; set; }

    /// <summary>Tổng số hộp cafe có cho game này trong khung giờ.</summary>
    public int TotalCopies { get; set; }

    /// <summary>Số bản đang được lobby/reservation giữ.</summary>
    public int HeldCopies { get; set; }

    /// <summary>Số bản đang phát cho khách chơi (in_use).</summary>
    public int InUseCopies { get; set; }

    public int AvailableCopies => TotalCopies - HeldCopies - InUseCopies;

    public uint RowVersion { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Cafe? Cafe { get; set; }
    public virtual GameTemplate? Game { get; set; }
}