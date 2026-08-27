using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// BR-KARMA-01 (§4.3) + §9.6: Ghi nhận lượt chơi ngắn (short play).
/// Mỗi reservation tạo tối đa 1 record. record khi <c>playedMinutes / scheduledMinutes &lt; 0.5</c>.
/// </summary>
public class KarmaShortPlayRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Reservation mà record này áp dụng.
    /// Nullable cho host-dissolve records (BR-REFUND-02) khi lobby không link reservation.
    /// </summary>
    public Guid? ReservationId { get; set; }

    /// <summary>User bị ghi nhận short-play.</summary>
    public Guid UserId { get; set; }

    /// <summary>Số phút thực tế đã chơi (từ ActiveSessionMember.TotalMinutesPlayed).</summary>
    public int PlayedMinutes { get; set; }

    /// <summary>Số phút dự kiến từ <c>Reservation.ScheduledEndTime - ScheduledStartTime</c>.</summary>
    public int ScheduledMinutes { get; set; }

    /// <summary>
    /// playedRatio = playedMinutes / scheduledMinutes.
    /// Nếu &lt; 0.5 → bị ghi nhận short-play.
    /// </summary>
    public decimal PlayedRatio { get; set; }

    /// <summary>
    /// Điểm Karma bị trừ. BR-KARMA-01 §4.3: default -5 cho ratio &lt; 0.5.
    /// </summary>
    public int KarmaDelta { get; set; }

    /// <summary>Điểm Karma cộng vào ví sau khi record được tạo.</summary>
    public decimal KarmaPointsAdded { get; set; }

    /// <summary>Tổng điểm Karma của user sau khi record được tạo.</summary>
    public int TotalKarmaScore { get; set; }

    /// <summary>Trạng thái record: ACTIVE = đang có hiệu lực; EXPIRED = hết hạn; CLEARED = đã được xóa bởi admin.</summary>
    public KarmaRecordStatus Status { get; set; } = KarmaRecordStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // === Appeal fields (BR-KARMA-05) ===
    public bool AppealRequested { get; set; }
    public string? AppealReason { get; set; }
    public DateTime? AppealReviewedAt { get; set; }
    public Guid? AppealReviewedBy { get; set; }
    public bool? AppealApproved { get; set; }

    // === Navigation ===
    public virtual Reservation? Reservation { get; set; }
    public virtual User User { get; set; } = null!;
}
