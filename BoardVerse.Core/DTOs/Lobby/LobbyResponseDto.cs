using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Lobby
{
    public class LobbyResponseDto
    {
        public Guid Id { get; set; }
        public Guid HostUserId { get; set; }
        public Guid GameTemplateId { get; set; }
        public string? GameName { get; set; }

        public Guid? CafeId { get; set; }
        public Guid? BookingId { get; set; }

        public DateTime? ScheduledStartTime { get; set; }
        public int MaxMembers { get; set; }
        public int MinPlayers { get; set; }
        public int? SeatCount { get; set; }
        public Guid? ActiveSessionId { get; set; }
        public LobbyStatus Status { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        /// <summary>Tính bằng Haversine formula khi search geo. Null khi không search theo vị trí.</summary>
        public double? DistanceKm { get; set; }

        public bool IsPrivate { get; set; }
        public string ShareCode { get; set; } = string.Empty;

        public string? Description { get; set; }
        public string? CoverImageUrl { get; set; }

        public int CancellationLeadTimeMinutes { get; set; }

        /// <summary>
        /// BR-10: Điểm Karma tối thiểu yêu cầu để join.
        /// Null = không yêu cầu tối thiểu. Member có thể join nếu <c>Member.KarmaPoints &gt;= MinKarmaScore</c>.
        /// Client hiển thị label "Yêu cầu Karma ≥ X" khi có giá trị.
        /// </summary>
        public int? MinKarmaScore { get; set; }

        public DateTime? ClosedAt { get; set; }
        public string? ClosedReason { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public List<LobbyMemberDto> Members { get; set; } = new();
    }

/// <summary>
/// Response sau khi host giải tán lobby (soft delete — row vẫn còn để phục vụ audit + risk signals).
///
/// GAP #1 + #6 fix (2026-08-16): host trước đây không được hoàn BVC và inventory không được giải phóng.
/// BR-REFUND-02/03: refund matrix theo thời điểm hủy (grace 15p / 24h / 6h / dưới 6h).
/// </summary>
public class DissolveLobbyResponseDto
{
    /// <summary>LobbyId đã chuyển sang trạng thái Dissolved (terminal, soft-deleted).</summary>
    public Guid LobbyId { get; set; }

    /// <summary>ReservationId liên kết (nếu có). Trạng thái reservation được chuyển sang CancelledByPlayer.</summary>
    public Guid? ReservationId { get; set; }

    /// <summary>Lý do giải tán.</summary>
    public string? Reason { get; set; }

    /// <summary>Thời điểm giải tán.</summary>
    public DateTime DissolvedAt { get; set; }

    /// <summary>Số BVC hoàn trả lại ví <c>availableBalance</c> của host (BR-REFUND-02/03).</summary>
    public long RefundBvc { get; set; }

    /// <summary>Số BVC bị forfeit (giữ lại thuộc doanh thu quán khi hủy dưới 6h hoặc quá grace).</summary>
    public long ForfeitBvc { get; set; }

    /// <summary>Tên policy áp dụng (grace-15p-no-member, cancel-24h, cancel-6h, cancel-under-6h).</summary>
    public string? RefundPolicyApplied { get; set; }
}
}