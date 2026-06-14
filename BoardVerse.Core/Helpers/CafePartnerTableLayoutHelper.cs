namespace BoardVerse.Core.Helpers
{
    public static class CafePartnerTableLayoutHelper
    {
        public static List<string> ResolveTableNames(
            int numberOfTables,
            IEnumerable<string>? requestTableNames,
            IReadOnlyList<string> existingTableNames)
        {
            var requested = requestTableNames?
                .Select(n => n.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList() ?? [];

            if (requested.Count > 0)
            {
                return SyncToCount(requested, numberOfTables);
            }

            if (existingTableNames.Count == 0 || IsDefaultLayout(existingTableNames))
            {
                return GenerateDefaultNames(numberOfTables);
            }

            return SyncToCount(existingTableNames, numberOfTables);
        }

        public static List<string> GenerateDefaultNames(int count) =>
            Enumerable.Range(1, count).Select(i => $"Bàn {i}").ToList();

        public static bool IsDefaultLayout(IReadOnlyList<string> names) =>
            names.Count > 0 &&
            names.Select((name, index) => name == $"Bàn {index + 1}").All(match => match);

        private static List<string> SyncToCount(IReadOnlyList<string> names, int count)
        {
            var result = names.Take(count).ToList();
            for (var i = result.Count; i < count; i++)
            {
                result.Add($"Bàn {i + 1}");
            }

            return result;
        }
    }
}
