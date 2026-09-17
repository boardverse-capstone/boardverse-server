using BoardVerse.Core.Enum;
using BoardVerse.Core.DTOs.Common;

namespace BoardVerse.Core.DTOs.Pos;

/// <summary>
/// Query string cho <c>GET /api/cafes/{cafeId}/pos/upcoming-reservations</c>.
/// POS staff dùng để mở dashboard "các nhóm đã đặt cọc / đang chơi tại quán".
///
/// Default behavior (khi FE không truyền):
/// <list type="bullet">
///   <item><description><c>FromDate</c> = today UTC, <c>ToDate</c> = today + 3 ngày.</description></item>
///   <item><description>
///     <c>Statuses</c> = <c>[Holding, Confirmed, CheckedIn, InProgress]</c> — chỉ active.
///     Lobby ở <c>WaitingCheckIn</c> (tất cả members Ready) cũng được include vì khi
///     lobby chuyển WaitingCheckIn, Reservation chuyển sang Confirmed
///     (xem <c>LobbyService.MarkMemberReadyAsync</c>). Staff phân biệt qua
///     <see cref="UpcomingLobbySummaryDto.IsWaitingCheckIn"/> flag trên lobby.
///   </description></item>
///   <item><description><c>IncludeCancelled</c> = false — không trả cancelled/expired/noShow.</description></item>
///   <item><description><c>SortBy</c> = <see cref="UpcomingReservationSortBy.ScheduledStartTime"/>, <c>SortDir</c> = asc.</description></item>
///   <item><description><c>PageNumber</c> = 1, <c>PageSize</c> = 20 (clamp 1..100).</description></item>
/// </list>
/// </summary>
public class UpcomingReservationsQuery
{
    /// <summary>Ngày bắt đầu filter theo <c>playDate</c> (inclusive). Null = today UTC.</summary>
    public DateOnly? FromDate { get; set; }

    /// <summary>Ngày kết thúc filter theo <c>playDate</c> (inclusive). Null = <c>FromDate + 3 ngày</c>.</summary>
    public DateOnly? ToDate { get; set; }

    /// <summary>
    /// Filter theo <c>Reservation.Status</c>. CSV qua <c>?statuses=Holding,Confirmed</c>.
    /// Null/missing = active default (Holding, Confirmed, CheckedIn, InProgress).
    /// Lobby ở <c>WaitingCheckIn</c> được cover bởi Confirmed (BR-LOBBY-READY-01) —
    /// xem <see cref="UpcomingLobbySummaryDto.IsWaitingCheckIn"/> để phân biệt.
    /// </summary>
    public List<ReservationStatus>? Statuses { get; set; }

    /// <summary>
    /// Filter theo <c>Lobby.Status</c>. CSV qua <c>?lobbyStatusFilter=Open,Viable</c>.
    /// Null = không filter (lobby status nào cũng trả, kể cả null cho legacy reservation).
    /// GAP-FIX-4: Khi set, legacy booking (r.Lobby == null) vẫn được trả vì filter chỉ áp dụng
    /// khi lobby tồn tại. Staff muốn loại trừ lobby null dùng thêm flag.
    /// </summary>
    public List<LobbyStatus>? LobbyStatusFilter { get; set; }

    /// <summary>
    /// Cho phép trả các trạng thái terminal (CancelledByPlayer/CancelledByCafe/Expired/NoShow).
    /// Khi false, chỉ active (Holding/Confirmed/CheckedIn/InProgress/WaitingCheckIn).
    /// Default false.
    /// </summary>
    public bool IncludeCancelled { get; set; } = false;

    /// <summary>Sắp xếp theo trường nào. Default <see cref="UpcomingReservationSortBy.ScheduledStartTime"/>.</summary>
    public UpcomingReservationSortBy SortBy { get; set; } = UpcomingReservationSortBy.ScheduledStartTime;

    /// <summary>Thứ tự sort. Default asc.</summary>
    public SortDirection SortDir { get; set; } = SortDirection.Asc;

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

/// <summary>Enum cho sort field của upcoming reservations dashboard.</summary>
public enum UpcomingReservationSortBy
{
    /// <summary>Sort theo <c>Reservation.ScheduledStartTime</c> (default).</summary>
    ScheduledStartTime = 0,

