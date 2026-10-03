using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Cafe;

namespace BoardVerse.Core.DTOs.Game;

/// <summary>
/// Response cho endpoint public <c>GET /api/v1/board-games/{boardgameId}/active-cafes</c>.
/// Wrap <see cref="PaginatedResponse{T}"/> của <see cref="NearbyCafeDto"/> kèm trạng thái
/// <see cref="IsSaved"/> của board game trong danh sách yêu thích (favorites) của player hiện tại.
///
/// <para>
/// Lý do wrap (không trả thẳng <c>PaginatedResponse</c>):
/// </para>
/// <list type="bullet">
///   <item><see cref="IsSaved"/> là thuộc tính của <em>board game được truy vấn</em> (một giá trị duy nhất),
///         không phải thuộc tính của từng cafe. Đặt ở top-level tránh trùng lặp dữ liệu trên từng item.</item>
///   <item>Khi danh sách cafe rỗng (<c>Cafes.Data = []</c>), client vẫn cần <see cref="IsSaved"/>
///         để hiển thị icon "đã lưu / chưa lưu" ở header trang detail game.</item>
///   <item>Khi player chưa đăng nhập, <see cref="IsSaved"/> luôn <c>false</c> (player chưa có danh sách yêu thích).</item>
/// </list>
/// </summary>
public class ActiveCafesByBoardGameResponseDto
{
    /// <summary>
    /// <c>true</c> nếu board game trong URL đang nằm trong danh sách yêu thích của player hiện tại.
    /// <c>false</c> nếu player chưa đăng nhập, chưa lưu game này, hoặc token không hợp lệ.
    /// Tương tự field <c>isSaved</c> trong <c>DiscoveryBoardGameDto</c> của endpoint <c>/api/v1/discovery/survey</c>.
    /// </summary>
    public bool IsSaved { get; set; }

    /// <summary>
    /// Danh sách quán cafe đang ACTIVE có board game này trong kho (phân trang, shape <see cref="NearbyCafeDto"/>).
    /// </summary>
    public PaginatedResponse<NearbyCafeDto> Cafes { get; set; } = new();
}
