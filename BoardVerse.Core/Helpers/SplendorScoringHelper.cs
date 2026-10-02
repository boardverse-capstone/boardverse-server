namespace BoardVerse.Core.Helpers;

/// <summary>
/// Helper tính điểm Swiss theo công thức BPA Splendor Tournament.
/// Công thức:
/// - 1st place: 1000 + (your_score / 2nd_place_score)
/// - 2nd place: 100 + (your_score / winner_score)
/// - 3rd place: 10 + (your_score / winner_score)
/// - 4th place: 1 + (your_score / winner_score)
///
/// Tiebreaker khi hòa Swiss score (theo thứ tự ưu tiên):
/// 1. Người chơi sở hữu ÍT thẻ Development nhất.
/// 2. Người chơi sở hữu NHIỀU thẻ Noble nhất.
/// 3. Người chơi còn lại NHIỀU viên đá quý nhất.
/// 4. Người chơi có thứ tự lượt đi sau hơn (turn order).
///
/// Nguồn: https://bgtournament.wordpress.com/splendor/tournament-format/
/// </summary>
public static class SplendorScoringHelper
{
    /// <summary>
    /// Tính Swiss score cho 1 match theo công thức BPA.
    /// </summary>
    /// <param name="rank">Hạng của player trong match (1 = winner, 2-4 = other ranks).</param>
    /// <param name="playerScore">Prestige Points của player.</param>
    /// <param name="winnerScore">Prestige Points của winner.</param>
    /// <param name="secondPlaceScore">Prestige Points của hạng 2 (chỉ dùng khi rank=1).</param>
    /// <returns>Swiss score theo công thức BPA (decimal).</returns>
    public static decimal CalculateSwissScore(int rank, int playerScore, int winnerScore, int secondPlaceScore)
    {
        // Defensive: tránh chia cho 0
        if (winnerScore <= 0) winnerScore = 1;
        if (secondPlaceScore <= 0) secondPlaceScore = 1;
        if (playerScore < 0) playerScore = 0;

        return rank switch
        {
            1 => 1000m + ((decimal)playerScore / secondPlaceScore),
            2 => 100m + ((decimal)playerScore / winnerScore),
            3 => 10m + ((decimal)playerScore / winnerScore),
            4 => 1m + ((decimal)playerScore / winnerScore),
            _ => 0m
        };
    }

    /// <summary>
    /// So sánh 2 participants theo Swiss score + tiebreaker đầy đủ.
    /// Return:
    ///   &lt; 0: p1 xếp trên p2 (cao hơn)
    ///   &gt; 0: p2 xếp trên p1
    ///   = 0: hòa hoàn toàn (rất hiếm)
    /// </summary>
    public static int CompareSwissRanking(
        decimal p1SwissScore, int p1Cards, int p1Nobles, int p1Gems, int p1TurnOrder,
        decimal p2SwissScore, int p2Cards, int p2Nobles, int p2Gems, int p2TurnOrder)
    {
        // 1. Swiss score cao hơn = xếp trên
        if (p1SwissScore != p2SwissScore)
            return p2SwissScore.CompareTo(p1SwissScore);

        // Hòa Swiss score → tiebreaker:
        // 2. ÍT thẻ Development hơn = xếp trên
        if (p1Cards != p2Cards)
            return p1Cards.CompareTo(p2Cards);

        // 3. NHIỀU thẻ Noble hơn = xếp trên
        if (p1Nobles != p2Nobles)
            return p2Nobles.CompareTo(p1Nobles);

        // 4. NHIỀU gems còn lại hơn = xếp trên
        if (p1Gems != p2Gems)
            return p2Gems.CompareTo(p1Gems);

        // 5. Turn order sau hơn = xếp trên (player đi sau có lợi thế trong Splendor)
        return p2TurnOrder.CompareTo(p1TurnOrder);
    }
}
