using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Bgg
{
    public class BggComponentCatalogItemDto
    {
        public BoardGameComponentKind Kind { get; set; }
        public string NameEn { get; set; } = string.Empty;
        public string NameVi { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int TypicalDefaultQuantity { get; set; }
    }

    public class BggResolvedComponentDto
    {
        public BoardGameComponentKind Kind { get; set; }
        public string Name { get; set; } = string.Empty;
        public int DefaultQuantity { get; set; }
        public GameComponentCatalogSource Source { get; set; }
    }

    public class BggGamePreviewDto
    {
        public int BggId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public string? Description { get; set; }
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public int PlayTime { get; set; }
        public IReadOnlyList<string> Categories { get; set; } = [];
        public IReadOnlyList<string> Mechanics { get; set; } = [];
        public IReadOnlyList<BggResolvedComponentDto> Components { get; set; } = [];
        public string ComponentResolutionNote { get; set; } = string.Empty;
        public bool HasCuratedComponents { get; set; }
    }

    public class BggSearchResultItemDto
    {
        public int BggId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? YearPublished { get; set; }
    }

    public class ImportGameFromBggRequestDto
    {
        public int BggId { get; set; }

        /// <summary>Khi true, ghi đè metadata và thay thế linh kiện nếu game đã tồn tại (theo BggId hoặc tên).</summary>
        public bool OverwriteExisting { get; set; }

        /// <summary>Khi true, chỉ dùng linh kiện từ danh mục nội bộ (GameCatalog); không suy luận từ BGG.</summary>
        public bool CuratedComponentsOnly { get; set; }
    }

    public class ImportGameFromBggResponseDto
    {
        public Guid GameTemplateId { get; set; }
        public int BggId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool Created { get; set; }
        public int ComponentCount { get; set; }
        public GameComponentCatalogSource PrimaryComponentSource { get; set; }
    }
}
