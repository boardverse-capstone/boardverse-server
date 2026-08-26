using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho quan hệ bạn bè.
/// </summary>
public interface IFriendshipRepository
{
    Task<Friendship?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Friendship?> GetByPairAsync(Guid userAId, Guid userBId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy tất cả quan hệ (Pending + Accepted) của user.
    /// </summary>
    Task<IReadOnlyList<Friendship>> GetByUserAsync(Guid userId, FriendshipStatus? status = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Friendship>> GetFriendsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<int> CountFriendsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách friend đã Accepted (chỉ UserId) của user.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetFriendUserIdsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch query: lấy tập friends-of-friends cho nhiều userIds trong 1 round-trip DB.
    /// Trả về Dictionary&lt;sourceUserId, list of friends&gt;. Dùng cho FriendService.GetFriendSuggestionsAsync
    /// để tránh N+1 (loop qua từng friendId → 1 query per friend).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetFriendsForUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đếm số bạn chung giữa 2 user.
    /// </summary>
    Task<int> CountMutualFriendsAsync(Guid userAId, Guid userBId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách bạn chung giữa currentUser và otherUser (chỉ UserId).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetMutualFriendIdsAsync(Guid currentUserId, Guid otherUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy các friendship Pending quá hạn (CreatedAt &lt; cutoff).
    /// </summary>
    Task<IReadOnlyList<Friendship>> GetExpiredPendingAsync(DateTime cutoff, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách UserId bị block (cả 2 chiều: tôi chặn họ + họ chặn tôi).
    /// Dùng để filter khỏi Search/Suggestions (BR-FRIEND-SEARCH-BLOCK-FILTER).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetBlockedUserIdsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// M2: Kiểm tra user có phải bạn bè Accepted của bất kỳ user nào trong candidateUserIds không.
    /// Trả về true nếu tồn tại friendship Accepted giữa userId và bất kỳ candidate.
    /// Dùng cho BR-LOBBY-PRIVACY-03 share-code join — tránh N+1 khi N = số member active.
    /// </summary>
    Task<bool> IsAcceptedFriendOfAnyAsync(Guid userId, IReadOnlyCollection<Guid> candidateUserIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lọc Friendship theo direction tính từ góc nhìn currentUser (BR-FRIEND-UI-DIRECTION-01).
    /// Direction được dịch sang WHERE clause dựa trên Status + RequesterId/AddresseeId/BlockerUserId.
    /// </summary>
    /// <param name="currentUserId">User hiện tại (góc nhìn).</param>
    /// <param name="direction">Direction cần lọc (None trả về empty list).</param>
    /// <param name="limit">Giới hạn số kết quả.</param>
    Task<IReadOnlyList<Friendship>> GetByDirectionAsync(
        Guid currentUserId,
        FriendshipRelationshipDirection direction,
        int limit = 50, CancellationToken cancellationToken = default);

    Task AddAsync(Friendship friendship, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
