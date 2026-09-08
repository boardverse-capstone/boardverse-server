using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Helpers
{
/// <summary>
/// Helper chuẩn hoá thao tác với điểm Karma và tier.
/// <para>
/// Karma nằm trong khoảng [0, 100] (BR quy định max = 100).
/// Default user mới = 100 (= neutral / full marks).
/// Tier thresholds:
/// <list type="bullet">
///   <item>Bronze: &lt; 70</item>
///   <item>Silver: 70 - 89</item>
///   <item>Gold:   ≥ 90</item>
/// </list>
/// </para>
/// </summary>
public static class KarmaRatingHelper
{
    public const int KarmaMin = 0;
    public const int KarmaMax = 100;

    /// <summary>Bronze upper bound (exclusive).</summary>
    public const int SilverThreshold = 70;

    /// <summary>Silver upper bound (exclusive).</summary>
    public const int GoldThreshold = 90;

    public static readonly IReadOnlyDictionary<KarmaRatingTag, decimal> TagWeights =
        new Dictionary<KarmaRatingTag, decimal>
        {
            [KarmaRatingTag.OnTime] = 0.1m,
            [KarmaRatingTag.Civil] = 0.1m,
            [KarmaRatingTag.Friendly] = 0.1m,
            [KarmaRatingTag.Toxic] = -1m,
            [KarmaRatingTag.NoShow] = -1m
        };

    public static IReadOnlyList<KarmaRatingTag> AvailableTags { get; } =
        System.Enum.GetValues<KarmaRatingTag>().ToList();

    public static decimal CalculateDelta(IEnumerable<KarmaRatingTag> tags)
    {
        return tags
            .Distinct()
            .Sum(tag => TagWeights.TryGetValue(tag, out var weight) ? weight : 0m);
    }

    /// <summary>Áp dụng delta vào current Karma, kẹp về [0, 100].</summary>
    public static int ApplyDeltaToKarmaPoints(int currentPoints, decimal delta)
    {
        var updated = currentPoints + delta;
        var rounded = (int)Math.Round(updated, MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, KarmaMin, KarmaMax);
    }

    public static GamerTier ResolveTier(int karmaPoints) =>
        karmaPoints switch
        {
            >= GoldThreshold => GamerTier.Gold,
            >= SilverThreshold => GamerTier.Silver,
            _ => GamerTier.Bronze
        };

    /// <summary>
    /// Lobby đang trong cửa sổ đánh giá Karma — mọi thành viên (kể cả host) có thể
    /// submit / lấy context đánh giá chéo. Cho phép cả 3 trạng thái:
    /// <list type="bullet">
    ///   <item><c>RatingOpen</c>: cửa sổ đánh giá đang mở (sau OpenLobbyKarmaRatingWindowAsync).</item>
    ///   <item><c>Closed</c>: phiên chơi đã đóng nhưng host có thể bật rating trước khi mở cửa sổ.</item>
    ///   <item><c>InProgress</c>: phiên chơi đang chạy — cho phép early rating (vd. host muốn rate ngay khi session end).</item>
    /// </list>
    /// </summary>
    public static bool IsRatingAllowed(LobbyStatus status) =>
        status is LobbyStatus.RatingOpen or LobbyStatus.Closed or LobbyStatus.InProgress;
}
}
