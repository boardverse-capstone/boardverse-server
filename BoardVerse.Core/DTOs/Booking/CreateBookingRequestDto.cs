using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Booking;

/// <summary>
/// Request tạo Booking (walk-in hoặc từ Lobby đã lock).
/// Theo mobile gap #3: cho phép <c>LobbyId = null</c> để hỗ trợ walk-in booking
/// — player đến quán trực tiếp không cần qua luồng ghép đội online.
/// BR-22: walk-in không cần deposit.
/// Theo ERD: lobbyId, cafeId, cafeTableId, scheduledStartTime, scheduleEndTime, playerQuantity.
/// </summary>
public class CreateBookingRequestDto
{
    /// <summary>
    /// Lobby đã lock (optional). Null khi walk-in (mobile gap #3).
    /// Khi null → bỏ qua BR-07 (lobby member bound), vẫn áp dụng BR-05 (capacity + payment success).
    /// </summary>
    public Guid? LobbyId { get; set; }

    /// <summary>Quán cafe đích — bắt buộc.</summary>
    [Required]
    public Guid CafeId { get; set; }

    /// <summary>Bàn cụ thể trong quán — bắt buộc (theo ERD).</summary>
    [Required]
    public Guid CafeTableId { get; set; }

    /// <summary>Thời gian bắt đầu dự kiến (timestamp UTC).</summary>
    [Required]
    public DateTime ScheduledStartTime { get; set; }

    /// <summary>Thời gian kết thúc dự kiến (timestamp UTC).</summary>
    [Required]
    public DateTime ScheduleEndTime { get; set; }

    /// <summary>
    /// Số người chơi. Mặc định = số members trong lobby (nếu có lobby), hoặc 1 (walk-in).
    /// </summary>
    [Range(1, 50)]
    public int? PlayerQuantity { get; set; }
}