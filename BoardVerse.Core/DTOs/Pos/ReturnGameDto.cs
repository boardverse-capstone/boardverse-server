namespace BoardVerse.Core.DTOs.Pos
{
    /// <summary>
    /// Request cho return-game: mảng linh kiện lỗi từ POS.
    /// POST /api/cafes/{cafeId}/pos/sessions/{sessionId}/return-game
    /// </summary>
    public class ReturnGameRequestDto
    {
        public Guid InventoryBoxId { get; set; }
        public List<DamagedComponentDto> DamagedComponents { get; set; } = [];
    }

    public class DamagedComponentDto
    {
        public Guid ComponentId { get; set; }
        /// <summary>Số lượng mất.</summary>
        public int MissingQuantity { get; set; }
        /// <summary>Số lượng hỏng nặng (cần maintenance).</summary>
        public int DamagedQuantity { get; set; }
    }

    public class ReturnGameResponseDto
    {
        public Guid SessionId { get; set; }
        public Guid InventoryBoxId { get; set; }
        public decimal SurchargeFine { get; set; }
        public bool HasDamagedComponents { get; set; }
        public string BoxMaintenanceStatus { get; set; } = string.Empty;
    }
}
