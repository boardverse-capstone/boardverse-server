namespace BoardVerse.Core.DTOs.Admin;

/// <summary>
/// W-07: Tổng hợp giải ngân theo NGÀY (mặc định hôm nay theo giờ VN).
/// Endpoint: GET /api/v1/admin/settlements/daily-summary?date=YYYY-MM-DD
/// </summary>
public class SettlementDailySummaryDto
{
    /// <summary>Ngày áp dụng (định dạng yyyy-MM-dd, theo giờ VN).</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Múi giờ áp dụng (vd "Asia/Ho_Chi_Minh").</summary>
    public string Timezone { get; set; } = "Asia/Ho_Chi_Minh";

    /// <summary>UTC range (inclusive start, exclusive end) mà hệ thống đã query.</summary>
    public DateTime QueryStartUtc { get; set; }
    public DateTime QueryEndUtc { get; set; }

    /// <summary>Tổng số quán có settlement trong ngày.</summary>
    public int CafeCount { get; set; }

    /// <summary>Tổng số settlement (mọi status) trong ngày.</summary>
    public int TotalSettlementCount { get; set; }

    /// <summary>Tổng tiền cần chuyển cho tất cả quán (Succeeded + Pending + Retrying + Overridden).</summary>
    public decimal GrandTotalToTransfer { get; set; }

    /// <summary>Tổng tiền đã chuyển thành công trong ngày.</summary>
    public decimal GrandTotalTransferred { get; set; }

    /// <summary>Tổng tiền deposit gốc trong ngày.</summary>
    public decimal GrandTotalDeposit { get; set; }

    /// <summary>Tổng tiền bị Failed (cần admin xử lý retry/override).</summary>
    public decimal GrandTotalFailed { get; set; }

    /// <summary>Chi tiết từng quán, sắp xếp theo TotalToTransfer DESC (quán lớn lên đầu).</summary>
    public List<CafeDailySettlementDto> Cafes { get; set; } = new();
}