    /// <summary>Sort theo <c>Reservation.CreatedAt</c> (thời điểm đặt cọc).</summary>
    CreatedAt = 1,

    /// <summary>Sort theo <c>Reservation.PlayDate</c>.</summary>
    PlayDate = 2
}

/// <summary>Response paginated cho dashboard upcoming reservations.</summary>
public class UpcomingReservationsResponseDto
{
    public List<UpcomingReservationItemDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);

    /// <summary>Echo lại date range đã filter (effective).</summary>
    public DateOnly EffectiveFromDate { get; set; }
    public DateOnly EffectiveToDate { get; set; }

    /// <summary>Thống kê nhanh để staff nhìn tab filter.</summary>
    public UpcomingReservationsSummaryDto Summary { get; set; } = new();
}

/// <summary>
/// Thống kê số lượng reservation theo trạng thái.
/// Staff dùng để render chip filter phía trên dashboard.
///
/// GAP-FIX-7: Luôn trả đủ keys cố định để FE render chip filter không bị thiếu.
/// Keys không có trong kết quả query vẫn trả giá trị 0.
/// </summary>
public class UpcomingReservationsSummaryDto
{
    /// <summary>
    /// Count theo <c>Reservation.Status</c> (string → count).
    /// Luôn trả đủ keys: Holding, Confirmed, CheckedIn, InProgress, WaitingCheckIn.
    /// GAP-FIX-9: Thêm HoldingSufficient / HoldingInsufficient để phân biệt lobby đã đủ/chưa đủ người.
    /// </summary>
    public Dictionary<string, int> ByReservationStatus { get; set; } = [];

    /// <summary>
    /// Count chi tiết Holding: đã đủ người chơi hay chưa.
    /// GAP-FIX-9: Giúp staff biết trong "Holding" có bao nhiêu nhóm đã đủ vs chưa đủ minPlayers.
    /// </summary>
    public UpcomingHoldingBreakdownDto HoldingBreakdown { get; set; } = new();

    /// <summary>
    /// Count theo <c>Lobby.Status</c> (string → count). Lobby null → key "NoLobby".
    /// Luôn trả đủ fixed keys: Open, Viable, Full, WaitingCheckIn, PendingCafeApproval, NoLobby.
    /// GAP-FIX-7: Không phụ thuộc kết quả query — đảm bảo FE luôn render đủ chip.
    /// </summary>
    public Dictionary<string, int> ByLobbyStatus { get; set; } = [];

    /// <summary>Tổng số lobby cần cafe duyệt (PendingCafeApproval). Cho badge action.</summary>
    public int PendingCafeApprovals { get; set; }

    /// <summary>Tổng số reservation đã đến giờ mà chưa check-in (trong grace window).</summary>
    public int ArrivedNoCheckIn { get; set; }
}

/// <summary>
/// GAP-FIX-9: Phân tách Holding thành 2 loại.
/// </summary>
public class UpcomingHoldingBreakdownDto
{
    /// <summary>Lobby Holding và đã đạt minPlayers (CurrentPlayers &gt;= MinPlayers).</summary>
    public int SufficientMembers { get; set; }

    /// <summary>Lobby Holding và CHƯA đạt minPlayers (CurrentPlayers &lt; MinPlayers).</summary>
    public int InsufficientMembers { get; set; }
}

/// <summary>Item trong dashboard upcoming reservations.</summary>
public class UpcomingReservationItemDto
{
    public Guid ReservationId { get; set; }
    public string ReservationCode { get; set; } = string.Empty;

    public Guid CafeId { get; set; }
    public string CafeName { get; set; } = string.Empty;

    /// <summary>Thông tin host (người trả cọc). Có phone để staff liên hệ khi khách đến muộn.</summary>
    public UpcomingHostSummaryDto Host { get; set; } = new();

    /// <summary>Thông tin game.</summary>
    public UpcomingGameSummaryDto Game { get; set; } = new();

    /// <summary>Lịch trình dự kiến.</summary>
    public UpcomingScheduleSummaryDto Schedule { get; set; } = new();

