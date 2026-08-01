using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Cafe;

/// <summary>
/// Mobile task #12: PATCH /api/cafes/{cafeId}/deposit-refund-policy
/// Manager cấu hình chính sách hoàn cọc cho cafe của mình.
/// BR-18: 3 policy Full/Partial/None + partial tiers.
/// </summary>
public class UpdateRefundPolicyRequestDto
{
    /// <summary>Full | Partial | None.</summary>
    [Required]
    public DepositRefundPolicy Policy { get; set; }

    /// <summary>Bắt buộc khi Policy=Partial. 1-5 tiers, sắp xếp giảm dần theo minHours.</summary>
    public List<RefundTierDto>? PartialTiers { get; set; }
}

public class RefundTierDto
{
    /// <summary>Số giờ trước giờ hẹn mà tier này áp dụng.</summary>
    [Range(0, int.MaxValue)]
    public int MinHoursBeforeScheduled { get; set; }

    /// <summary>% hoàn cọc (0-100).</summary>
    [Range(0, 100)]
    public int RefundPercent { get; set; }
}

public class RefundPolicyResponseDto
{
    public Guid CafeId { get; set; }
    public DepositRefundPolicy Policy { get; set; }
    public List<RefundTierDto>? PartialTiers { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
