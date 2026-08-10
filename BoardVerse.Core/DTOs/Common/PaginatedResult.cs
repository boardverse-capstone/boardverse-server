namespace BoardVerse.Core.DTOs.Common;

/// <summary>
/// Kết quả phân trang chung cho các list endpoint.
/// </summary>
/// <typeparam name="T">Kiểu phần tử trong danh sách.</typeparam>
public class PaginatedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => PageNumber < TotalPages;
    public bool HasPreviousPage => PageNumber > 1;
}