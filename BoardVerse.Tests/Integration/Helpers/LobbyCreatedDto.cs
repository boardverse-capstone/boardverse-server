namespace BoardVerse.Tests.Integration.Helpers;

/// <summary>
/// DTO response cho lobby creation (POST /api/v1/lobbies).
/// Dùng cho các test cũ muốn parse lobby sau khi tạo.
/// </summary>
internal class LobbyCreatedDto
{
    public Guid Id { get; set; }
    public BoardVerse.Core.Enum.LobbyStatus Status { get; set; }
    public int MaxMembers { get; set; }
    public int CurrentMemberCount { get; set; }
}