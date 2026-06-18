namespace BoardVerse.Core.Enum
{
    public enum GameComponentCatalogSource
    {
        /// <summary>Danh mục nội bộ BoardVerse (GameCatalog) gắn BggId.</summary>
        CuratedCatalog = 0,

        /// <summary>Suy luận từ mechanic/category BGG (BGG không cung cấp danh sách hộp).</summary>
        InferredFromBgg = 1,

        /// <summary>Chưa xác định — cần Manager bổ sung thủ công.</summary>
        Unknown = 2
    }
}
