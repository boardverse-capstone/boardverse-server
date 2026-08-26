using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Pos;

/// <summary>
/// GAP-NEW-1 Fix: DTO cho POS staff xem danh sách yêu cầu gia hạn đang chờ.
/// GET /api/cafes/{cafeId}/pos/extension-requests/pending
/// </summary>
public class PendingExtensionRequestsResponseDto
{
    public int TotalCount { get; set; }
    public List<PendingExtensionRequestDto> Requests { get; set; } = new();
}

/// <summary>
/// Thông tin một yêu cầu gia hạn đang chờ.
/// </summary>
public class PendingExtensionRequestDto
{
    public Guid RequestId { get; set; }
    public Guid SessionId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public Guid PlayerId { get; set; }
    public int RequestedMinutes { get; set; }
    public decimal EstimatedAdditionalCostVnd { get; set; }
    public DateTime RequestedAt { get; set; }
    public int MinutesUntilExpiry { get; set; } // tính từ now đến CreatedAt + threshold
}

/// <summary>
/// Request body để POS staff duyệt yêu cầu gia hạn.
/// POST /api/cafes/{cafeId}/pos/extension-requests/{requestId}/approve
/// </summary>
public class ApproveExtensionRequestDto
{
    /// <summary>Số phút gia hạn thực tế (có thể khác với requestedMinutes nếu staff điều chỉnh).</summary>
    public int ApprovedMinutes { get; set; }
}

/// <summary>
/// Request body để POS staff từ chối yêu cầu gia hạn.
/// POST /api/cafes/{cafeId}/pos/extension-requests/{requestId}/reject
/// </summary>
public class RejectExtensionRequestDto
{
    /// <summary>Lý do từ chối (tùy chọn, tối thiểu 10 ký tự).</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Response sau khi duyệt/từ chối yêu cầu gia hạn.
/// </summary>
public class ExtensionRequestProcessedDto
{
    public Guid RequestId { get; set; }
    public string Status { get; set; } = string.Empty; // "Approved" hoặc "Rejected"
    public int ApprovedMinutes { get; set; } // 0 nếu rejected
    public DateTime ProcessedAt { get; set; }
    public string Message { get; set; } = string.Empty;

    // GAP-11 Fix: Thời điểm kết thúc mới sau khi extension được approve
    public DateTime? NewEndTime { get; set; }
}
