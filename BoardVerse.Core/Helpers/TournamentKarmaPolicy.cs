namespace BoardVerse.Core.Helpers;

/// <summary>
/// Quy tắc tính Karma bonus/penalty cho tournament.
/// Karma nằm trong khoảng [0, 100] (BR quy định max = 100) — mọi bonus/penalty đều clamp về khoảng này.
/// <para>
/// Scale (S2 — phù hợp max=100):
/// <list type="bullet">
///   <item>Winner: +5</item>
///   <item>Finalist (rank 2..N): linear giảm dần, ví dụ FinalistCount=4 → rank 2: +3, rank 3: +2, rank 4: +1</item>
///   <item>No-show: -10 (default, manager có thể điều chỉnh)</item>
/// </list>
/// </para>
/// </summary>
public static class TournamentKarmaPolicy
{
    public const int KarmaMin = 0;
    public const int KarmaMax = 100;

    /// <summary>Bonus cố định cho nhà vô địch (rank 1).</summary>
    public const int WinnerBonus = 5;

    /// <summary>Penalty mặc định cho người không đến (no-show). Manager override được qua DTO.</summary>
    public const int NoShowPenalty = -10;

    /// <summary>
    /// Giảm tuyến tính giữa các finalist (rank 2..FinalistCount).
    /// </summary>
    /// <remarks>
    /// Công thức: bonus = max(1, WinnerBonus - (finalRank - 1) * step), step = max(1, ceil(WinnerBonus / FinalistCount)).
    /// Ví dụ FinalistCount=4, WinnerBonus=5 → step=2 → rank 2: 3, rank 3: 2, rank 4: 1.
    /// </remarks>
    public static int GetFinalistBonus(int finalRank, int finalistCount)
    {
        if (finalRank < 2) return 0;
        var step = Math.Max(1, (int)Math.Ceiling((double)WinnerBonus / Math.Max(1, finalistCount)));
        var bonus = WinnerBonus - (finalRank - 1) * step;
        return Math.Clamp(bonus, 1, WinnerBonus);
    }

    public static int ClampKarma(int value) => Math.Clamp(value, KarmaMin, KarmaMax);

    public static int ClampPenalty(int value) => Math.Clamp(value, -KarmaMax, 0);
}
