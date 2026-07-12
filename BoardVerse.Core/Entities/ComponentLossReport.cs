using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.Entities;

public class ComponentLossReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CafeId { get; set; }
    public Guid? ActiveSessionId { get; set; }
    public Guid? CafeInventoryBoxId { get; set; }
    public Guid ReportedByUserId { get; set; }
    public string LossDescription { get; set; } = string.Empty;
    public decimal TotalPenaltyAmount { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual Cafe Cafe { get; set; } = null!;
    public virtual ActiveSession? ActiveSession { get; set; }
    public virtual CafeInventoryBox? CafeInventoryBox { get; set; }
}
