namespace BoardVerse.Core.Entities;

/// <summary>
/// Tin nhắn chat trong lobby (giữa các thành viên).
/// BR-LOBBY-CHAT-01: Chỉ member active mới gửi/xem được.
/// BR-LOBBY-CHAT-02: Sau khi lobby Closed, vẫn xem được history.
/// BR-LOBBY-CHAT-03: Message tối đa 1000 ký tự.
/// </summary>
public class LobbyMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LobbyId { get; set; }
    
    /// <summary>Nullable cho system messages (SenderId = null khi IsSystem = true).</summary>
    public Guid? SenderId { get; set; }

    /// <summary>Nội dung tin nhắn (1-1000 ký tự).</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>System message (vd: "Alice joined the lobby"). True nếu do hệ thống tạo.</summary>
    public bool IsSystem { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Lobby Lobby { get; set; } = null!;
    
    /// <summary>Navigation nullable vì system messages không có Sender.</summary>
    public virtual User? Sender { get; set; }
}
