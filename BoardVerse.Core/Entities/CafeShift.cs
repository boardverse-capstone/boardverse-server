using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

public class CafeShift
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CafeId { get; set; }
    public Guid OpenedByUserId { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningCashBalance { get; set; }
    public decimal ClosingCashBalance { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalSessions { get; set; }
    public ShiftStatus Status { get; set; } = ShiftStatus.Open;

    public virtual Cafe Cafe { get; set; } = null!;
}
