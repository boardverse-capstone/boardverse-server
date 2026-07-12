using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Session
{
    /// <summary>
    /// Request để ghép thành viên vào phiên chơi của nhóm khác.
    /// Exception 4: A3 nhảy từ nhóm A sang nhóm B.
    /// - A3 hoàn thành kiểm kê ở nhóm cũ (SUSPENDED_MUTATION)
    /// - Nhân viên quét mã A3 → ghép vào nhóm B
    /// - A3 không mất thời gian, tổng thời gian tính liên tục từ lúc ban đầu
    /// </summary>
    public class MergeSessionRequestDto
    {
        /// <summary>Mã thành viên đang ở trạng thái SUSPENDED_MUTATION cần ghép vào nhóm mới.</summary>
        [Required]
        public Guid MemberId { get; set; }

        /// <summary>Mã phiên chơi của nhóm mới (target session).</summary>
        [Required]
        public Guid TargetSessionId { get; set; }
    }

    /// <summary>
    /// Response sau khi ghép thành viên vào nhóm mới.
    /// </summary>
    public class MergeSessionResponseDto
    {
        public Guid MemberId { get; set; }
        public Guid SourceSessionId { get; set; }
        public Guid TargetSessionId { get; set; }
        public DateTime MergedAt { get; set; }
        public ActiveSessionResponseDto TargetSession { get; set; } = null!;
    }
}
