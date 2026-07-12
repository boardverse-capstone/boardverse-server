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
    RatingOpen = 6
}
