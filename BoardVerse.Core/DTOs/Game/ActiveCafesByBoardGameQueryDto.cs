namespace BoardVerse.Core.DTOs.Game;

/// <summary>
/// Query cho endpoint public <c>GET /api/v1/board-games/{boardgameId}/active-cafes</c>.
/// Trả về danh sách quán cafe đang hoạt động có board game này trong kho với trạng thái
/// <c>Available</c> hoặc <c>InUse</c> (nghĩa là game đang được mở cho khách chơi).
/// Tất cả field đều optional — null nghĩa là "không filter".
/// </summary>
public class ActiveCafesByBoardGameQueryDto
{
    /// <summary>
    /// Vĩ độ player (WGS84, -90 đến 90). Khi truyền cùng <see cref="Longitude"/>,
    /// server tính <c>DistanceMeters</c> cho từng quán và sắp xếp theo khoảng cách tăng dần.
    /// Bỏ trống → sắp xếp theo tên quán A→Z (giống <c>GET /api/cafes</c>).
    /// </summary>
    public double? Latitude { get; set; }

    /// <summary>
    /// Kinh độ player (WGS84, -180 đến 180). Phải truyền kèm <see cref="Latitude"/>;
    /// nếu chỉ truyền 1 trong 2 → bỏ qua, dùng sort theo tên.
    /// </summary>
    public double? Longitude { get; set; }

    /// <summary>
    /// Tìm kiếm theo tên quán (case-insensitive, partial match). Null/empty = không filter.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>Số trang (mặc định 1).</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>Kích thước trang (mặc định 20).</summary>
    public int PageSize { get; set; } = 20;
}
