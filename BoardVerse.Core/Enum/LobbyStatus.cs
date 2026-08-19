namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái phòng chờ trực tuyến (Lobby).
/// Theo boardverse-state-machine.mdc - Section 2.
/// </summary>
public enum LobbyStatus
{
    /// <summary>Phòng hiển thị công khai. Đang tuyển người.</summary>
    Open = 0,

    /// <summary>Đã đủ người tối đa HOẶC Host khóa sớm. Sẵn sàng đặt chỗ.</summary>
    Full = 1,

    /// <summary>Không đủ người tối thiểu đến giờ hẹn. Giải tán phòng. (BR-08)</summary>
    TimeoutFailed = 2,

    /// <summary>Host chủ động giải tán phòng.</summary>
    HostCancelled = 3,

    /// <summary>Đồng bộ với GroupSession: ACTIVE. Game đã bắt đầu tại quán.</summary>
    InProgress = 4,

    /// <summary>Phiên kết thúc hoàn toàn. Ghi nhận lịch sử, biến động Elo, kích hoạt đánh giá Karma.</summary>
    Closed = 5,

    /// <summary>Cửa sổ đánh giá Karma đang mở sau khi POS thanh toán xong.</summary>
    RatingOpen = 6,

    // ===== BR-NEW-11 §6.2 / §XII =====
    /// <summary>Đang trong transaction atomic giữ BVC + ghế + game copy. Lobby chưa publish.</summary>
    PendingActivation = 10,

    /// <summary>Lobby có playDate &gt; 2 ngày — chờ cafe duyệt (BR-NEW-11).</summary>
    PendingCafeApproval = 11,

    /// <summary>Cafe từ chối duyệt lobby — hoàn 100% BVC cho host.</summary>
    RejectedByCafe = 12,

    /// <summary>Cafe không duyệt trong 24 giờ — hoàn 100% BVC cho host.</summary>
    ExpiredByCafe = 13,

    /// <summary>Đã đạt minPlayers trước recruitmentDeadline, vẫn nhận thêm đến maxPlayers.</summary>
    Viable = 14,

    /// <summary>
    /// Host chủ động giải tán lobby trước khi check-in (DELETE /api/v1/lobbies/{id}).
    /// Soft delete: row vẫn còn trong DB để phục vụ audit trail + risk score signals
    /// (BR-RISK-01 SIG-01/SIG-02, BR-NEW-10 cooling-off).
    /// </summary>
    Dissolved = 15
}
