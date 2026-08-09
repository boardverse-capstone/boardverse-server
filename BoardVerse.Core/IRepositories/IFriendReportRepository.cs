using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

public interface IFriendReportRepository
{
    Task<FriendReport?> GetByIdAsync(Guid id);
    Task<FriendReport?> GetPendingByReporterAndTargetAsync(Guid reporterId, Guid targetUserId);
    Task<IReadOnlyList<FriendReport>> GetByReporterAsync(Guid reporterId);

    /// <summary>Admin: Lấy tất cả reports với filter theo status + phân trang.</summary>
    Task<(IReadOnlyList<FriendReport> Items, int Total)> GetAllForAdminAsync(
        string? status,
        int offset,
        int limit);

    Task AddAsync(FriendReport report);
    void Update(FriendReport report);
    Task SaveChangesAsync();
}
