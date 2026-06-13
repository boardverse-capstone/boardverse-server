using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;

namespace BoardVerse.Core.Helpers
{
    public static class GameCatalogMapper
    {
        public static List<CategoryDto> MapCategories(GameTemplate? game) =>
            game?.Categories
                .Select(gc => gc.Category)
                .OrderBy(c => c.SortOrder)
                .Select(c => new CategoryDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Slug = c.Slug,
                    Description = c.Description,
                    SortOrder = c.SortOrder
                }).ToList() ?? [];

        public static List<BoardGameComponentDto> MapComponents(IEnumerable<GameComponentTemplate>? components) =>
            (components ?? [])
                .OrderBy(c => c.ComponentName)
                .Select(c => new BoardGameComponentDto
                {
                    Id = c.Id,
                    ComponentName = c.ComponentName,
                    DefaultQuantity = c.DefaultQuantity
                }).ToList();
    }
}
