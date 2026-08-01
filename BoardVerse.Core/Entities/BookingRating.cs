namespace BoardVerse.Core.Entities;

/// <summary>
/// Chấm điểm chéo giữa các thành viên trong một booking (BR: cross-rating sau check-out).
/// Mỗi <c>VoterUserId</c> có đúng 1 row cho mỗi <c>BookingId</c>, chứa JSON array các lượt
/// đánh giá từng <c>RatedUserId</c>. Aggregate sau 24h từ CheckedInAt (hoặc sớm hơn nếu Staff bấm kết thúc)
/// để cập nhật Karma của các user bị chấm.
/// </summary>
public class BookingRating
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Booking mà lượt chấm điểm này áp dụng.</summary>
    public Guid BookingId { get; set; }

    /// <summary>User gửi lượt chấm điểm.</summary>
    public Guid VoterUserId { get; set; }

    /// <summary>
    /// JSON array các rating theo format:
    /// <c>[{"ratedUserId":"guid","attitude":5,"sportsmanship":4,"punctuality":5,"comment":"..."}]</c>
    /// Lưu JSON để tránh tạo 1 row per rated user (giảm số round-trip).
    /// </summary>
    public string RatingsJson { get; set; } = "[]";

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True sau khi backend aggregate thành công vào Karma (khi Staff check-out hoặc sau 24h).</summary>
    public bool IsAggregated { get; set; }

    public DateTime? AggregatedAt { get; set; }

    // === Navigation ===
    public virtual Booking Booking { get; set; } = null!;
    public virtual User Voter { get; set; } = null!;
}