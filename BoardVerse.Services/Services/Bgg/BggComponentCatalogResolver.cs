using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Bgg;
using BoardVerse.Core.Enum;

namespace BoardVerse.Services.Services.Bgg
{
    internal static class BggComponentCatalogResolver
    {
        private const string ResolutionNote =
            "BGG XML API không cung cấp danh sách linh kiện trong hộp. " +
            "BoardVerse ưu tiên GameCatalog nội bộ (nếu có BggId); nếu không, suy luận tối thiểu từ mechanic/category BGG.";

        public static (IReadOnlyList<BggResolvedComponentDto> Components, bool HasCurated, GameComponentCatalogSource PrimarySource)
            Resolve(BggThingData thing, bool curatedOnly)
        {
            var curated = GameCatalog.GetByBggId(thing.Id);
            if (curated != null && curated.Components.Count > 0)
            {
                var components = curated.Components
                    .Select(c =>
                    {
                        var kind = ComponentCatalog.ResolveKindFromName(c.Name);
                        return new BggResolvedComponentDto
                        {
                            Kind = kind,
                            Name = c.Name,
                            DefaultQuantity = c.Quantity > 0 ? c.Quantity : 1,
                            Source = GameComponentCatalogSource.CuratedCatalog
                        };
                    })
                    .ToList();

                return (components, true, GameComponentCatalogSource.CuratedCatalog);
            }

            if (curatedOnly)
            {
                return (
                    [MinimalRulebook()],
                    false,
                    GameComponentCatalogSource.Unknown);
            }

            var inferred = InferFromBggMetadata(thing);
            return (
                inferred,
                false,
                inferred.Count > 1
                    ? GameComponentCatalogSource.InferredFromBgg
                    : GameComponentCatalogSource.Unknown);
        }

        public static string GetResolutionNote() => ResolutionNote;

        private static List<BggResolvedComponentDto> InferFromBggMetadata(BggThingData thing)
        {
            var results = new List<BggResolvedComponentDto> { MinimalRulebook() };
            var mechanics = new HashSet<string>(thing.Mechanics, StringComparer.OrdinalIgnoreCase);
            var categories = new HashSet<string>(thing.Categories, StringComparer.OrdinalIgnoreCase);

            if (HasAny(mechanics, "Dice Rolling", "Roll / Spin and Move"))
                AddUnique(results, BoardGameComponentKind.Die, "Dice", 2);

            if (HasAny(categories, "Card Game", "Collectible Components"))
                AddUnique(results, BoardGameComponentKind.CardDeck, "Card Deck", 1);

            if (HasAny(mechanics, "Hand Management", "Deck, Bag, and Pool Building", "Trick-taking", "Drafting"))
                AddUnique(results, BoardGameComponentKind.CardDeck, "Card Deck", 1);

            if (HasAny(mechanics, "Tile Placement", "Modular Board", "Grid Movement"))
                AddUnique(results, BoardGameComponentKind.Tile, "Tiles", 1);

            if (HasAny(mechanics, "Worker Placement", "Area Majority / Influence"))
                AddUnique(results, BoardGameComponentKind.Meeple, "Meeples / figures", Math.Max(1, thing.MaxPlayers));

            if (HasAny(categories, "Economic", "Negotiation") && !results.Any(r => r.Kind == BoardGameComponentKind.Token))
                AddUnique(results, BoardGameComponentKind.Token, "Resource tokens", 1);

            if (!IsLikelyCardOnly(categories, mechanics))
                AddUnique(results, BoardGameComponentKind.GameBoard, "Game Board", 1);

            return results;
        }

        private static bool IsLikelyCardOnly(ISet<string> categories, ISet<string> mechanics) =>
            categories.Contains("Card Game") &&
            !mechanics.Any(m =>
                m.Contains("Tile", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("Board", StringComparison.OrdinalIgnoreCase));

        private static BggResolvedComponentDto MinimalRulebook() =>
            new()
            {
                Kind = BoardGameComponentKind.Rulebook,
                Name = ComponentCatalog.DisplayName(BoardGameComponentKind.Rulebook),
                DefaultQuantity = 1,
                Source = GameComponentCatalogSource.InferredFromBgg
            };

        private static void AddUnique(
            List<BggResolvedComponentDto> list,
            BoardGameComponentKind kind,
            string name,
            int quantity)
        {
            if (list.Any(c => c.Kind == kind))
                return;

            list.Add(new BggResolvedComponentDto
            {
                Kind = kind,
                Name = name,
                DefaultQuantity = Math.Max(1, quantity),
                Source = GameComponentCatalogSource.InferredFromBgg
            });
        }

        private static bool HasAny(ISet<string> values, params string[] candidates) =>
            candidates.Any(c => values.Contains(c));
    }
}
