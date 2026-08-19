namespace BoardVerse.Core.DTOs.Booking;

/// <summary>
/// Request gửi phiếu vote vắng mặt cho booking (booking-payment-gaps.md #4).
/// </summary>
public class SubmitNoShowVoteRequestDto
{
    /// <summary>Booking id (cho idempotent check).</summary>
    public Guid BookingId { get; set; }
    /// <summary>Danh sách UserId bị vote vắng mặt (không bao gồm chính voter).</summary>
    public List<Guid> AbsentMemberIds { get; set; } = new();
    /// <summary>Thời điểm vote (UTC).</summary>
    public DateTime VotedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Thống kê vote cho từng thành viên trong booking.
/// </summary>
public class NoShowVoteCountDto
{
    public int AbsentVotes { get; set; }
    public int PresentVotes { get; set; }
    public int TotalMembers { get; set; }
}

/// <summary>
/// Response trả về sau khi vote thành công (booking-payment-gaps.md #4).
/// </summary>
public class NoShowVoteResponseDto
{
    public Guid BookingId { get; set; }
    public Guid VoterId { get; set; }
    public List<Guid> AbsentMemberIds { get; set; } = new();
    public Dictionary<Guid, NoShowVoteCountDto> CurrentVoteCounts { get; set; } = new();
    /// <summary>Danh sách thành viên đã bị đa số confirm vắng mặt (>= totalMembers/2 + 1).</summary>
    public List<Guid> NoShowConfirmedMembers { get; set; } = new();
    public DateTime? ProcessedAt { get; set; }
}