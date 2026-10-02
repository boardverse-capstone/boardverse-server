using BoardVerse.Core.Enum;
using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Lobby
{
    /// <summary>
    /// Request để lấy toàn bộ lobby trong hệ thống (admin). Filter theo host, game, cafe, status, ngày tạo.
    /// </summary>
    public class GetAllLobbiesRequestDto
    {
        /// <summary>Optional: filter theo HostUserId.</summary>
        public Guid? HostUserId { get; set; }

        /// <summary>Optional: filter theo GameTemplateId.</summary>
        public Guid? GameTemplateId { get; set; }

        /// <summary>Optional: filter theo CafeId.</summary>
        public Guid? CafeId { get; set; }

        /// <summary>Optional: filter theo 1 hoặc nhiều LobbyStatus (enum int). Truyền nhiều giá trị bằng cách lặp query
        /// (vd: <c>?statuses=0&amp;statuses=1</c>) hoặc comma-separated (vd: <c>?statuses=0,1</c>). Null = tất cả.</summary>
        public List<int>? Statuses { get; set; }

        /// <summary>Optional: filter theo ngày tạo (inclusive). Null = không giới hạn dưới.</summary>
        public DateTime? FromDate { get; set; }

        /// <summary>Optional: filter theo ngày tạo (inclusive). Null = không giới hạn trên.</summary>
        public DateTime? ToDate { get; set; }

        /// <summary>Trang (1-indexed). Mặc định 1.</summary>
        [Range(1, int.MaxValue, ErrorMessage = "Page phải >= 1.")]
        public int Page { get; set; } = 1;

        /// <summary>Số lobby mỗi trang (1-200). Mặc định 50.</summary>
        [Range(1, 200, ErrorMessage = "PageSize phải nằm trong [1, 200].")]
        public int PageSize { get; set; } = 50;
    }

    /// <summary>
    /// Response trả về trang kết quả + metadata phân trang.
    /// </summary>
    public class GetAllLobbiesResponseDto
    {
        public List<LobbySummaryDto> Items { get; set; } = [];
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    }

    /// <summary>
    /// Lobby summary — phiên bản rút gọn của <c>LobbyResponseDto</c> cho list endpoint.
    /// Chỉ chứa field cần thiết để hiển thị danh sách (không kèm Members để tránh payload lớn).
    /// Client muốn xem chi tiết → gọi thêm <c>GET /api/v1/lobbies/{lobbyId}</c>.
    /// </summary>
    public class LobbySummaryDto
    {
        public Guid Id { get; set; }
        public Guid HostUserId { get; set; }
        public string? HostUserName { get; set; }

        public Guid GameTemplateId { get; set; }
        public string? GameName { get; set; }

        public Guid? CafeId { get; set; }
        public string? CafeName { get; set; }

        public LobbyStatus Status { get; set; }
        public string StatusDisplay => Status.ToString();

        public DateTime? ScheduledStartTime { get; set; }
        public DateTime? ScheduledEndTime { get; set; }
        public DateOnly? PlayDate { get; set; }
        public TimeOnly? PreferredStartTime { get; set; }
        public TimeOnly? PreferredEndTime { get; set; }

        public int MaxMembers { get; set; }
        public int MinPlayers { get; set; }
        public int CurrentMembers { get; set; }

        public bool IsPrivate { get; set; }
        public string? ShareCode { get; set; }

        public DateTime? ClosedAt { get; set; }
        public string? ClosedReason { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Request để lấy lịch sử lobby của user hiện tại (cả active + terminal).
    /// </summary>
    public class GetLobbyHistoryRequestDto
    {
        /// <summary>Optional: filter theo 1 hoặc nhiều LobbyStatus (enum int). Truyền nhiều giá trị bằng cách lặp query
        /// (vd: <c>?statuses=0&amp;statuses=1</c>) hoặc comma-separated (vd: <c>?statuses=0,1</c>). Null = tất cả.</summary>
        public List<int>? Statuses { get; set; }

        /// <summary>Optional: comma-separated LobbyStatus enum (vd: <c>"Open,Viable"</c>).
        /// Hỗ trợ tên enum (case-insensitive) thay vì số. Nếu truyền cả 2 tham số thì union lại.</summary>
        public string? StatusFilter { get; set; }

        /// <summary>Optional: chỉ lấy lobby mà user là host (true) hoặc member (false). Null = cả 2.</summary>
        public bool? AsHost { get; set; }

        /// <summary>Optional: filter theo ngày tạo (inclusive). Null = không giới hạn dưới.</summary>
        public DateTime? FromDate { get; set; }

        /// <summary>Optional: filter theo ngày tạo (inclusive). Null = không giới hạn trên.</summary>
        public DateTime? ToDate { get; set; }

        /// <summary>Trang (1-indexed). Mặc định 1.</summary>
        [Range(1, int.MaxValue, ErrorMessage = "Page phải >= 1.")]
        public int Page { get; set; } = 1;

        /// <summary>Số lobby mỗi trang (1-100). Mặc định 50.</summary>
        [Range(1, 100, ErrorMessage = "PageSize phải nằm trong [1, 100].")]
        public int PageSize { get; set; } = 50;
    }
}