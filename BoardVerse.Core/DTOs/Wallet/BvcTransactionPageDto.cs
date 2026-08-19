namespace BoardVerse.Core.DTOs.Wallet;

/// <summary>Phản hồi phân trang cho lịch sử giao dịch BVC.</summary>
public class BvcTransactionPageDto
{
    public IReadOnlyList<BvcTransactionDto> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public bool HasMore => Page * PageSize < TotalItems;
}
