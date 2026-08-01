using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Booking;

/// <summary>
/// Response trả về chi tiết một Booking cho client.
/// Theo ERD: lobbyId, cafeId, cafeTableId, scheduledStartTime, scheduleEndTime,
///         status, verificationQRCode, playerQuantity.
/// Mobile app cần thêm các field (xem booking-payment-gaps.md #10):
///   gameId, gameName, depositAmount, depositDeadline, paymentRef,
///   hostId, memberIds, createdAt, updatedAt, checkedInAt, checkedOutAt.
/// </summary>
public class BookingResponseDto
{
    public Guid Id { get; set; }

    // === Relationships ===
    /// <summary>Nullable cho walk-in booking (mobile gap #3).</summary>
    public Guid? LobbyId { get; set; }
    public Guid CafeId { get; set; }
    public string? CafeName { get; set; }
    public Guid CafeTableId { get; set; }
    public string? CafeTableName { get; set; }

    // === Schedule ===
    public DateTime ScheduledStartTime { get; set; }
    public DateTime ScheduleEndTime { get; set; }

    // === Status ===
    public BookingStatus Status { get; set; }
    public string StatusText => Status.ToString();

    // === Verification QR ===
    public string? VerificationQRCode { get; set; }

    // === Player quantity ===
    public int PlayerQuantity { get; set; }

    // === Mobile gap #10: Game info ===
    public Guid? GameId { get; set; }
    public string? GameName { get; set; }

    // === Mobile gap #10: Deposit info (snapshot) ===
    /// <summary>Số tiền cọc đã đặt (0 nếu chưa có deposit).</summary>
    public decimal DepositAmount { get; set; }
    /// <summary>Deadline cọc (QrExpiresAt của deposit).</summary>
    public DateTime? DepositDeadline { get; set; }
    /// <summary>Mã giao dịch SePay (OrderId) để mobile polling/check-in.</summary>
    public string? PaymentRef { get; set; }

    // === Mobile gap #10: Host & members ===
    public Guid? HostId { get; set; }
    public List<Guid> MemberIds { get; set; } = new();

    // === Mobile gap #10: Audit trail ===
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CheckedInAt { get; set; }

    public static BookingResponseDto FromEntity(Entities.Booking entity) => new()
    {
        Id = entity.Id,
        LobbyId = entity.LobbyId,
        CafeId = entity.CafeId,
        CafeName = entity.Cafe?.Name,
        CafeTableId = entity.CafeTableId,
        CafeTableName = entity.CafeTable?.Name,
        ScheduledStartTime = entity.ScheduledStartTime,
        ScheduleEndTime = entity.ScheduleEndTime,
        Status = entity.Status,
        VerificationQRCode = entity.VerificationQRCode,
        PlayerQuantity = entity.PlayerQuantity,
        GameId = entity.Lobby?.GameTemplateId,
        GameName = entity.Lobby?.GameTemplate?.Name,
        DepositAmount = entity.BookingDeposit?.Amount ?? 0,
        DepositDeadline = entity.BookingDeposit?.QrExpiresAt,
        PaymentRef = entity.BookingDeposit?.OrderId,
        HostId = entity.Lobby?.HostUserId,
        MemberIds = entity.Lobby?.Members?
            .Where(m => m.IsActive)
            .Select(m => m.UserId)
            .ToList() ?? new List<Guid>(),
        CreatedAt = entity.BookingDeposit?.CreatedAt,
        UpdatedAt = entity.BookingDeposit?.UpdatedAt,
        CheckedInAt = entity.CheckedInAt
    };
}