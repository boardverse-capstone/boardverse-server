using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class CafeGameWaitTimeHelperTests
{
    [Fact]
    public void CalculateEstimatedWaitMinutes_NoActiveSessions_ReturnsDefaultPlayTime()
    {
        var utcNow = new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc);

        var result = CafeGameWaitTimeHelper.CalculateEstimatedWaitMinutes(
            defaultPlayTimeMinutes: 90,
            activeSessionStartTimes: [],
            utcNow);

        Assert.Equal(90, result);
    }

    [Fact]
    public void CalculateEstimatedWaitMinutes_ReturnsShortestRemainingWait()
    {
        var utcNow = new DateTime(2026, 6, 17, 12, 30, 0, DateTimeKind.Utc);
        var starts = new[]
        {
            utcNow.AddMinutes(-60), // 30 min left on 90 min game
            utcNow.AddMinutes(-80)  // 10 min left
        };

        var result = CafeGameWaitTimeHelper.CalculateEstimatedWaitMinutes(90, starts, utcNow);

        Assert.Equal(10, result);
    }

    [Fact]
    public void CalculateEstimatedWaitMinutes_ElapsedExceedsPlayTime_FloorsAtZero()
    {
        var utcNow = new DateTime(2026, 6, 17, 14, 0, 0, DateTimeKind.Utc);
        var starts = new[] { utcNow.AddMinutes(-120) };

        var result = CafeGameWaitTimeHelper.CalculateEstimatedWaitMinutes(90, starts, utcNow);

        Assert.Equal(0, result);
    }
}
