using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Session
{
    /// <summary>
    /// Request gán thêm game vào phiên chơi.
    /// Exception 6: Nhóm tự ý lấy thêm game mà không báo nhân viên.
    /// </summary>
    public class AttachGameRequestDto
    {
        [Required]
        public string GameBarcode { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request thêm thành viên đến muộn vào phiên.
    /// Exception 8: Thêm 2 người bạn đến muộn vào nhóm đang chơi.
    /// </summary>
    public class AddLateMemberRequestDto
    {
        [Required]
        public List<Guid> MemberUserIds { get; set; } = new();
    }

    /// <summary>
    /// Request ghi nhận hao hụt linh kiện trước phiên chơi.
    /// Exception 7: Nhân viên ca chiều phát hiện game bị thiếu từ ca sáng.
    /// </summary>
    public class RecordInventoryLossRequestDto
    {
        [Required]
        public Guid CafeInventoryBoxId { get; set; }

        /// <summary>Danh sách linh kiện bị thiếu/hỏng.</summary>
        public List<ComponentLossItemDto> LostComponents { get; set; } = new();

        /// <summary>Ghi chú.</summary>
        public string? Notes { get; set; }
    }

    public class ComponentLossItemDto
    {
        [Required]
        public Guid ComponentId { get; set; }

        [Required]
        public string ComponentName { get; set; } = string.Empty;

        public bool IsDamaged { get; set; }
    }

    /// <summary>
    /// Response gợi ý quán thay thế khi hết chỗ.
    /// Exception 1: Phòng đầy nhưng quán hết chỗ.
    /// </summary>
    public class AlternativeCafesResponseDto
    {
        public List<AlternativeCafeDto> Cafes { get; set; } = new();
    }

    public class AlternativeCafeDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double DistanceKm { get; set; }
        public int AvailableSeats { get; set; }
        public bool HasRequestedGame { get; set; }
    }
}
