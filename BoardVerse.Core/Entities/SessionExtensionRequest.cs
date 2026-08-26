using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// GAP-1 Fix: Lưu yêu cầu gia hạn thời gian chơi từ player.
/// Staff POS xem và duyệt/từ chối.
/// </summary>
public class SessionExtensionRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Mã phiên chơi được yêu cầu gia hạn.</summary>
    public Guid SessionId { get; set; }

    /// <summary>UserId của player yêu cầu.</summary>
    public Guid RequestedByUserId { get; set; }

    /// <summary>Số phút muốn gia hạn thêm.</summary>
    public int RequestedMinutes { get; set; }

    /// <summary>Ước tính chi phí thêm (VND).</summary>
    public decimal EstimatedAdditionalCostVnd { get; set; }

    public SessionExtensionRequestStatus Status { get; set; } = SessionExtensionRequestStatus.Pending;

    /// <summary>Staff xử lý yêu cầu.</summary>
    public Guid? ProcessedByUserId { get; set; }

    /// <summary>Thời điểm staff xử lý.</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Lý do từ chối (nếu có).</summary>
    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // GAP-R2-03 Fix: Số phút staff thực sự approve — có thể khác RequestedMinutes (staff thương lượng).
    // Persist để audit trail biết được staff approve khác requested bao nhiêu.
    public int? ApprovedMinutes { get; set; }

    // Navigation
    public ActiveSession? Session { get; set; }
    public User? RequestedByUser { get; set; }
}

public enum SessionExtensionRequestStatus
{
    /// <summary>Chờ staff duyệt.</summary>
    Pending = 0,

    /// <summary>Staff đã duyệt — POS đã gia hạn thời gian.</summary>
    Approved = 1,

    /// <summary>Staff từ chối.</summary>
    Rejected = 2,

    /// <summary>Hết hạn (quá lâu không staff xử lý).</summary>
    Expired = 3
}
