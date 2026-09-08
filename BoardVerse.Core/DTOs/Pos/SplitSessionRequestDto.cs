namespace BoardVerse.Core.DTOs.Pos
{
    /// <summary>
    /// Request tách một nhóm member ra khỏi session đang Checking
    /// để tiếp tục chơi (session mới) hoặc merge thẳng vào session target.
    /// Exception 4 (doc time-slot-fixed-end-design.md): A3, A4 ở lại sau khi A1, A2 về sớm.
    /// </summary>
    public class SplitSessionRequestDto
    {
        /// <summary>
        /// Members muốn tách ra khỏi session gốc. Phải đang ở trạng thái Playing
        /// (BR-12 đã kiểm kê xong cho cả nhóm). Members đang SuspendedMutation
        /// không thể tách — họ phải checkout tại session gốc.
        /// </summary>
        public List<Guid> MemberIds { get; set; } = new();

        /// <summary>
        /// Optional. Nếu null: tạo session mới (members tiếp tục chơi với box hiện tại).
        /// Nếu set: merge ngay vào session target đang Active — bỏ qua việc tạo session trung gian.
        /// Target phải: Active, cùng cafeId, cùng GameTemplateId, còn chỗ ngồi.
        /// </summary>
        public Guid? TargetSessionId { get; set; }
    }
}