    /// <summary>Thông tin lobby liên kết (null nếu legacy booking không có lobby).</summary>
    public UpcomingLobbySummaryDto? Lobby { get; set; }

    /// <summary>Trạng thái reservation + thông tin deposit + bàn.</summary>
    public UpcomingReservationStatusDto Reservation { get; set; } = new();

    /// <summary>
    /// GAP-FIX-5: Thông tin ActiveSession khi reservation đã check-in / đang chơi.
    /// Null khi chưa check-in.
    /// Staff dùng để xem nhóm đang chơi game gì, bao lâu rồi, đã kết thúc game chưa.
    /// </summary>
    public UpcomingActiveSessionSummaryDto? ActiveSession { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// GAP-FIX-5: ActiveSession summary cho POS staff dashboard.
/// Chỉ populate khi reservation đã check-in (Status = CheckedIn / InProgress).
/// </summary>
public class UpcomingActiveSessionSummaryDto
{
    public Guid SessionId { get; set; }

    /// <summary>Trạng thái phiên chơi tổng (Active/Checking/Unpaid/Paid).</summary>
    public GroupSessionStatus Status { get; set; }

    /// <summary>Trạng thái display string.</summary>
    public string StatusDisplay => Status.ToString();

    /// <summary>Tên bàn đã gán (vd: "Bàn 3", "Tầng 2 - Bàn 5").</summary>
    public string? TableName { get; set; }

    /// <summary>Tên game đang chơi (từ CafeInventoryBox.BoardGameTemplate.Name).</summary>
    public string? CurrentGameName { get; set; }

    /// <summary>Barcode hộp game đang chơi.</summary>
    public string? CurrentBarcode { get; set; }

    /// <summary>Số phút đã chơi (elapsed).</summary>
    public int ElapsedMinutes { get; set; }

    /// <summary>UTC. Thời điểm bắt đầu phiên.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>UTC. Null nếu phiên chưa kết thúc.</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>True nếu phiên đang tạm dừng.</summary>
    public bool IsPaused { get; set; }

    /// <summary>Tổng tiền giờ chơi (chưa trừ deposit).</summary>
    public decimal Subtotal { get; set; }
}

/// <summary>
/// GAP-FIX-6: Member summary cho staff.
/// Staff dùng để biết ai trong nhóm (ngoài host) để liên hệ khi cần.
/// </summary>
public class UpcomingMemberSummaryDto
{
    public Guid UserId { get; set; }

    /// <summary>
    /// Display name: Profile.LastResolvedDisplayName → FirstName+LastName → Username.
    /// Null cho GuestSlot.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Số điện thoại. Null nếu user không cập nhật.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>True nếu đây là host của lobby.</summary>
    public bool IsHost { get; set; }

    /// <summary>UTC. Thời điểm member join lobby.</summary>
    public DateTime JoinedAt { get; set; }
}

/// <summary>Host info tối thiểu cho staff dashboard.</summary>
public class UpcomingHostSummaryDto
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Số điện thoại host. Null nếu user không cập nhật.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>URL avatar host. Null nếu không có.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// GAP-FIX-11: Host đang trong cooling-off period.
    /// Staff biết để chuẩn bị tâm lý — cọc ×2 cho lobby mới, hạn chế booking xa.
    /// </summary>
    public bool IsCoolingOff { get; set; }
}

/// <summary>Game info cho staff dashboard.</summary>
public class UpcomingGameSummaryDto
{
    public Guid GameId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
}

/// <summary>Lịch trình reservation (UTC ISO 8601). FE convert local theo timezone user.</summary>
public class UpcomingScheduleSummaryDto
{
    public DateOnly PlayDate { get; set; }

    /// <summary>Start time dạng HH:mm (preferredStartTime). Null nếu chỉ có PlayDate.</summary>
    public TimeOnly? PreferredStartTime { get; set; }

    /// <summary>End time dạng HH:mm (preferredEndTime). Null nếu chỉ có PlayDate.</summary>
    public TimeOnly? PreferredEndTime { get; set; }

    /// <summary>UTC ISO 8601.</summary>
    public DateTime ScheduledStartTime { get; set; }

