using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Payment;

/// <summary>
/// DTO trả về chi tiết một BookingDeposit cho client.
/// Tách khỏi Entity để: 
/// - Tránh lộ navigation properties không cần thiết (MasterAccount, Cafe, User, ActiveSession).
/// - Dễ thêm/sửa field cho mobile mà không ảnh hưởng entity.
/// - Ổn định contract API khi schema database thay đổi.
/// </summary>
public class BookingDepositResponseDto
{
    public Guid Id { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public Guid? ActiveSessionId { get; set; }
    public Guid UserId { get; set; }
    public Guid CafeId { get; set; }
    public Guid CafeManagerId { get; set; }
    public decimal Amount { get; set; }
    public decimal? RefundedAmount { get; set; }
    public DepositRefundPolicy RefundPolicy { get; set; }
    public BookingDepositStatus Status { get; set; }
    public string? TransferContent { get; set; }
    public string? SePayTransactionId { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public DateTime? RefundedAt { get; set; }
    public DateTime? ForfeitedAt { get; set; }
    public string? QrUrl { get; set; }
    public DateTime? QrExpiresAt { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public static BookingDepositResponseDto FromEntity(BookingDeposit entity, decimal? refundedAmount = null) => new()
    {
        Id = entity.Id,
        OrderId = entity.OrderId,
        ActiveSessionId = entity.ActiveSessionId,
        UserId = entity.UserId,
        CafeId = entity.CafeId,
        CafeManagerId = entity.CafeManagerId,
        Amount = entity.Amount,
        RefundedAmount = refundedAmount,
        RefundPolicy = entity.RefundPolicy,
        Status = entity.Status,
        TransferContent = entity.TransferContent,
        SePayTransactionId = entity.SePayTransactionId,
        PaidAt = entity.PaidAt,
        ReleasedAt = entity.ReleasedAt,
        RefundedAt = entity.RefundedAt,
        ForfeitedAt = entity.ForfeitedAt,
        QrUrl = entity.QrUrl,
        QrExpiresAt = entity.QrExpiresAt,
        ScheduledAt = entity.ScheduledAt,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };
}
