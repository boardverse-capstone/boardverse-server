using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.CafeShift;

public class CafeShiftResponseDto
{
    public Guid Id { get; set; }
    public Guid CafeId { get; set; }
    public Guid OpenedByUserId { get; set; }

    /// <summary>
    /// Tên hiển thị của người mở ca. Fallback chain: Profile.LastResolvedDisplayName
    /// → FirstName + LastName → Username. Null nếu user đã bị xóa.
    /// </summary>
    public string? OpenedByUserName { get; set; }

    public Guid? ClosedByUserId { get; set; }

    /// <summary>
    /// Tên hiển thị của người đóng ca. Fallback chain tương tự <see cref="OpenedByUserName"/>.
    /// Null nếu ca chưa đóng hoặc user đã bị xóa.
    /// </summary>
    public string? ClosedByUserName { get; set; }

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
