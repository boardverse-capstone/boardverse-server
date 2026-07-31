using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Booking;

/// <summary>
/// Response trả về chi tiết một Booking cho client.
/// Theo ERD: lobbyId, cafeId, cafeTableId, scheduledStartTime, scheduleEndTime,
///         status, verificationQRCode, playerQuantity.
/// </summary>
public class BookingResponseDto
{
    public Guid Id { get; set; }

    // === Relationships ===
    public Guid LobbyId { get; set; }
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
        PlayerQuantity = entity.PlayerQuantity
    };
}
