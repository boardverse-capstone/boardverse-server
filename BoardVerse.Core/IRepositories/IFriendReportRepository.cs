using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

public interface IFriendReportRepository
{
    Task<FriendReport?> GetByIdAsync(Guid id);
    Task<FriendReport?> GetPendingByReporterAndTargetAsync(Guid reporterId, Guid targetUserId);
    Task<IReadOnlyList<FriendReport>> GetByReporterAsync(Guid reporterId);
    Task AddAsync(FriendReport report);
    void Update(FriendReport report);
    Task SaveChangesAsync();
}
