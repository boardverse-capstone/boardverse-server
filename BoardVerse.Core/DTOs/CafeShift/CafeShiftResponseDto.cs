using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.CafeShift;

public class CafeShiftResponseDto
{
    public Guid Id { get; set; }
    public Guid CafeId { get; set; }
    public Guid OpenedByUserId { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningCashBalance { get; set; }
    public decimal ClosingCashBalance { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalSessions { get; set; }
    public ShiftStatus Status { get; set; }
}

public class CafeShiftHistoryResponseDto
{
    public IReadOnlyList<CafeShiftResponseDto> Shifts { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