    /// <summary>UTC ISO 8601.</summary>
    public DateTime ScheduledEndTime { get; set; }

    /// <summary>UTC ISO 8601. Lobby.Status &gt; Open/Viable/Full/PendingCafeApproval nếu quá deadline.</summary>
    public DateTime RecruitmentDeadline { get; set; }
}

/// <summary>
/// Lobby summary cho staff. ShareCode KHÔNG được trả (BR-LOBBY-PRIVACY-02).
///
/// GAP-FIX-6: Thêm danh sách members.
/// GAP-FIX-11: Thêm IsHostCoolingOff.
/// GAP-FIX-1: Thêm IsLobbyWaitingCheckIn.
/// </summary>
public class UpcomingLobbySummaryDto
{
    public Guid LobbyId { get; set; }
    public LobbyStatus Status { get; set; }
    public string StatusDisplay => Status.ToString();

    public bool IsPrivate { get; set; }

    public int CurrentPlayers { get; set; }
    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }

    /// <summary>True nếu lobby đang chờ cafe duyệt (Status == PendingCafeApproval).</summary>
    public bool RequiresCafeApproval { get; set; }

    /// <summary>UTC ISO 8601. Null nếu lobby không cần duyệt.</summary>
    public DateTime? CafeApprovalDeadline { get; set; }

    /// <summary>
    /// GAP-FIX-1: Lobby đạt đủ người, tất cả members đã Ready, đang chờ check-in.
    /// Staff dùng để highlight "nhóm đã sẵn sàng, chuẩn bị bàn/ghế".
    /// </summary>
    public bool IsWaitingCheckIn { get; set; }

    /// <summary>
    /// GAP-FIX-11: Host của lobby đang trong cooling-off period.
    /// </summary>
    public bool IsHostCoolingOff { get; set; }

    /// <summary>
    /// GAP-FIX-6: Danh sách thành viên active trong lobby (không kể host).
    /// Host không nằm trong list này (thông tin host ở UpcomingHostSummaryDto riêng).
    /// Staff dùng để liên hệ ai trong nhóm khi cần.
    /// </summary>
    public List<UpcomingMemberSummaryDto> Members { get; set; } = [];
}

/// <summary>
/// Trạng thái reservation + deposit info cho staff.
///
/// GAP-FIX-3: Thêm TableName (tên bàn thay vì chỉ số).
/// GAP-FIX-10: DepositCurrency động thay vì hardcoded "BVC".
/// </summary>
public class UpcomingReservationStatusDto
{
    public ReservationStatus Status { get; set; }
    public string StatusDisplay => Status.ToString();

    /// <summary>BVC đã hold từ ví host. Luôn &gt; 0 cho Reservation entity (BR-DEPOSIT-02).</summary>
    public long DepositAmount { get; set; }

    /// <summary>
    /// Currency: luôn "BVC" cho Reservation entity (Reservation.DepositAmount luôn là BVC
    /// theo BR-DEPOSIT-02). "VND" không áp dụng ở đây — chỉ áp dụng cho
    /// legacy <c>BookingDeposit</c> entity (Flow B), entity đó dùng endpoint khác.
    /// </summary>
    public string DepositCurrency { get; set; } = "BVC";

    /// <summary>UTC ISO 8601. Null nếu chưa check-in.</summary>
    public DateTime? CheckedInAt { get; set; }

    /// <summary>
    /// Số bàn staff đã gán.
    /// GAP-FIX-3: Có thể null nếu chưa check-in.
    /// </summary>
    public int? TableNumber { get; set; }

    /// <summary>
    /// GAP-FIX-3: Tên bàn thực tế (vd: "Bàn 3", "Tầng 2 - Bàn 5").
    /// Null nếu chưa check-in hoặc không có CafeTable liên kết.
    /// Staff ưu tiên dùng TableName thay vì TableNumber.
    /// </summary>
    public string? TableName { get; set; }

    /// <summary>
    /// True nếu đã qua <c>ScheduledStartTime</c> mà chưa <c>CheckedIn</c>.
    /// Staff dùng để highlight "khách đến muộn — cần liên hệ host".
    /// </summary>
    public bool IsOverdueForCheckIn { get; set; }
}
