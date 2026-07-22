using BoardVerse.Core.DTOs.Lobby;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Service xử lý chat messages trong lobby.
/// </summary>
public interface ILobbyMessageService
{
    /// <summary>
    /// Member gửi tin nhắn chat (1-1000 ký tự). Phải là active member của lobby.
    /// </summary>
    Task<LobbyMessageDto> SendMessageAsync(Guid lobbyId, Guid senderId, string content);

    /// <summary>
    /// Lấy lịch sử chat (cursor pagination).
    /// </summary>
    Task<IReadOnlyList<LobbyMessageDto>> GetMessagesAsync(Guid lobbyId, DateTime? beforeCursor, int limit = 50);

    /// <summary>
    /// Hệ thống tự thêm message (vd: "Alice joined the lobby").
    /// </summary>
    Task AddSystemMessageAsync(Guid lobbyId, string content);

    /// <summary>
    /// Hệ thống tự thêm member-joined message (vd: "Alice đã tham gia").
    /// </summary>
    Task AddMemberJoinedMessageAsync(Guid lobbyId, Guid userId);
}