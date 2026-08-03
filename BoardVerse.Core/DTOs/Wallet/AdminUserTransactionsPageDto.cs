namespace BoardVerse.Core.DTOs.Wallet;

/// <summary>
/// Lịch sử giao dịch BVC của một user — dùng cho admin xem user khác.
/// </summary>
public class AdminUserTransactionsPageDto
{
    /// <summary>UserId của player được xem.</summary>
    public Guid UserId { get; set; }

    /// <summary>Tên/email user để admin biết đang xem ai.</summary>
    public string? UserDisplayName { get; set; }

    public IReadOnlyList<BvcTransactionDto> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalItems / PageSize);
}
