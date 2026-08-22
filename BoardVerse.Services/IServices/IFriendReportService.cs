using BoardVerse.Core.DTOs.Friend;

using System.Threading;
namespace BoardVerse.Services.IServices;

public interface IFriendReportService
{
    Task<FriendReportDto> SubmitReportAsync(Guid reporterId, CreateFriendReportDto dto, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FriendReportDto>> GetMyReportsAsync(Guid reporterId, CancellationToken cancellationToken = default);

    /// <summary>Admin: Lấy danh sách friend reports với filter theo status + phân trang.</summary>
    Task<(IReadOnlyList<FriendReportDto> Items, int Total)> GetAllForAdminAsync(
        string? status, int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>Admin: Đánh dấu report đã xử lý (Reviewed) hoặc bỏ qua (Dismissed).
    /// Ghi admin note để audit.</summary>
    Task<FriendReportDto> ResolveAsync(
        Guid adminUserId,
        Guid reportId,
        string newStatus,
        string? adminNote, CancellationToken cancellationToken = default);
}
