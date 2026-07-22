using BoardVerse.Core.DTOs.Friend;
using BoardVerse.Core.DTOs.Lobby;

namespace BoardVerse.Services.IServices;

public interface IFriendService
{
    /// <summary>
    /// Gửi lời mời kết bạn tới addresseeId.
    /// BR-FRIEND-01: Không gửi cho chính mình; không tạo trùng.
    /// </summary>
    Task<FriendshipResponseDto> SendFriendRequestAsync(Guid requesterId, SendFriendRequestDto request);

    /// <summary>
    /// Accept lời mời. Chỉ addressee mới có thể accept.
    /// </summary>
    Task<FriendshipResponseDto> AcceptFriendRequestAsync(Guid currentUserId, Guid friendshipId);

    /// <summary>
    /// Từ chối lời mời. Addressee từ chối → record chuyển Removed.
    /// </summary>
    Task<FriendshipResponseDto> DeclineFriendRequestAsync(Guid currentUserId, Guid friendshipId);

    /// <summary>
    /// Hủy kết bạn / xóa quan hệ. Cả 2 bên đều có thể xóa.
    /// </summary>
    Task RemoveFriendshipAsync(Guid currentUserId, Guid friendshipId);

    /// <summary>
    /// Chặn user. Sau khi chặn, user bị chặn không thể gửi friend request hoặc lobby invite.
    /// </summary>
    Task BlockUserAsync(Guid currentUserId, Guid targetUserId);

    /// <summary>
    /// Bỏ chặn user.
    /// </summary>
    Task UnblockUserAsync(Guid currentUserId, Guid targetUserId);

    /// <summary>
    /// Danh sách bạn bè (Accepted).
    /// </summary>
    Task<IReadOnlyList<FriendSummaryDto>> GetFriendsAsync(Guid userId);

    /// <summary>
    /// Lời mời đang Pending mà current user nhận được.
    /// </summary>
    Task<IReadOnlyList<FriendshipResponseDto>> GetPendingReceivedRequestsAsync(Guid userId);

    /// <summary>
    /// Lời mời đã gửi đi nhưng chưa được phản hồi.
    /// </summary>
    Task<IReadOnlyList<FriendshipResponseDto>> GetPendingSentRequestsAsync(Guid userId);

    /// <summary>
    /// Tìm user theo username cho friend search. Trả về thêm trạng thái quan hệ hiện tại + mutual friend count.
    /// </summary>
    Task<IReadOnlyList<UserSearchResultDto>> SearchUsersAsync(Guid currentUserId, string keyword, int limit = 20);

    // === Activity / Suggestions / Mutual / Privacy / Note / Report ===

    /// <summary>
    /// Lấy danh sách bạn bè kèm trạng thái hoạt động (online, recently active, away, offline).
    /// </summary>
    Task<IReadOnlyList<FriendActivityDto>> GetFriendsActivityAsync(Guid userId);

    /// <summary>
    /// Gợi ý kết bạn: bạn của bạn, người cùng chơi trong lobby gần đây.
    /// </summary>
    Task<IReadOnlyList<FriendSuggestionDto>> GetFriendSuggestionsAsync(Guid userId, int limit = 20);

    /// <summary>
    /// Lấy danh sách bạn chung giữa currentUser và otherUser.
    /// </summary>
    Task<IReadOnlyList<MutualFriendDto>> GetMutualFriendsAsync(Guid currentUserId, Guid otherUserId);

    /// <summary>
    /// Xem friend list của user khác (tôn trọng privacy).
    /// </summary>
    Task<IReadOnlyList<FriendSummaryDto>> GetOtherUserFriendsAsync(Guid currentUserId, Guid otherUserId);

    /// <summary>
    /// Cập nhật quyền riêng tư cho friend list.
    /// </summary>
    Task UpdatePrivacyAsync(Guid userId, UpdateFriendPrivacyDto dto);

    /// <summary>
    /// Đánh dấu đã đọc lời mời kết bạn (cho current user = addressee).
    /// </summary>
    Task MarkRequestAsReadAsync(Guid currentUserId, Guid friendshipId);

    /// <summary>
    /// Auto-expire các friend request Pending quá hạn (BR-FRIEND-05).
    /// </summary>
    Task<int> ExpireOldPendingRequestsAsync(int expiryDays = 30);
}
