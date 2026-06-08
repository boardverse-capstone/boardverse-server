namespace BoardVerse.Core.Data
{
    public record KnownBggGameEntry(
        int BggGameId,
        string Name,
        string Description,
        int MinPlayers,
        int MaxPlayers,
        int PlayTime,
        IReadOnlyList<(string Name, int Quantity)> Components);

    /// <summary>
    /// Curated master data for popular board games.
    /// Used for component checklists when BGG XML API is unavailable or lacks structured component data.
    /// </summary>
    public static class BggKnownGameCatalog
    {
        public static readonly IReadOnlyList<int> PopularGameIds =
        [
            13,      // Catan
            9209,    // Ticket to Ride
            822,     // Carcassonne
            30549,   // Pandemic
            266192,  // Wingspan
            230802,  // Azul
            148228,  // Splendor
            167791,  // Terraforming Mars
            174430,  // Gloomhaven
            178900   // Codenames
        ];

        private static readonly Dictionary<int, KnownBggGameEntry> Games = BuildCatalog();

        public static KnownBggGameEntry? GetById(int bggGameId) =>
            Games.TryGetValue(bggGameId, out var entry) ? entry : null;

        public static IReadOnlyList<KnownBggGameEntry> GetAll() => Games.Values.ToList();

        public static IReadOnlyList<(string Name, int Quantity)> GetComponents(int bggGameId) =>
            GetById(bggGameId)?.Components ?? [];

        private static Dictionary<int, KnownBggGameEntry> BuildCatalog()
        {
            var entries = new[]
            {
                Entry(13, "Catan",
                    "A strategy board game where players build settlements, roads, and cities by gathering and trading resources.",
                    3, 4, 60,
                    ("Wood Hexagon Tiles", 4),
                    ("Brick Hexagon Tiles", 3),
                    ("Sheep Resource Cards", 19),
                    ("Wheat Resource Cards", 19),
                    ("Ore Resource Cards", 19),
                    ("Settlement Pieces", 20),
                    ("Road Pieces", 30),
                    ("City Pieces", 16),
                    ("Dice", 2)),

                Entry(9209, "Ticket to Ride",
                    "A railway-themed card game where players collect train cards to claim routes across a map.",
                    2, 5, 60,
                    ("Game Board", 1),
                    ("Train Car Pieces (per color)", 45),
                    ("Train Cards", 240),
                    ("Destination Ticket Cards", 30),
                    ("Scoring Markers", 5),
                    ("Longest Route Bonus Card", 1)),

                Entry(822, "Carcassonne",
                    "A tile-placement game where players build a medieval landscape and deploy followers to score points.",
                    2, 5, 45,
                    ("Land Tiles", 71),
                    ("Meeples", 40),
                    ("Scoring Track", 1)),

                Entry(30549, "Pandemic",
                    "A cooperative game where players work together as disease-fighting specialists to save the world.",
                    2, 4, 45,
                    ("World Board", 1),
                    ("Player Pawns", 4),
                    ("Role Cards", 5),
                    ("Player Cards", 48),
                    ("Infection Cards", 24),
                    ("Epidemic Cards", 6),
                    ("Cure Markers", 4),
                    ("Research Stations", 6),
                    ("Outbreak Marker", 1),
                    ("Infection Rate Marker", 1)),

                Entry(266192, "Wingspan",
                    "An engine-building card game about attracting birds to wildlife preserves.",
                    1, 5, 70,
                    ("Player Mats", 5),
                    ("Bird Cards", 170),
                    ("Food Tokens", 5),
                    ("Egg Tokens", 36),
                    ("Birdhouse Action Cubes", 5),
                    ("Scoreboard", 1),
                    ("Goal Cards", 26),
                    ("Bonus Cards", 21)),

                Entry(230802, "Azul",
                    "An abstract strategy game where players draft colorful tiles to decorate a palace wall.",
                    2, 4, 45,
                    ("Factory Displays", 9),
                    ("Player Boards", 4),
                    ("Scoring Markers", 4),
                    ("Tile Bags", 1),
                    ("Tiles", 100),
                    ("First Player Marker", 1)),

                Entry(148228, "Splendor",
                    "A strategy game of chip-collecting and card development where players act as Renaissance merchants.",
                    2, 4, 30,
                    ("Ruby Gem Tokens", 7),
                    ("Sapphire Gem Tokens", 7),
                    ("Emerald Gem Tokens", 7),
                    ("Onyx Gem Tokens", 7),
                    ("Diamond Gem Tokens", 7),
                    ("Gold Joker Tokens", 5),
                    ("Development Cards (Tier 1)", 40),
                    ("Development Cards (Tier 2)", 30),
                    ("Development Cards (Tier 3)", 20),
                    ("Noble Tiles", 10)),

                Entry(167791, "Terraforming Mars",
                    "A engine-building game where corporations compete to terraform Mars and develop its ecosystem.",
                    1, 5, 120,
                    ("Game Board", 1),
                    ("Project Cards", 208),
                    ("Corporation Cards", 20),
                    ("Milestone Cards", 3),
                    ("Award Cards", 3),
                    ("Greenery Tiles", 14),
                    ("City Tiles", 9),
                    ("Ocean Tiles", 9),
                    ("Player Boards", 5),
                    ("Resource Cubes (per player)", 25)),

                Entry(174430, "Gloomhaven",
                    "A cooperative campaign dungeon crawler with legacy-style scenarios and tactical combat.",
                    1, 4, 120,
                    ("Scenario Book", 1),
                    ("Map Tiles", 22),
                    ("Character Boards", 17),
                    ("Monster Stat Cards", 34),
                    ("Attack Modifier Deck", 1),
                    ("Item Cards", 1),
                    ("Loot Cards", 1),
                    ("Status Effect Tokens", 18),
                    ("Damage Tokens", 1)),

                Entry(178900, "Codenames",
                    "A social word game where spymasters give one-word clues to help their team identify secret agents.",
                    2, 8, 15,
                    ("Agent Cards", 16),
                    ("Bystander Cards", 7),
                    ("Assassin Card", 1),
                    ("Key Cards", 40),
                    ("Codename Cards", 200),
                    ("Sand Timer", 1)),

                // Additional games from EF seed data
                Entry(1406, "Monopoly",
                    "A classic real estate trading game where players buy, sell, and trade properties to bankrupt their opponents.",
                    2, 8, 120,
                    ("Gameboard", 1),
                    ("Player Tokens", 8),
                    ("Title Deed Cards", 28),
                    ("Chance Cards", 16),
                    ("Community Chest Cards", 16),
                    ("Houses", 32),
                    ("Hotels", 12),
                    ("Dice", 2),
                    ("Monopoly Money", 1)),

                Entry(2225, "Uno",
                    "A fast-paced card game where players match colors and numbers, using action cards to change the game dynamics.",
                    2, 10, 30,
                    ("Number Cards (Red)", 19),
                    ("Number Cards (Blue)", 19),
                    ("Number Cards (Green)", 19),
                    ("Number Cards (Yellow)", 19),
                    ("Skip Cards", 8),
                    ("Reverse Cards", 8),
                    ("Draw Two Cards", 8),
                    ("Wild Cards", 4),
                    ("Wild Draw Four Cards", 4)),

                Entry(925, "Werewolf Ultimate",
                    "A social deduction party game where players are assigned secret roles and must identify the werewolves among them.",
                    5, 20, 45,
                    ("Villager Role Cards", 10),
                    ("Werewolf Role Cards", 4),
                    ("Seer Role Card", 1),
                    ("Doctor Role Card", 1),
                    ("Witch Role Card", 1),
                    ("Hunter Role Card", 1),
                    ("Moderator Script", 1),
                    ("Night Phase Marker", 1),
                    ("Day Phase Marker", 1))
            };

            return entries.ToDictionary(e => e.BggGameId);
        }

        private static KnownBggGameEntry Entry(
            int bggGameId,
            string name,
            string description,
            int minPlayers,
            int maxPlayers,
            int playTime,
            params (string Name, int Quantity)[] components) =>
            new(bggGameId, name, description, minPlayers, maxPlayers, playTime, components);
    }
}
