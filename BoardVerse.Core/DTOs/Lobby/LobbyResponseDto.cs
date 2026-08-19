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
    /// Response sau khi host giải tán lobby (hard delete).
    /// </summary>
    public class DissolveLobbyResponseDto
    {
        /// <summary>LobbyId đã bị xóa.</summary>
        public Guid LobbyId { get; set; }

        /// <summary>ReservationId liên kết (nếu có). Trạng thái reservation được chuyển về Holding.</summary>
        public Guid? ReservationId { get; set; }

        /// <summary>Lý do giải tán.</summary>
        public string? Reason { get; set; }

        /// <summary>Thời điểm giải tán.</summary>
        public DateTime DissolvedAt { get; set; }
    }
}