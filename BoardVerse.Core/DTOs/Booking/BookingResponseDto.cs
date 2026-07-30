using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Booking;

/// <summary>
/// Response trả về chi tiết một Booking cho client.
/// </summary>
public class BookingResponseDto
{
    public Guid Id { get; set; }

    // === Cafe & Users ===
    public Guid CafeId { get; set; }
    public string? CafeName { get; set; }
    public Guid UserId { get; set; }
    public string? UserDisplayName { get; set; }
    public Guid? BookingDepositId { get; set; }
    public Guid? LobbyId { get; set; }

    // === Schedule ===
    public DateTime BookingDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }

    // === Status ===
    public BookingStatus Status { get; set; }
    public string StatusText => Status.ToString();

    // === Slot & Table ===
    public int TotalSlot { get; set; }
    public int? TableNumber { get; set; }
    public string? TableCode { get; set; }

    // === Notes & Reason ===
    public string? SpecialRequest { get; set; }
    public string? CancellationReason { get; set; }

    // === Audit ===
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // === BookingDeposit summary (nếu có) ===
    public BookingDepositSummaryDto? DepositSummary { get; set; }

    public static BookingResponseDto FromEntity(Entities.Booking entity, bool includeDeposit = false) => new()
    {
        Id = entity.Id,
        CafeId = entity.CafeId,
        CafeName = entity.Cafe?.Name,
        UserId = entity.UserId,
        UserDisplayName = entity.User?.Username,
        BookingDepositId = entity.BookingDepositId,
        LobbyId = entity.LobbyId,
        BookingDate = entity.BookingDate,
        StartTime = entity.StartTime,
        EndTime = entity.EndTime,
        ActualStartTime = entity.ActualStartTime,
        ActualEndTime = entity.ActualEndTime,
        Status = entity.Status,
        TotalSlot = entity.TotalSlot,
        TableNumber = entity.TableNumber,
        TableCode = entity.TableCode,
        SpecialRequest = entity.SpecialRequest,
        CancellationReason = entity.CancellationReason,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
        DepositSummary = includeDeposit && entity.BookingDeposit != null
            ? BookingDepositSummaryDto.FromEntity(entity.BookingDeposit)
            : null
    };
}

/// <summary>
/// Summary nhỏ gọn của BookingDeposit để embed trong BookingResponseDto.
/// </summary>
public class BookingDepositSummaryDto
{
    public Guid Id { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public BookingDepositStatus Status { get; set; }
    public string? QrUrl { get; set; }
    public string? TransferContent { get; set; }
    public DateTime? PaidAt { get; set; }

    public static BookingDepositSummaryDto FromEntity(Entities.BookingDeposit entity) => new()
    {
        Id = entity.Id,
        OrderId = entity.OrderId,
        Amount = entity.Amount,
        Status = entity.Status,
        QrUrl = entity.QrUrl,
        TransferContent = entity.TransferContent,
        PaidAt = entity.PaidAt
    };
}
