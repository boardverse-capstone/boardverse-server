namespace BoardVerse.Core.Constants;

/// <summary>
/// BR-LOBBY-INVITE-10: Giới hạn chống spam gửi/nhận lobby invite.
/// </summary>
public static class LobbyInviteLimits
{
    /// <summary>Tối đa số invite còn Pending mà 1 user được nhận trong 1 ngày.</summary>
    public const int MaxReceivedPerUserPerDay = 20;

    /// <summary>Tối đa số invite mà 1 user được gửi trong 1 ngày (mọi trạng thái).</summary>
    public const int MaxSentPerUserPerDay = 30;

    /// <summary>Thời gian hết hạn của 1 invite (giờ).</summary>
    public const int InviteExpiryHours = 24;
}