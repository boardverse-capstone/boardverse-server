using BoardVerse.Core.Entities;
using BoardVerse.Core.Helpers;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Helpers
{
    public static class GameSearchHelper
    {
        public static IQueryable<GameTemplate> ApplyFuzzyNameSearch(
            IQueryable<GameTemplate> query,
            string searchTerm)
        {
            var trimmed = searchTerm.Trim();
            var searchKey = VietnameseTextNormalizer.ToSearchKey(trimmed);

            return query.Where(g =>
                EF.Functions.ILike(g.NameSearchKey, $"%{searchKey}%") ||
                EF.Functions.ILike(g.Name, $"%{trimmed}%") ||
                EF.Functions.ILike(g.SearchAliasesKey, $"%{searchKey}%"));
        }
    }
}
