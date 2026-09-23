namespace BoardVerse.Core.Enum;

/// <summary>
/// Lý do member rời lobby.
/// Dùng cho trường LobbyMember.LeftReason.
/// </summary>
public enum LeftReason
{
    /// <summary>Member tự bấm "Rời phòng" trên app.</summary>
    Voluntary = 0,

    /// <summary>Host kick member ra khỏi lobby.</summary>
    KickedByHost = 1,

    /// <summary>Bị hệ thống tự loại (lobby timeout/dissolve).</summary>
    SystemRemoved = 2,

    /// <summary>Member rời lobby cũ để nhập vào lobby mới (ghép nhóm).</summary>
    MergedIntoAnotherLobby = 3
}
