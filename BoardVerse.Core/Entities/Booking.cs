using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Đặt chỗ trước tại quán cafe boardgame.
/// Phân biệt với BookingDeposit (đặt cọc online).
/// Booking lưu trữ thông tin đặt chỗ, trong khi BookingDeposit lưu trữ thông tin thanh toán cọc.
/// </summary>
public class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // === Cafe & Users ===
    public Guid CafeId { get; set; }
    public Guid UserId { get; set; }
    public Guid? BookingDepositId { get; set; }
    /// <summary>Link đến Lobby (nếu booking được tạo từ lobby).</summary>
    public Guid? LobbyId { get; set; }

    // === Schedule ===
    /// <summary>Ngày đặt chỗ.</summary>
    public DateTime BookingDate { get; set; }
    /// <summary>Thời gian bắt đầu dự kiến (giobatdau).</summary>
    public TimeSpan StartTime { get; set; }
    /// <summary>Thời gian kết thúc dự kiến (gioketthuc).</summary>
    public TimeSpan EndTime { get; set; }
    /// <summary>Thời gian thực tế bắt đầu (khi check-in).</summary>
    public DateTime? ActualStartTime { get; set; }
    /// <summary>Thời gian thực tế kết thúc (khi check-out).</summary>
    public DateTime? ActualEndTime { get; set; }

    // === Status ===
    public BookingStatus Status { get; set; } = BookingStatus.PendingDeposit;

    // === Slot & Table ===
    /// <summary>Tổng số ghế/người chơi trong booking.</summary>
    public int TotalSlot { get; set; }
    /// <summary>Số bàn (nếu có).</summary>
    public int? TableNumber { get; set; }
    /// <summary>Mã bàn (nếu có).</summary>
    public string? TableCode { get; set; }

    // === Notes & Reason ===
    /// <summary>Ghi chú đặc biệt ( VD: sinh nhật, team building,...).</summary>
    public string? SpecialRequest { get; set; }
    /// <summary>Lý do hủy (nếu có).</summary>
    public string? CancellationReason { get; set; }

    // === Audit ===
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // === Navigation ===
    public virtual Cafe Cafe { get; set; } = null!;
    public virtual User User { get; set; } = null!;
    public virtual BookingDeposit? BookingDeposit { get; set; }
    public virtual Lobby? Lobby { get; set; }
}
