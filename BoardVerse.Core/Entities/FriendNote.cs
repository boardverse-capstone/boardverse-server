namespace BoardVerse.Core.Entities;

/// <summary>
/// Ghi chú/alias riêng mà user đặt cho một người bạn (vd: "Anh Cường - Catan").
/// BR-FRIEND-NOTE-01: Một cặp (OwnerUserId, FriendUserId) chỉ có 1 record.
/// BR-FRIEND-NOTE-02: Chỉ chủ sở hữu mới đọc/sửa/xóa.
/// </summary>
public class FriendNote
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User tạo ghi chú.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>User được gắn ghi chú.</summary>
    public Guid FriendUserId { get; set; }

    /// <summary>Alias hiển thị cho bạn bè.</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>Ghi chú chi tiết (vd: chơi Catan tốt, thích chơi tối).</summary>
    public string? Note { get; set; }

    /// <summary>Tag phân loại (vd: "Catan", "Wingman").</summary>
    public string? Tags { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual User Owner { get; set; } = null!;
    public virtual User Friend { get; set; } = null!;
}
