using System.Text.Json.Serialization;

namespace BoardVerse.Core.DTOs.Booking;

/// <summary>
/// 1 lượt chấm điểm cho 1 thành viên trong booking (mobile gap #5).
/// </summary>
public class BookingRatingItemDto
{
    /// <summary>UserId bị chấm.</summary>
    public Guid RatedUserId { get; set; }
    /// <summary>Thái độ chơi (1-5).</summary>
    public int Attitude { get; set; }
    /// <summary>Tinh thần thể thao (1-5).</summary>
    public int Sportsmanship { get; set; }
    /// <summary>Đúng giờ (1-5).</summary>
    public int Punctuality { get; set; }
    /// <summary>Nhận xét (optional, max 500 chars).</summary>
    public string? Comment { get; set; }
}

/// <summary>
/// Request gửi lượt chấm điểm cho booking (mobile gap #5).
/// </summary>
public class SubmitBookingRatingsRequestDto
{
    public Guid BookingId { get; set; }
    public List<BookingRatingItemDto> Ratings { get; set; } = new();
}

/// <summary>
/// Response sau khi submit chấm điểm.
/// </summary>
public class BookingRatingResponseDto
{
    public Guid BookingId { get; set; }
    public Guid VoterId { get; set; }
    public DateTime SubmittedAt { get; set; }
    public int RatedCount { get; set; }
}

/// <summary>
/// Trạng thái rating của voter trong booking — dùng cho mobile UI ẩn/hiện form.
/// </summary>
public class BookingRatingStatusDto
{
    public Guid BookingId { get; set; }
    public bool CanRate { get; set; }
    public DateTime? RateDeadline { get; set; }
    public bool AlreadyRated { get; set; }
    public List<Guid> RatedUserIds { get; set; } = new();
    public List<Guid> MissingMemberIds { get; set; } = new();
}

/// <summary>
/// Summary kết quả aggregate Karma sau khi staff check-out booking.
/// Mobile gap #5 (cross-rating) + Exception 2 (no-show).
/// </summary>
public class BookingRatingAggregationResultDto
{
    public Guid BookingId { get; set; }
    public DateTime AggregatedAt { get; set; }
    /// <summary>Số <see cref="BookingRating"/> rows đã aggregate (set IsAggregated = true).</summary>
    public int RatingsProcessed { get; set; }
    /// <summary>UserId → tổng delta Karma (âm = trừ, dương = cộng).</summary>
    public Dictionary<Guid, decimal> KarmaDeltaByUser { get; set; } = new();
    /// <summary>UserId bị no-show confirmed (đa số vote + deposit forfeit nếu có).</summary>
    public List<Guid> NoShowConfirmedMembers { get; set; } = new();
    /// <summary>DepositId bị mark Forfeited (RefundPolicy = None).</summary>
    public List<Guid> ForfeitedDepositIds { get; set; } = new();
    /// <summary>Tổng delta Karma toàn booking (dương = cộng, âm = trừ).</summary>
    public decimal TotalKarmaDelta { get; set; }
}