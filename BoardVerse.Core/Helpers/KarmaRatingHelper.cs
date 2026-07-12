using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Helpers
{
    public static class KarmaRatingHelper
    {
        public static readonly IReadOnlyDictionary<KarmaRatingTag, decimal> TagWeights =
            new Dictionary<KarmaRatingTag, decimal>
            {
                [KarmaRatingTag.OnTime] = 0.5m,
                [KarmaRatingTag.Civil] = 0.5m,
                [KarmaRatingTag.Friendly] = 0.5m,
                [KarmaRatingTag.Toxic] = -2m,
                [KarmaRatingTag.NoShow] = -2m
            };

        public static IReadOnlyList<KarmaRatingTag> AvailableTags { get; } =
            System.Enum.GetValues<KarmaRatingTag>().ToList();

        public static decimal CalculateDelta(IEnumerable<KarmaRatingTag> tags)
        {
            return tags
                .Distinct()
                .Sum(tag => TagWeights.TryGetValue(tag, out var weight) ? weight : 0m);
        }

        public static int ApplyDeltaToKarmaPoints(int currentPoints, decimal delta)
        {
            var updated = currentPoints + delta;
            var rounded = (int)Math.Round(updated, MidpointRounding.AwayFromZero);
            return Math.Max(0, rounded);
        }

        public static GamerTier ResolveTier(int karmaPoints) =>
            karmaPoints switch
            {
                >= 300 => GamerTier.Gold,
                >= 150 => GamerTier.Silver,
                _ => GamerTier.Bronze
            };

        public static bool IsRatingAllowed(LobbyStatus status) =>
            status is LobbyStatus.InProgress or LobbyStatus.Closed;
    }
}
