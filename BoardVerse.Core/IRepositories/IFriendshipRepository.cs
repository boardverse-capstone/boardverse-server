using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho quan hệ bạn bè.
/// </summary>
public interface IFriendshipRepository
{
    Task<Friendship?> GetByIdAsync(Guid id);
    Task<Friendship?> GetByPairAsync(Guid userAId, Guid userBId);

    /// <summary>
    /// Lấy tất cả quan hệ (Pending + Accepted) của user.
    /// </summary>
    Task<IReadOnlyList<Friendship>> GetByUserAsync(Guid userId, FriendshipStatus? status = null);

    Task<IReadOnlyList<Friendship>> GetFriendsAsync(Guid userId);

    Task<int> CountFriendsAsync(Guid userId);

    /// <summary>
    /// Lấy danh sách friend đã Accepted (chỉ UserId) của user.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetFriendUserIdsAsync(Guid userId);

    /// <summary>
    /// Đếm số bạn chung giữa 2 user.
    /// </summary>
    Task<int> CountMutualFriendsAsync(Guid userAId, Guid userBId);

    /// <summary>
    /// Lấy danh sách bạn chung giữa currentUser và otherUser (chỉ UserId).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetMutualFriendIdsAsync(Guid currentUserId, Guid otherUserId);

    /// <summary>
    /// Lấy các friendship Pending quá hạn (CreatedAt &lt; cutoff).
    /// </summary>
    Task<IReadOnlyList<Friendship>> GetExpiredPendingAsync(DateTime cutoff);

    Task AddAsync(Friendship friendship);
    Task SaveChangesAsync();
}
