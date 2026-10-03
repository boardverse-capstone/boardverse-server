using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Admin;

/// <summary>
/// W-07: Tổng hợp giải ngân của 1 quán trong 1 ngày.
/// Dùng cho màn hình admin "Hôm nay chuyển bao nhiêu tiền cho quán nào".
/// </summary>
public class CafeDailySettlementDto
{
    public Guid CafeId { get; set; }
    public string? CafeName { get; set; }
    public Guid CafeManagerId { get; set; }
    public string? SePayBankCode { get; set; }
    public string? SePayAccountNumber { get; set; }

    /// <summary>Tổng deposit gốc (chưa trừ fee) của tất cả settlement trong ngày.</summary>
    public decimal TotalDepositAmount { get; set; }

    /// <summary>
    /// Tổng tiền thực tế cần chuyển cho quán (Succeeded + Pending + Retrying + Overridden).
    /// Không tính Failed (vì Failed chưa chuyển được, admin cần xử lý riêng).
    /// </summary>
    public decimal TotalToTransfer { get; set; }

    /// <summary>Tổng tiền đã chuyển thành công (Status = Succeeded).</summary>
    public decimal TotalTransferred { get; set; }

    /// <summary>Tổng tiền đang pending / retrying (cần theo dõi).</summary>
    public decimal TotalPending { get; set; }

    /// <summary>Tổng tiền bị Failed (cần admin xử lý).</summary>
    public decimal TotalFailed { get; set; }

    /// <summary>Tổng tiền đã Admin override.</summary>
    public decimal TotalOverridden { get; set; }

    public int TotalCount { get; set; }

    /// <summary>Thời điểm settlement mới nhất trong ngày (CreatedAt hoặc TransferredAt tùy status).</summary>
    public DateTime? LatestActivityAt { get; set; }

    /// <summary>Breakdown per status, luôn chứa đủ 5 status (kể cả count=0).</summary>
    public List<SettlementStatusBreakdownDto> ByStatus { get; set; } = new();

    /// <summary>Danh sách SettlementId trong ngày (để admin drill-down).</summary>
    public List<Guid> SettlementIds { get; set; } = new();
}
