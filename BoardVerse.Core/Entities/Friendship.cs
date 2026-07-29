using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Quan hệ bạn bè giữa hai người dùng trên BoardVerse.
/// Mỗi record đại diện một chiều quan hệ (Requester → Addressee).
/// BR-FRIEND-01: Một cặp (RequesterId, AddresseeId) chỉ tồn tại duy nhất 1 record.
/// BR-FRIEND-02: RequesterId != AddresseeId.
/// BR-FRIEND-03: Status chuyển Pending → Accepted/Blocked; Accepted → Removed; Blocked → Removed.
/// BR-FRIEND-04: FriendRequest có Message tối đa 200 ký tự (nullable).
/// BR-FRIEND-05: Tự động expire sau FriendRequestExpiryDays (mặc định 30 ngày).
/// BR-FRIEND-06: Addressee có thể đánh dấu "đã đọc" qua AddresseeReadAt.
/// </summary>
public class Friendship
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Người gửi lời mời kết bạn.</summary>
    public Guid RequesterId { get; set; }

    /// <summary>Người nhận lời mời kết bạn.</summary>
    public Guid AddresseeId { get; set; }

    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;

    /// <summary>Lời nhắn kèm theo lời mời (tối đa 200 ký tự).</summary>
    public string? Message { get; set; }

    /// <summary>Thời điểm accept (null khi chưa accept).</summary>
    public DateTime? AcceptedAt { get; set; }

    /// <summary>Thời điểm đổi trạng thái cuối cùng (gửi/accept/block/remove).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Thời điểm addressee đánh dấu đã đọc lời mời (cho inbox notification).</summary>
    public DateTime? AddresseeReadAt { get; set; }

    /// <summary>
    /// BR-FRIEND-BLOCK-VIEW: Người đã chặn (set khi Status = Blocked).
    /// Vì 1 cặp (Requester, Addressee) chỉ có 1 record và cả 2 có thể block nhau,
    /// cần lưu riêng field này để biết ai đã block trước/sau, không phụ thuộc vào RequesterId.
    /// Null khi Status != Blocked.
    /// </summary>
    public Guid? BlockerUserId { get; set; }

    // Navigation
    public virtual User Requester { get; set; } = null!;
    public virtual User Addressee { get; set; } = null!;
}