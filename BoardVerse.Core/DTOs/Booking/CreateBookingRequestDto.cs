using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Booking;

/// <summary>
/// Request tạo Booking từ Lobby đã lock.
/// Theo ERD: lobbyId, cafeId, cafeTableId, scheduledStartTime, scheduleEndTime, playerQuantity.
/// </summary>
public class CreateBookingRequestDto
{
    /// <summary>
    /// Lobby đã lock — bắt buộc. Booking sẽ được link với lobby này.
    /// </summary>
    [Required]
    public Guid LobbyId { get; set; }

    /// <summary>
    /// Quán cafe đích — bắt buộc.
    /// </summary>
    [Required]
    public Guid CafeId { get; set; }

    /// <summary>
    /// Bàn cụ thể trong quán — bắt buộc (theo ERD).
    /// </summary>
    [Required]
    public Guid CafeTableId { get; set; }

    /// <summary>
    /// Thời gian bắt đầu dự kiến (timestamp).
    /// </summary>
    [Required]
    public DateTime ScheduledStartTime { get; set; }

    /// <summary>
    /// Thời gian kết thúc dự kiến (timestamp).
    /// </summary>
    [Required]
    public DateTime ScheduleEndTime { get; set; }

    /// <summary>
    /// Số người chơi. Mặc định = số members trong lobby.
    /// </summary>
    [Range(1, 50)]
    public int? PlayerQuantity { get; set; }
}
