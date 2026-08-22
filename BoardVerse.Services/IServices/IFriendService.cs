using BoardVerse.Core.DTOs.Friend;
using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Services.IServices;

public interface IFriendService
{
    /// <summary>
    /// Gửi lời mời kết bạn tới addresseeId.
    /// BR-FRIEND-01: Không gửi cho chính mình; không tạo trùng.
    /// </summary>
    Task<FriendshipResponseDto> SendFriendRequestAsync(Guid requesterId, SendFriendRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Accept lời mời. Chỉ addressee mới có thể accept.
    /// </summary>
    Task<FriendshipResponseDto> AcceptFriendRequestAsync(Guid currentUserId, Guid friendshipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Từ chối lời mời. Addressee từ chối → record chuyển Removed.
    /// </summary>
    Task<FriendshipResponseDto> DeclineFriendRequestAsync(Guid currentUserId, Guid friendshipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hủy lời mời kết bạn đã gửi. Chỉ requester mới có thể hủy, và chỉ khi còn Pending.
    /// </summary>
    Task CancelFriendRequestAsync(Guid currentUserId, Guid friendshipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy chi tiết 1 lời mời kết bạn theo id (cho notification deeplink).
    /// Chỉ requester hoặc addressee mới xem được.
    /// </summary>
    Task<FriendshipResponseDto> GetFriendRequestByIdAsync(Guid currentUserId, Guid friendshipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách user mà current user đã chặn (Status=Blocked + BlockerUserId=current).
    /// </summary>
    Task<IReadOnlyList<FriendshipResponseDto>> GetBlockedUsersAsync(Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách user đã chặn current user (Status=Blocked + BlockerUserId != current).
    /// Dùng cho UI debug/explain tại sao action không thành công.
    /// </summary>
    Task<IReadOnlyList<FriendshipResponseDto>> GetBlockedByUsersAsync(Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hủy kết bạn / xóa quan hệ. Cả 2 bên đều có thể xóa.
    /// </summary>
    Task RemoveFriendshipAsync(Guid currentUserId, Guid friendshipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chặn user. Sau khi chặn, user bị chặn không thể gửi friend request hoặc lobby invite.
    /// </summary>
    Task BlockUserAsync(Guid currentUserId, Guid targetUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bỏ chặn user.
    /// </summary>
    Task UnblockUserAsync(Guid currentUserId, Guid targetUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Danh sách bạn bè (Accepted).
    /// </summary>
    Task<IReadOnlyList<FriendSummaryDto>> GetFriendsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lời mời đang Pending mà current user nhận được.
    /// </summary>
    Task<IReadOnlyList<FriendshipResponseDto>> GetPendingReceivedRequestsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lời mời đã gửi đi nhưng chưa được phản hồi.
    /// </summary>
    Task<IReadOnlyList<FriendshipResponseDto>> GetPendingSentRequestsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách quan hệ bạn bè lọc theo direction từ góc nhìn current user (BR-FRIEND-UI-DIRECTION-01).
    /// <para>Direction = <see cref="FriendshipRelationshipDirection.None"/> trả về empty list.</para>
    /// </summary>
    Task<IReadOnlyList<FriendshipResponseDto>> GetByDirectionAsync(
        Guid currentUserId,
        FriendshipRelationshipDirection direction,
        int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tìm user theo username cho friend search. Trả về thêm trạng thái quan hệ hiện tại + mutual friend count.
    /// </summary>
    Task<IReadOnlyList<UserSearchResultDto>> SearchUsersAsync(Guid currentUserId, string keyword, int limit = 20);

    // === Activity / Suggestions / Mutual / Privacy / Note / Report ===

    /// <summary>
    /// Lấy danh sách bạn bè kèm trạng thái hoạt động (online, recently active, away, offline).
    /// </summary>
    Task<IReadOnlyList<FriendActivityDto>> GetFriendsActivityAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gợi ý kết bạn: bạn của bạn, người cùng chơi trong lobby gần đây.
    /// </summary>
    Task<IReadOnlyList<FriendSuggestionDto>> GetFriendSuggestionsAsync(Guid userId, int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách bạn chung giữa currentUser và otherUser.
    /// </summary>
    Task<IReadOnlyList<MutualFriendDto>> GetMutualFriendsAsync(Guid currentUserId, Guid otherUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xem friend list của user khác (tôn trọng privacy).
    /// </summary>
    Task<IReadOnlyList<FriendSummaryDto>> GetOtherUserFriendsAsync(Guid currentUserId, Guid otherUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật quyền riêng tư cho friend list.
    /// </summary>
    Task UpdatePrivacyAsync(Guid userId, UpdateFriendPrivacyDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đánh dấu đã đọc lời mời kết bạn (cho current user = addressee).
    /// </summary>
    Task MarkRequestAsReadAsync(Guid currentUserId, Guid friendshipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Auto-expire các friend request Pending quá hạn (BR-FRIEND-05).
    /// </summary>
    Task<int> ExpireOldPendingRequestsAsync(int expiryDays = 30, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xem chi tiết public profile của 1 player, kèm:
    /// - Quan hệ hiện tại giữa current user và target (None / PendingSent / PendingReceived / Accepted / Blocked).
    /// - Số bạn chung.
    /// - Số bạn bè (tôn trọng IsFriendListPublic: chỉ trả count, không trả list).
    /// - Permission flags (canSendFriendRequest, canReport) — canReport chỉ true khi đã Accepted (BR-FRIEND-REPORT-01).
    /// Trả 404 nếu target không tồn tại, bị block 2 chiều, hoặc account không Active.
    /// </summary>
    Task<PlayerProfileDto> GetPlayerProfileAsync(Guid currentUserId, Guid targetUserId, CancellationToken cancellationToken = default);
}
