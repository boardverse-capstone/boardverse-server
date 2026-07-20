using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Game thực tế được sử dụng trong một ActiveSession.
    /// Exception 6: khách có thể lấy thêm game không qua nhân viên.
    /// BR-12: Component Checklist - kiểm kê trung gian bắt buộc.
    /// </summary>
    public class ActiveSessionGame
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ActiveSessionId { get; set; }
        public Guid CafeInventoryBoxId { get; set; }
        public Guid GameTemplateId { get; set; }

        /// <summary>Thời điểm game được gán vào phiên.</summary>
        public DateTime AttachedAt { get; set; } = DateTime.UtcNow;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // === BR-12: Component Checklist ===
        /// <summary>Trạng thái kiểm kê linh kiện.</summary>
        public ComponentCheckStatus CheckStatus { get; set; } = ComponentCheckStatus.NotChecked;

        /// <summary>Thời điểm kiểm kê xong.</summary>
        public DateTime? CheckedAt { get; set; }

        /// <summary>Staff đã thực hiện kiểm kê linh kiện.</summary>
        public Guid? CheckedByStaffId { get; set; }

        /// <summary>Tổng tiền phạt từ kiểm kê (số âm = thiếu).</summary>
        public decimal TotalPenaltyAmount { get; set; }

        public virtual ActiveSession ActiveSession { get; set; } = null!;
        public virtual CafeInventoryBox CafeInventoryBox { get; set; } = null!;
        public virtual GameTemplate GameTemplate { get; set; } = null!;
        public virtual User? CheckedByStaff { get; set; }
    }
}
