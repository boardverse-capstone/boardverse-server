namespace BoardVerse.Core.Entities;

/// <summary>
/// Phiếu vote vắng mặt của thành viên trong booking (BR: Exception 2 - No-show vote).
/// Sau khi Staff check-in với số người thực tế ít hơn booking, các thành viên có mặt
/// vote ai là người vắng mặt. Khi đa số confirm → thành viên đó bị xử lý no-show.
/// BR-22: Mỗi voter chỉ vote được 1 lần cho mỗi booking (idempotent qua (BookingId, VoterUserId)).
/// </summary>
public class BookingNoShowVote
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Booking mà phiếu vote này áp dụng.</summary>
    public Guid BookingId { get; set; }

    /// <summary>User gửi phiếu vote.</summary>
    public Guid VoterUserId { get; set; }

    /// <summary>
    /// Danh sách UserId bị vote vắng mặt, lưu dạng JSON array (vd: "[\"guid1\",\"guid2\"]").
    /// Cho phép voter vote nhiều người trong 1 lần submit, override lần vote trước.
    /// </summary>
    public string AbsentMemberIdsJson { get; set; } = "[]";

    public DateTime VotedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Thời điểm cập nhật vote gần nhất (nếu voter vote lại).</summary>
    public DateTime? UpdatedAt { get; set; }

    // === Navigation ===
    public virtual Booking Booking { get; set; } = null!;
    public virtual User Voter { get; set; } = null!;
}