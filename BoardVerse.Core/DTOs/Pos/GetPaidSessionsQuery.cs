namespace BoardVerse.Core.DTOs.Pos;

/// <summary>
/// Query cho GET /sessions/paid. Hỗ trợ filter theo ngày thanh toán
/// (mặc định hôm nay UTC) + phân trang.
/// </summary>
/// <remarks>
/// **Timezone (Bug #4 fix):** <c>FromDate</c> / <c>ToDate</c> là **UTC date** (theo <c>DateTime.UtcNow</c>).
/// Client phải convert local date → UTC date trước khi gọi. VD: ở VN (UTC+7) lúc 09:00 sáng 11/08
/// local → UTC = 02:00 ngày 11/08 → gửi <c>FromDate=2026-08-11</c>. Sai timezone sẽ lệch ±1 ngày.
/// </remarks>
public class GetPaidSessionsQuery
{
    private int _pageNumber = 1;
    private int _pageSize = 20;
    private DateOnly _fromDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private DateOnly _toDate = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Từ ngày UTC (inclusive) — lọc theo PaidAt.</summary>
    public DateOnly FromDate
    {
        get => _fromDate;
        set => _fromDate = value;
    }

    /// <summary>Đến ngày UTC (inclusive) — lọc theo PaidAt.</summary>
    public DateOnly ToDate
    {
        get => _toDate;
        set => _toDate = value;
    }

    /// <summary>Optional filter theo game template.</summary>
    public Guid? GameTemplateId { get; set; }

    /// <summary>Optional filter theo staff thực hiện thanh toán.</summary>
    public Guid? StaffId { get; set; }

    /// <summary>1-indexed page number.</summary>
    public int PageNumber
    {
        get => _pageNumber;
        set => _pageNumber = value < 1 ? 1 : value;
    }

    /// <summary>Page size (1-100, default 20).</summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 20 : (value > 100 ? 100 : value);
    }
}