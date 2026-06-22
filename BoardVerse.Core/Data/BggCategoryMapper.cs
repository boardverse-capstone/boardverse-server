using BoardVerse.Core.Helpers;

namespace BoardVerse.Core.Data
{
    /// <summary>
    /// Maps BGG boardgamecategory / boardgamemechanic labels to BoardVerse category slugs.
    /// Falls back to <see cref="GameCategorySeedMap"/> when BGG metadata yields no match.
    /// </summary>
    public static class BggCategoryMapper
    {
        private static readonly Dictionary<string, string[]> LabelToSlugs =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // Chiến thuật
                ["Strategy"] = ["chien-thuat"],
                ["Economic"] = ["chien-thuat"],
                ["City Building"] = ["chien-thuat"],
                ["Farming"] = ["chien-thuat"],
                ["Industry / Manufacturing"] = ["chien-thuat"],
                ["Civilization"] = ["chien-thuat"],
                ["Medieval"] = ["chien-thuat"],
                ["Renaissance"] = ["chien-thuat"],
                ["Abstract Strategy"] = ["chien-thuat"],
                ["Territory Building"] = ["chien-thuat"],
                ["Trains"] = ["chien-thuat"],
                ["Transportation"] = ["chien-thuat"],
                ["Tile Placement"] = ["chien-thuat"],
                ["Hand Management"] = ["chien-thuat"],
                ["Set Collection"] = ["chien-thuat"],
                ["Engine Building"] = ["chien-thuat"],
                ["Worker Placement"] = ["chien-thuat"],
                ["Route/Network Building"] = ["chien-thuat"],

                // Giải trí
                ["Party Game"] = ["giai-tri"],
                ["Humor"] = ["giai-tri"],
                ["Word Game"] = ["giai-tri"],
                ["Trivia"] = ["giai-tri"],
                ["Real-time"] = ["giai-tri"],
                ["Acting"] = ["giai-tri"],
                ["Music"] = ["giai-tri"],

                // Ẩn vai
                ["Deduction"] = ["an-vai"],
                ["Bluffing"] = ["an-vai"],
                ["Hidden Roles"] = ["an-vai"],
                ["Social Deduction"] = ["an-vai"],
                ["Spies/Secret Agents"] = ["an-vai"],
                ["Murder / Mystery"] = ["an-vai"],

                // Hợp tác
                ["Cooperative Game"] = ["hop-tac"],
                ["Cooperative"] = ["hop-tac"],

                // Đối kháng
                ["Negotiation"] = ["doi-khang"],
                ["Fighting"] = ["doi-khang"],
                ["Wargame"] = ["doi-khang"],
                ["Political"] = ["doi-khang"],
                ["Area Control / Area Influence"] = ["doi-khang"],
                ["Take That"] = ["doi-khang"],
                ["Direct Conflict"] = ["doi-khang"],

                // Phiêu lưu
                ["Adventure"] = ["phieu-luu"],
                ["Exploration"] = ["phieu-luu"],
                ["Fantasy"] = ["phieu-luu"],
                ["Mythology"] = ["phieu-luu"],
                ["Horror"] = ["phieu-luu"],
                ["Campaign / Battle Card Driven"] = ["phieu-luu"],
            };

        public static IReadOnlyList<string> ResolveCategorySlugs(
            IEnumerable<string>? bggCategories,
            IEnumerable<string>? bggMechanics,
            string gameName)
        {
            var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var label in bggCategories ?? [])
                AddMappedSlugs(slugs, label);

            foreach (var label in bggMechanics ?? [])
                AddMappedSlugs(slugs, label);

            if (slugs.Count == 0)
            {
                foreach (var slug in GameCategorySeedMap.GetCategorySlugs(gameName))
                    slugs.Add(slug);
            }

            return slugs.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void AddMappedSlugs(ISet<string> slugs, string? label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return;

            var key = label.Trim();
            if (LabelToSlugs.TryGetValue(key, out var mapped))
            {
                foreach (var slug in mapped)
                    slugs.Add(slug);
                return;
            }

            var normalized = VietnameseTextNormalizer.ToSearchKey(key);
            foreach (var (mapKey, mapSlugs) in LabelToSlugs)
            {
                if (VietnameseTextNormalizer.ToSearchKey(mapKey) == normalized)
                {
                    foreach (var slug in mapSlugs)
                        slugs.Add(slug);
                    return;
                }
            }
        }
    }
}
