namespace BoardVerse.Core.Entities;

/// <summary>
/// Bảng ghi nhận cảnh báo at-risk đã gửi — chỉ gửi 1 lần mỗi lobby (BR-NEW-14).
/// </summary>
public class LobbyAtRiskWarning
{
    public Guid Id { get; set; }
    public Guid LobbyId { get; set; }
    public DateTime WarnedAt { get; set; }
    public int CurrentPlayers { get; set; }
    public int MinPlayers { get; set; }

    public virtual Lobby Lobby { get; set; } = null!;
}
