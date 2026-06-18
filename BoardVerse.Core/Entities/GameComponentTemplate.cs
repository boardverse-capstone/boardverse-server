using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Linh kiện/cấu phần bắt buộc trong hộp game.
    /// Bảng DB: GameComponentTemplates (tương đương GameComponents trong thiết kế nghiệp vụ).
    /// </summary>
    public class GameComponentTemplate
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid GameTemplateId { get; set; }
        public string ComponentName { get; set; } = string.Empty;

        /// <summary>Loại cấu phần chuẩn (ComponentCatalog); null nếu chưa phân loại.</summary>
        public BoardGameComponentKind? ComponentKind { get; set; }

        private int _defaultQuantity = 1;
        public int DefaultQuantity
        {
            get => _defaultQuantity;
            set
            {
                if (value < 1)
                    throw new ArgumentException("DefaultQuantity must be positive");
                _defaultQuantity = value;
            }
        }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property to GameTemplate
        public virtual GameTemplate GameTemplate { get; set; } = null!;
    }
}
