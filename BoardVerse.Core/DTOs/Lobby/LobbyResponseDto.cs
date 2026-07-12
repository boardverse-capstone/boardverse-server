using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Lobby
{
    public class LobbyResponseDto
    {
        public Guid Id { get; set; }
        public Guid HostUserId { get; set; }
        public Guid GameTemplateId { get; set; }
        public DateTime? ScheduledStartTime { get; set; }
        public int MaxMembers { get; set; }
        public int? SeatCount { get; set; }
        public Guid? ActiveSessionId { get; set; }
        public LobbyStatus Status { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        /// <summary>Tính bằng Haversine formula khi search geo. Null khi không search theo vị trí.</summary>
        public double? DistanceKm { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<LobbyMemberDto> Members { get; set; } = new();
    }
}
