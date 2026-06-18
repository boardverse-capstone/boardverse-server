using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Data
{
    public record ComponentCatalogDefinition(
        BoardGameComponentKind Kind,
        string NameEn,
        string NameVi,
        string Description,
        int TypicalDefaultQuantity);

    /// <summary>
    /// Taxonomy cấu phần mẫu — BGG không trả về nội dung hộp; BoardVerse dùng catalog này + overlay GameCatalog.
    /// </summary>
    public static class ComponentCatalog
    {
        private static readonly IReadOnlyList<ComponentCatalogDefinition> AllDefinitions =
        [
            Def(BoardGameComponentKind.GameBoard, "Game Board", "Bàn chơi", "Main play surface.", 1),
            Def(BoardGameComponentKind.Rulebook, "Rulebook", "Sách luật", "Rules reference.", 1),
            Def(BoardGameComponentKind.CardDeck, "Card Deck", "Bộ bài", "Standard or custom cards.", 1),
            Def(BoardGameComponentKind.PlayerBoard, "Player Board", "Bàn người chơi", "Individual player mat or board.", 1),
            Def(BoardGameComponentKind.Tile, "Tile", "Thẻ ô đất / tile", "Modular map or placement tiles.", 1),
            Def(BoardGameComponentKind.Token, "Token", "Token / thẻ tài nguyên", "Resource chips, cubes, or counters.", 1),
            Def(BoardGameComponentKind.Meeple, "Meeple", "Meeple / quân đại diện", "Wooden or plastic figures.", 1),
            Def(BoardGameComponentKind.Pawn, "Pawn", "Quân cờ / pawn", "Player markers moved on the board.", 1),
            Def(BoardGameComponentKind.Die, "Die", "Xúc xắc", "Standard six-sided or custom dice.", 1),
            Def(BoardGameComponentKind.ScoreTrack, "Score Track", "Thang điểm", "Scoreboard or track.", 1),
            Def(BoardGameComponentKind.Marker, "Marker", "Marker", "Turn order, infection rate, etc.", 1),
            Def(BoardGameComponentKind.Money, "Money", "Tiền / thẻ tiền", "Bills, coins, or money cards.", 1),
            Def(BoardGameComponentKind.Timer, "Timer", "Đồng hồ / timer", "Sand timer or clock component.", 1),
            Def(BoardGameComponentKind.Other, "Other", "Khác", "Component not in standard taxonomy.", 1)
        ];

        private static readonly Dictionary<BoardGameComponentKind, ComponentCatalogDefinition> ByKind =
            AllDefinitions.ToDictionary(d => d.Kind);

        public static IReadOnlyList<ComponentCatalogDefinition> GetAll() => AllDefinitions;

        public static ComponentCatalogDefinition Get(BoardGameComponentKind kind) =>
            ByKind.TryGetValue(kind, out var def) ? def : ByKind[BoardGameComponentKind.Other];

        public static string DisplayName(BoardGameComponentKind kind, bool vietnamese = false)
        {
            var def = Get(kind);
            return vietnamese ? def.NameVi : def.NameEn;
        }

        /// <summary>Map tên linh kiện tự do (GameCatalog) sang loại chuẩn.</summary>
        public static BoardGameComponentKind ResolveKindFromName(string componentName)
        {
            if (string.IsNullOrWhiteSpace(componentName))
                return BoardGameComponentKind.Other;

            var n = componentName.ToLowerInvariant();

            if (n.Contains("dice") || n.Contains("die") || n.Contains("xúc xắc") || n.Contains("xuc xac"))
                return BoardGameComponentKind.Die;
            if (n.Contains("card") || n.Contains("bài") || n.Contains("deck"))
                return BoardGameComponentKind.CardDeck;
            if (n.Contains("meeple"))
                return BoardGameComponentKind.Meeple;
            if (n.Contains("tile") || n.Contains("hexagon"))
                return BoardGameComponentKind.Tile;
            if (n.Contains("board") && !n.Contains("score"))
                return n.Contains("player") ? BoardGameComponentKind.PlayerBoard : BoardGameComponentKind.GameBoard;
            if (n.Contains("token") || n.Contains("gem") || n.Contains("cube"))
                return BoardGameComponentKind.Token;
            if (n.Contains("pawn") || n.Contains("marker") && n.Contains("player"))
                return BoardGameComponentKind.Pawn;
            if (n.Contains("score") || n.Contains("track"))
                return BoardGameComponentKind.ScoreTrack;
            if (n.Contains("money") || n.Contains("bill"))
                return BoardGameComponentKind.Money;
            if (n.Contains("rule"))
                return BoardGameComponentKind.Rulebook;

            return BoardGameComponentKind.Other;
        }

        private static ComponentCatalogDefinition Def(
            BoardGameComponentKind kind,
            string nameEn,
            string nameVi,
            string description,
            int typicalQty) =>
            new(kind, nameEn, nameVi, description, typicalQty);
    }
}
