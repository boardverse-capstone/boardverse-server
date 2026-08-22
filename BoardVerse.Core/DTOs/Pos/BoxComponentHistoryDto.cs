using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Pos;

/// <summary>
/// Lịch sử kiểm kê linh kiện của một hộp game (CafeInventoryBox).
/// <para>
/// Trả về các lần hộp này từng bị ghi nhận <c>MissingComponents</c> qua các phiên
/// trước, kèm chi tiết từng linh kiện thiếu, staff đã kiểm kê, member chịu trách nhiệm
/// (nếu có). Dùng để staff chủ động kiểm tra box khi giao cho khách phiên mới.
/// </para>
/// <para>
/// BR-12: mỗi lần staff submit component-check → insert audit trail.
/// </para>
/// </summary>
public class BoxComponentHistoryDto
{
    public Guid BoxId { get; set; }
    /// <summary>GAP-R4-A26: Barcode trước đây đặt tên "BoxLabel" gây nhầm lẫn với human-readable label. Đổi sang BoxBarcode cho khớp semantic.</summary>
    public string? BoxBarcode { get; set; }
    public string? Barcode { get; set; }
    public Guid GameTemplateId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public int TotalIncidents { get; set; }
    public List<BoxComponentIncidentDto> Incidents { get; set; } = [];
}

/// <summary>
/// Một lần hộp bị kiểm kê thiếu linh kiện.
/// </summary>
public class BoxComponentIncidentDto
{
    public Guid SessionGameId { get; set; }
    public Guid SessionId { get; set; }
    public DateTime CheckedAt { get; set; }
    public Guid StaffId { get; set; }
    public string? StaffName { get; set; }
    public decimal TotalPenaltyAmount { get; set; }
    public List<BoxMissingComponentDto> MissingComponents { get; set; } = [];
}

/// <summary>
/// Chi tiết một linh kiện bị thiếu trong incident.
/// </summary>
public class BoxMissingComponentDto
{
    public Guid ComponentId { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public BoardGameComponentKind? ComponentKind { get; set; }
    public int ExpectedQuantity { get; set; }
    public int ActualQuantity { get; set; }
    public int MissingQuantity => Math.Max(0, ExpectedQuantity - ActualQuantity);
    public decimal PenaltyFee { get; set; }

    /// <summary>Member chịu trách nhiệm (nếu có) — null = phạt chung phiên (BR-12).</summary>
    public Guid? ResponsibleMemberId { get; set; }
    public string? ResponsibleMemberName { get; set; }
}