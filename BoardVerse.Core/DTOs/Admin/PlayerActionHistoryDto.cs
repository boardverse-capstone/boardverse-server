using BoardVerse.Core.Common;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Admin;

/// <summary>
/// A-03: DTO trả lịch sử admin action cho 1 user (BR-RISK-05).
/// </summary>
public class PlayerActionHistoryDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public AdminActionType ActionType { get; set; }
    public Guid ActionBy { get; set; }
    public string? ActionByUsername { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// BR-RISK-05: Param cho endpoint liệt kê PlayerActionHistory (filter theo user, actionType, range ngày).
/// </summary>
public class PlayerActionHistoryQuery : PaginationParams
{
    public Guid? UserId { get; set; }
    public AdminActionType? ActionType { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
}
