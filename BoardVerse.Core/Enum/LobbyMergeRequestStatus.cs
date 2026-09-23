namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái yêu cầu ghép nhóm (Lobby Merge Request).
/// Theo Exception Path §4 trong boardverse-business-context.mdc.
/// </summary>
public enum LobbyMergeRequestStatus
{
    /// <summary>Đã gửi yêu cầu, đang chờ nhân viên xử lý.</summary>
    Pending = 0,

    /// <summary>Nhân viên đã xác nhận ghép thành công.</summary>
    Approved = 1,

    /// <summary>Nhân viên từ chối ghép.</summary>
    Rejected = 2,

    /// <summary>Yêu cầu đã hết hạn (quá 15 phút chờ).</summary>
    Expired = 3,

    /// <summary>Yêu cầu bị hủy bởi bên gửi (player/staff).</summary>
    Cancelled = 4
}
