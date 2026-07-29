namespace BoardVerse.Core.Enum;

/// <summary>
/// Hướng quan hệ bạn bè giữa current user và một user khác — dùng cho UI search/suggestions/list.
/// BR-FRIEND-UI-DIRECTION-01: Phải tính từ góc nhìn current user (RequesterId vs AddresseeId),
/// không chỉ dựa vào DB status thô vì cùng Status = Pending có thể là "đã gửi" hoặc "đã nhận".
///
/// ⚠️ DTO-ONLY enum: KHÔNG map xuống DB, KHÔNG cần migration.
/// Giá trị này được tính toán runtime từ <see cref="FriendshipStatus"/> + RequesterId/AddresseeId/BlockerUserId
/// trong FriendService.ResolveDirection(...) và serialize ra JSON response cho FE.
/// </summary>
public enum FriendshipRelationshipDirection
{
    /// <summary>Chưa có quan hệ (chưa từng gửi request, hoặc đã Removed/Declined).</summary>
    None = 0,

    /// <summary>Current user là requester, Status = Pending → "Đã gửi lời mời".</summary>
    OutgoingRequest = 1,

    /// <summary>Current user là addressee, Status = Pending → "Chấp nhận / Từ chối".</summary>
    IncomingRequest = 2,

    /// <summary>Status = Accepted → "Bạn bè" (cả 2 chiều đều giống nhau).</summary>
    Accepted = 3,

    /// <summary>Current user đã chặn đối phương (BlockerUserId = currentUser) → "Bỏ chặn".</summary>
    BlockedByMe = 4,

    /// <summary>Đối phương đã chặn current user (BlockerUserId = other) → Ẩn/Disable UI.</summary>
    BlockedByThem = 5
}