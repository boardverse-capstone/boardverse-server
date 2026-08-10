using BoardVerse.Core.Entities;

namespace BoardVerse.Core.DTOs.Pos;

/// <summary>
/// Kết quả phân trang cho paid sessions (raw từ repository).
/// Service layer sẽ map sang <c>PaginatedResult&lt;PaidSessionDto&gt;</c>.
/// </summary>
public class PaidSessionsPagedResult
{
    public IReadOnlyList<ActiveSession> Items { get; set; } = [];
    public int TotalCount { get; set; }
}