namespace BoardVerse.Core.DTOs.Pos;

/// <summary>
/// DTO chứa thông tin booking để preview trước khi check-in.
/// AC 1.1: Hiển thị danh sách thành viên + game info TRƯỚC khi check-in.
/// </summary>
public class BookingPreviewDto
{
    /// <summary>
    /// Mã đặt chỗ (OrderId).
    /// </summary>
    public string BookingCode { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái deposit.
    /// </summary>
    public string DepositStatus { get; set; } = string.Empty;

    /// <summary>
    /// Thông tin Host (người đặt cọc).
    /// </summary>
    public BookingMemberInfoDto? Host { get; set; }

    /// <summary>
    /// Thông tin lobby liên kết (nếu có).
    /// </summary>
    public BookingLobbyInfoDto? Lobby { get; set; }

    /// <summary>
    /// Số tiền cọc đã thanh toán.
    /// </summary>
    public decimal DepositAmount { get; set; }

    /// <summary>
    /// Thời gian hẹn.
    /// </summary>
    public DateTime? ScheduledStartTime { get; set; }

    /// <summary>
    /// Số người đăng ký.
    /// </summary>
    public int RegisteredMemberCount { get; set; }

    /// <summary>
    /// Có thể check-in được không.
    /// </summary>
    public bool CanCheckIn { get; set; }

    /// <summary>
    /// Lý do không thể check-in (nếu có).
    /// </summary>
    public string? CannotCheckInReason { get; set; }
}

/// <summary>
/// Thông tin thành viên trong booking.
/// </summary>
public class BookingMemberInfoDto
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int KarmaScore { get; set; }
}

/// <summary>
/// Thông tin lobby liên kết với booking.
/// GAP-18 Fix: Thêm danh sách thành viên trong lobby.
/// </summary>
public class BookingLobbyInfoDto
{
    public Guid LobbyId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public int CurrentMemberCount { get; set; }

    /// <summary>
    /// GAP-18 Fix: Danh sách thành viên trong lobby (bao gồm host + members đã join).
    /// </summary>
    public List<BookingMemberInfoDto> Members { get; set; } = [];
}
