namespace BoardVerse.Core.Helpers
{
    public static class EloRatingHelper
    {
        public const int DefaultRating = 1200;
        public const int MinimumRating = 100;

        /// <summary>Standard K-factor tiers based on current rating and optional configured base K.</summary>
        public static int GetKFactor(int currentRating, int? configuredBaseK = null)
        {
            var baseK = configuredBaseK ?? 32;
            return currentRating switch
            {
                < 2100 => baseK,
                < 2400 => (int)Math.Round(baseK * 0.75, MidpointRounding.AwayFromZero),
                _ => (int)Math.Round(baseK * 0.5, MidpointRounding.AwayFromZero)
            };
        }

        public static double ExpectedScore(int ratingA, int ratingB) =>
            1.0 / (1.0 + Math.Pow(10, (ratingB - ratingA) / 400.0));

        /// <summary>
        /// Multi-player Elo: each player's score is 1 (win), 0 (loss), or 0.5 (draw-all).
        /// Expected score is the average expected result vs every other participant.
        /// </summary>
        public static IReadOnlyDictionary<Guid, int> CalculateRatingChanges(
            IReadOnlyDictionary<Guid, int> ratingsByUser,
            Guid? winnerUserId,
            bool isDraw,
            int? configuredBaseK = null)
        {
            var players = ratingsByUser.Keys.ToList();
            if (players.Count < 2)
            {
                return players.ToDictionary(id => id, _ => 0);
            }

            var deltas = new Dictionary<Guid, int>();

            foreach (var playerId in players)
            {
                var playerRating = ratingsByUser[playerId];
                var actualScore = ResolveActualScore(playerId, winnerUserId, isDraw);

                var expectedSum = 0.0;
                foreach (var opponentId in players)
                {
                    if (opponentId == playerId)
                    {
                        continue;
                    }

                    expectedSum += ExpectedScore(playerRating, ratingsByUser[opponentId]);
                }

                var expected = expectedSum / (players.Count - 1);
                var k = GetKFactor(playerRating, configuredBaseK);
                var delta = k * (actualScore - expected);
                var newRating = Math.Max(MinimumRating, playerRating + (int)Math.Round(delta, MidpointRounding.AwayFromZero));
                deltas[playerId] = newRating - playerRating;
            }

            return deltas;
        }

        private static double ResolveActualScore(Guid playerId, Guid? winnerUserId, bool isDraw)
        {
            if (isDraw)
            {
                return 0.5;
            }

            if (!winnerUserId.HasValue)
            {
                return 0.5;
            }

            return playerId == winnerUserId.Value ? 1.0 : 0.0;
        }
    }
}
