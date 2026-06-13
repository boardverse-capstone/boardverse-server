namespace BoardVerse.Core.Data
{
    /// <summary>
    /// Gợi ý thể loại và alias tìm kiếm khi seed/upsert game từ catalog nội bộ.
    /// </summary>
    public static class GameCategorySeedMap
    {
        public static IReadOnlyList<string> GetCategorySlugs(string name) =>
            Normalize(name) switch
            {
                "catan" => ["chien-thuat", "doi-khang"],
                "monopoly" => ["giai-tri", "doi-khang"],
                "uno" => ["giai-tri"],
                "splendor" => ["chien-thuat"],
                "werewolf ultimate" => ["an-vai", "giai-tri"],
                "the resistance: avalon" => ["an-vai", "chien-thuat"],
                "codenames" => ["an-vai", "giai-tri"],
                "pandemic" => ["hop-tac", "chien-thuat"],
                "ticket to ride" => ["chien-thuat", "giai-tri"],
                "carcassonne" => ["chien-thuat"],
                "wingspan" => ["chien-thuat"],
                "azul" => ["chien-thuat"],
                "terraforming mars" => ["chien-thuat"],
                "gloomhaven" => ["phieu-luu", "hop-tac", "chien-thuat"],
                _ => GuessByName(name)
            };

        public static string? GetSearchAliases(string name) =>
            Normalize(name) switch
            {
                "catan" => "Catán, Settlers of Catan, Catan",
                "werewolf ultimate" => "Ma Sói, Ma Soi, Werewolf, Ma Sói Ultimate",
                "the resistance: avalon" => "Avalon, Kháng Chiến, The Resistance",
                "codenames" => "Codenames, Mật Danh",
                "ticket to ride" => "Ticket to Ride, Tàu Hỏa Miền Tây",
                "pandemic" => "Pandemic, Đại Dịch",
                "wingspan" => "Wingspan, Chim, Đập Cánh",
                "azul" => "Azul",
                "terraforming mars" => "Terraforming Mars, Sao Hỏa",
                "gloomhaven" => "Gloomhaven",
                "carcassonne" => "Carcassonne",
                "monopoly" => "Cờ Tỷ Phú",
                "uno" => "Uno",
                "splendor" => "Splendor, Ngọc Trai",
                _ => null
            };

        private static string Normalize(string name) => name.Trim().ToLowerInvariant();

        private static IReadOnlyList<string> GuessByName(string name)
        {
            var lower = name.ToLowerInvariant();
            if (lower.Contains("werewolf") || lower.Contains("ma sói"))
                return ["an-vai", "giai-tri"];
            if (lower.Contains("pandemic"))
                return ["hop-tac", "chien-thuat"];
            if (lower.Contains("codenames"))
                return ["an-vai", "giai-tri"];
            return [];
        }
    }
}
