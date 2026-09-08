using BoardVerse.Core.DTOs.Session;

namespace BoardVerse.Core.DTOs.Pos
{
    /// <summary>
    /// Response sau khi tách thành công members khỏi session gốc.
    /// NewSessionId set khi tạo session mới; TargetSessionId set khi merge thẳng vào target.
    /// </summary>
    public class SplitSessionResponseDto
    {
        public Guid SourceSessionId { get; set; }

        /// <summary>Null nếu merge thẳng vào TargetSessionId (không tạo session trung gian).</summary>
        public Guid? NewSessionId { get; set; }

        /// <summary>Null nếu tạo session mới.</summary>
        public Guid? TargetSessionId { get; set; }

        public List<Guid> MovedMemberIds { get; set; } = new();

        /// <summary>Snapshot session gốc sau khi tách (còn A1, A2 SuspendedMutation).</summary>
        public ActiveSessionResponseDto SourceSession { get; set; } = new();

        /// <summary>Snapshot session mới. Null nếu merge thẳng vào target.</summary>
        public ActiveSessionResponseDto? NewSession { get; set; }

        public DateTime SplitAt { get; set; }
    }
}
