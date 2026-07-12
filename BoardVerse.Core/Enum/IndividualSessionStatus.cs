namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái phiên chơi cá nhân (Individual Session).
/// Theo boardverse-state-machine.mdc - Section 4.2.
/// Phục vụ luồng tách/ghép/ghé khách vô danh.
/// </summary>
public enum IndividualSessionStatus
{
    /// <summary>Đếm phút tính tiền lũy tiến thực tế.</summary>
    Playing = 0,

    /// <summary>Bộ nhớ đệm khi hoàn thành kiểm kê nhóm cũ. Trục thời gian vẫn chạy ngầm nhưng link nhóm bị treo. (BR-14)</summary>
    SuspendedMutation = 1,

    /// <summary>Thời gian chơi chính thức dừng. Thanh toán cá nhân HOẶC kết toán gộp.</summary>
    Finished = 2
}
