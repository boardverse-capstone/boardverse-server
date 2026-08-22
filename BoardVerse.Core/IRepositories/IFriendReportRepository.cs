using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

public interface IFriendReportRepository
{
    Task<FriendReport?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<FriendReport?> GetPendingByReporterAndTargetAsync(Guid reporterId, Guid targetUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FriendReport>> GetByReporterAsync(Guid reporterId, CancellationToken cancellationToken = default);

    /// <summary>Admin: Lấy tất cả reports với filter theo status + phân trang.</summary>
    Task<(IReadOnlyList<FriendReport> Items, int Total)> GetAllForAdminAsync(
        string? status,
        int offset,
        int limit, CancellationToken cancellationToken = default);

    Task AddAsync(FriendReport report, CancellationToken cancellationToken = default);
    void Update(FriendReport report);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
