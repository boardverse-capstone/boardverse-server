namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho LevelingService — tính level + exp progression.
/// Level 1: 0-99 exp
/// Level 2: 100-299 exp (200 exp cần để lên level 3)
/// Level 3: 300-599 exp
/// Level N: cần BaseExpPerLevel + (N-1) * ExpIncrementPerLevel = 100 + (N-1) * 100
/// </summary>
public class LevelingServiceTests
{
    private readonly LevelingService _sut = new();

    #region CalculateLevel

    [Fact]
    public void CalculateLevel_NegativeExp_ReturnsLevel1()
    {
        Assert.Equal(1, _sut.CalculateLevel(-1));
    }

    [Fact]
    public void CalculateLevel_ZeroExp_ReturnsLevel1()
    {
        Assert.Equal(1, _sut.CalculateLevel(0));
    }

    [Fact]
    public void CalculateLevel_99Exp_ReturnsLevel1()
    {
        Assert.Equal(1, _sut.CalculateLevel(99));
    }

    [Fact]
    public void CalculateLevel_Exactly100Exp_ReturnsLevel2()
    {
        Assert.Equal(2, _sut.CalculateLevel(100));
    }

    [Fact]
    public void CalculateLevel_299Exp_ReturnsLevel2()
    {
        Assert.Equal(2, _sut.CalculateLevel(299));
    }

    [Fact]
    public void CalculateLevel_300Exp_ReturnsLevel3()
    {
        Assert.Equal(3, _sut.CalculateLevel(300));
    }

    [Fact]
    public void CalculateLevel_599Exp_ReturnsLevel3()
    {
        Assert.Equal(3, _sut.CalculateLevel(599));
    }

    [Fact]
    public void CalculateLevel_600Exp_ReturnsLevel4()
    {
        // Level 3 needs 300 exp, ends at 599. Next needs 400 (level 4).
        Assert.Equal(4, _sut.CalculateLevel(600));
    }

    [Fact]
    public void CalculateLevel_LargeExp_ReturnsCorrectLevel()
    {
        // Level 5 starts at 100+200+300+400 = 1000
        // Level 5 needs 500, ends at 1499
        // Level 6 needs 600, ends at 2099
        Assert.Equal(5, _sut.CalculateLevel(1000));
        Assert.Equal(5, _sut.CalculateLevel(1499));
        Assert.Equal(6, _sut.CalculateLevel(1500));
    }

    #endregion

    #region GetExpForNextLevel

    [Theory]
    [InlineData(1, 100)]
    [InlineData(2, 200)]
    [InlineData(3, 300)]
    [InlineData(4, 400)]
    [InlineData(10, 1000)]
    public void GetExpForNextLevel_ReturnsExpectedExp(int currentLevel, long expected)
    {
        Assert.Equal(expected, _sut.GetExpForNextLevel(currentLevel));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void GetExpForNextLevel_LevelBelowOne_TreatsAsOne(int level)
    {
        Assert.Equal(100, _sut.GetExpForNextLevel(level));
    }

    #endregion

    #region GetExpToNextLevel

    [Fact]
    public void GetExpToNextLevel_ZeroCurrentExp_AtLevel1_Needs100()
    {
        Assert.Equal(100, _sut.GetExpToNextLevel(currentExp: 0, currentLevel: 1));
    }

    [Fact]
    public void GetExpToNextLevel_50CurrentExp_AtLevel1_Needs50()
    {
        Assert.Equal(50, _sut.GetExpToNextLevel(currentExp: 50, currentLevel: 1));
    }

    [Fact]
    public void GetExpToNextLevel_AlreadyOverLevelThreshold_ReturnsZero()
    {
        // 250 exp, level 2 (needs 100). Threshold for level 3 = 300.
        Assert.Equal(0, _sut.GetExpToNextLevel(currentExp: 250, currentLevel: 2));
    }

    [Fact]
    public void GetExpToNextLevel_FarBeyondThreshold_StillReturnsZero()
    {
        Assert.Equal(0, _sut.GetExpToNextLevel(currentExp: 999_999, currentLevel: 5));
    }

    #endregion

    #region GetLevelAndRemainingExp

    [Fact]
    public void GetLevelAndRemainingExp_ZeroExp_Level1Needs100()
    {
        var (level, remaining) = _sut.GetLevelAndRemainingExp(0);

        Assert.Equal(1, level);
        Assert.Equal(100, remaining);
    }

    [Fact]
    public void GetLevelAndRemainingExp_50ExpAtLevel1_Remaining50()
    {
        var (level, remaining) = _sut.GetLevelAndRemainingExp(50);

        Assert.Equal(1, level);
        Assert.Equal(50, remaining);
    }

    [Fact]
    public void GetLevelAndRemainingExp_Exactly100Exp_Level2Needs200()
    {
        // At 100 exp → start of level 2 → needs 200 more for level 3
        var (level, remaining) = _sut.GetLevelAndRemainingExp(100);

        Assert.Equal(2, level);
        Assert.Equal(200, remaining);
    }

    [Fact]
    public void GetLevelAndRemainingExp_250Exp_Level2Remaining50()
    {
        // 250 exp, start of level 2 = 100, within-level exp = 150, need 200 for next → remaining 50
        var (level, remaining) = _sut.GetLevelAndRemainingExp(250);

        Assert.Equal(2, level);
        Assert.Equal(50, remaining);
    }

    [Fact]
    public void GetLevelAndRemainingExp_OverThresholdOfLevel_ClampsRemainingToZero()
    {
        // 500 exp: level 3 (300-599), within-level exp = 200, need 300 → remaining 100
        var (level, remaining) = _sut.GetLevelAndRemainingExp(500);

        Assert.Equal(3, level);
        Assert.Equal(100, remaining);
    }

    #endregion

    #region UpdateUserLevelAsync (stub)

    [Fact]
    public async Task UpdateUserLevelAsync_AlwaysCompletes()
    {
        await _sut.UpdateUserLevelAsync(Guid.NewGuid(), 100);
        // Stub method - just verifies it doesn't throw.
    }

    #endregion
}
