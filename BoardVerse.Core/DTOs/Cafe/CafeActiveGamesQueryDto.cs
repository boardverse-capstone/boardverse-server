namespace BoardVerse.Core.DTOs.Cafe;

/// <summary>
/// Query cho endpoint public <c>GET /api/cafes/{cafeId}/active-games</c>.
/// Tất cả field đều optional — null nghĩa là "không filter".
/// </summary>
public class CafeActiveGamesQueryDto
{
    /// <summary>
    /// Lọc theo category id (bất kỳ thể loại nào trong list trùng khớp). Null = tất cả category.
    /// </summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// Lọc theo số người chơi tối thiểu của nhóm player.
    /// Chỉ trả game có <c>MinPlayers &lt;= groupSize</c>. Null = không filter.
    /// </summary>
    public int? GroupSize { get; set; }

    /// <summary>
    /// Chỉ trả game đang có ít nhất 1 hộp <c>Available</c>.
    /// Mặc định <c>false</c> (trả cả <c>Available</c> + <c>InUse</c>).
    /// </summary>
    public bool AvailableOnly { get; set; } = false;

    /// <summary>
    /// Tìm kiếm theo tên game (case-insensitive, partial match). Null/empty = không search.
    /// </summary>
    public string? SearchTerm { get; set; }

    /// <summary>
    /// Sắp xếp kết quả. Mặc định <see cref="CafeActiveGamesSort.Name"/>.
    /// </summary>
    public CafeActiveGamesSort SortBy { get; set; } = CafeActiveGamesSort.Name;

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Sort options cho endpoint <c>GET /api/cafes/{cafeId}/active-games</c>.
/// </summary>
public enum CafeActiveGamesSort
{
    /// <summary>Theo tên game A→Z (mặc định).</summary>
    Name = 0,

    /// <summary>Theo số hộp Available giảm dần — game càng nhiều hộp trống càng trước.</summary>
    AvailableBoxesDesc = 1,

    /// <summary>Theo <c>PlayTime</c> tăng dần — game ngắn trước (player chọn nhanh).</summary>
    PlayTimeAsc = 2,

    /// <summary>Theo <c>MinPlayers</c> tăng dần, sau đó <c>MaxPlayers</c> tăng dần.</summary>
    PlayerCountAsc = 3,
}
