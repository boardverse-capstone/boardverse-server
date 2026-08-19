using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Cafe;

/// <summary>
/// Mobile task #13: PUT /api/cafes/{cafeId}/pricing-config
/// Manager cập nhật biểu phí (BasePrice, BillingModel, TieredBlockRate...).
/// BR-04: Chỉ cho phép update khi quán đang đóng cửa (IsPricingLocked=false).
/// BR-04 tiếp: Sau khi update → trigger SignalR event CafePricingChanged cho member có booking trong tuần.
/// </summary>
public class UpdatePricingConfigRequestDto
{
    /// <summary>Mô hình tính tiền: TimeBased | FlatEntry (BR-01).</summary>
    public CafePartnerBillingModel? BillingModel { get; set; }

    /// <summary>Giá giờ đầu hoặc giá vé vào cổng tùy mô hình.</summary>
    [Range(0, double.MaxValue)]
    public decimal? BasePrice { get; set; }

    /// <summary>Giá mỗi block lũy tiến theo phút (chỉ dùng cho TimeBased).</summary>
    [Range(0, double.MaxValue)]
    public decimal? TieredBlockRate { get; set; }

    /// <summary>Số phút cho mỗi block tính tiền. Mặc định 15.</summary>
    [Range(1, 1440)]
    public int? TieredBlockMinutes { get; set; }
}

public class CafePricingConfigResponseDto
{
    public Guid CafeId { get; set; }
    public CafePartnerBillingModel BillingModel { get; set; }
    public decimal BasePrice { get; set; }
    public decimal? TieredBlockRate { get; set; }
    public int TieredBlockMinutes { get; set; }
    public bool IsPricingLocked { get; set; }
    public DateTime? OperationalProfileUpdatedAt { get; set; }

    /// <summary>Thông tin cho mobile biết có bao nhiêu booking bị ảnh hưởng.</summary>
    public int AffectedBookingsCount { get; set; }
}
