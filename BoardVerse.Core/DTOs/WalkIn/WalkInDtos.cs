using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.WalkIn;

/// <summary>
/// Request tạo WalkInBooking (§10.3 + Flow C).
/// BR-WALKIN-01: Chỉ tạo walk-in khi WalkInWindow.Status ∈ {Available, Partial}.
/// BR-WALKIN-05: OCC trên WalkInWindow.Version khi giữ ghế.
/// </summary>
public class CreateWalkInBookingRequestDto
{
    [Required]
    public Guid WalkInWindowId { get; set; }

    [Required]
    [MinLength(1)]
    public string GuestName { get; set; } = string.Empty;

    public string? GuestPhone { get; set; }

    [Required]
    [Range(1, 100)]
    public int Seats { get; set; }

    /// <summary>
    /// Idempotency key để tránh double-tap khi tạo walk-in.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Response sau khi tạo WalkInBooking thành công.
/// </summary>
public class WalkInBookingResponseDto
{
    public Guid Id { get; set; }
    public Guid WalkInWindowId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public string? GuestPhone { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int Seats { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// DTO cho WalkInWindow khi POS xem danh sách (§4.4).
/// </summary>
public class WalkInWindowDto
{
    public Guid Id { get; set; }
    public Guid? SourceReservationId { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public int TotalSeats { get; set; }
    public int AvailableSeats { get; set; }
    public int HeldSeats { get; set; }
    public int InUseSeats { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Response cho danh sách WalkInWindow của 1 cafe/date.
/// </summary>
public class WalkInWindowsResponseDto
{
    public IReadOnlyList<WalkInWindowDto> Items { get; set; } = [];
}

/// <summary>
/// Request đóng WalkInWindow (thủ công bởi POS staff).
/// </summary>
public class CloseWalkInWindowRequestDto
{
    public string? Reason { get; set; }
}
