using BoardVerse.Core.Enum;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho RefundCalculationService — BR-REFUND-01..07 (docs/lobby-booking-deposit-bvc §X).
/// BR-END-04: playedRatio >= 90% → OnTime, refund 0%.
/// BR-REFUND-04: playedRatio >= 50% → refund 30%, forfeit 70%.
/// BR-REFUND-05: playedRatio &lt; 50% → forfeit 100% (refund 0%).
/// </summary>
public class RefundCalculationServiceTests
{
    private readonly RefundCalculationService _sut = new();

    #region Calculate (playedRatio-based)

    [Fact]
    public void Calculate_DepositZero_ReturnsOther()
    {
        var (refund, forfeit, reason) = _sut.Calculate(0, 0.5m);

        Assert.Equal(0, refund);
        Assert.Equal(0, forfeit);
        Assert.Equal(RefundReason.Other, reason);
    }

    [Fact]
    public void Calculate_DepositNegative_ReturnsOther()
    {
        var (refund, forfeit, reason) = _sut.Calculate(-10, 0.5m);

        Assert.Equal(0, refund);
        Assert.Equal(0, forfeit);
        Assert.Equal(RefundReason.Other, reason);
    }

    [Fact]
    public void Calculate_NullRatio_TreatedAsZero_ForfeitsAll()
    {
        var (refund, forfeit, reason) = _sut.Calculate(100, null);

        Assert.Equal(0, refund);
        Assert.Equal(100, forfeit);
        Assert.Equal(RefundReason.EarlyCheckout, reason);
    }

    [Theory]
    [InlineData(0.90)]
    [InlineData(0.95)]
    [InlineData(1.00)]
    public void Calculate_RatioAtLeast90Percent_OnTimeNoRefund(decimal ratio)
    {
        var (refund, forfeit, reason) = _sut.Calculate(100, ratio);

        Assert.Equal(0, refund);
        Assert.Equal(100, forfeit);
        Assert.Equal(RefundReason.OnTime, reason);
    }

    [Theory]
    [InlineData(0.50)]
    [InlineData(0.65)]
    [InlineData(0.89)]
    public void Calculate_RatioBetween50And90Percent_Refunds30Percent(decimal ratio)
    {
        var (refund, forfeit, reason) = _sut.Calculate(100, ratio);

        Assert.Equal(30, refund);
        Assert.Equal(70, forfeit);
        Assert.Equal(RefundReason.EarlyCheckout, reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.10)]
    [InlineData(0.49)]
    public void Calculate_RatioBelow50Percent_ForfeitsAll(decimal ratio)
    {
        var (refund, forfeit, reason) = _sut.Calculate(100, ratio);

        Assert.Equal(0, refund);
        Assert.Equal(100, forfeit);
        Assert.Equal(RefundReason.EarlyCheckout, reason);
    }

    [Fact]
    public void Calculate_30PercentRefund_RoundsAwayFromZero()
    {
        // 100 * 0.30 = 30 (exact)
        Assert.Equal(30, _sut.Calculate(100, 0.60m).Item1);

        // 101 * 0.30 = 30.3 → rounds to 30
        Assert.Equal(30, _sut.Calculate(101, 0.60m).Item1);

        // 103 * 0.30 = 30.9 → rounds to 31
        Assert.Equal(31, _sut.Calculate(103, 0.60m).Item1);
    }

    [Fact]
    public void Calculate_RefundAndForfeit_SumEqualsOriginalDeposit()
    {
        const long deposit = 123;

        foreach (var ratio in new[] { 0m, 0.10m, 0.49m, 0.50m, 0.65m, 0.89m, 0.90m, 1.00m })
        {
            var (refund, forfeit, _) = _sut.Calculate(deposit, ratio);
            Assert.Equal(deposit, refund + forfeit);
        }
    }

    #endregion

    #region CalculateHostCancel (BR-REFUND-02 + BR-REFUND-03)

    [Fact]
    public void CalculateHostCancel_InGracePeriod_RefundsFull()
    {
        var (refund, forfeit, reason) = _sut.CalculateHostCancel(100, hoursBeforeStart: 2.0, isInGracePeriod: true);

        Assert.Equal(100, refund);
        Assert.Equal(0, forfeit);
        Assert.Equal(RefundReason.CancelGracePeriod, reason);
    }

    [Fact]
    public void CalculateHostCancel_Before24Hours_RefundsFull()
    {
        var (refund, forfeit, reason) = _sut.CalculateHostCancel(100, hoursBeforeStart: 48.0, isInGracePeriod: false);

        Assert.Equal(100, refund);
        Assert.Equal(0, forfeit);
        Assert.Equal(RefundReason.CancelBefore24h, reason);
    }

    [Fact]
    public void CalculateHostCancel_Exactly24Hours_RefundsFull()
    {
        var (refund, forfeit, reason) = _sut.CalculateHostCancel(100, hoursBeforeStart: 24.0, isInGracePeriod: false);

        Assert.Equal(100, refund);
        Assert.Equal(0, forfeit);
        Assert.Equal(RefundReason.CancelBefore24h, reason);
    }

    [Theory]
    [InlineData(23.99)]
    [InlineData(12.0)]
    [InlineData(1.0)]
    [InlineData(0.0)]
    public void CalculateHostCancel_After24Hours_ForfeitsAll(double hours)
    {
        var (refund, forfeit, reason) = _sut.CalculateHostCancel(100, hoursBeforeStart: hours, isInGracePeriod: false);

        Assert.Equal(0, refund);
        Assert.Equal(100, forfeit);
        Assert.Equal(RefundReason.CancelAfter24h, reason);
    }

    #endregion

    #region CalculateNoShow (BR-CHECKIN-02 + BR-REFUND-03)

    [Fact]
    public void CalculateNoShow_ForfeitsAll()
    {
        var (refund, forfeit, reason) = _sut.CalculateNoShow(100);

        Assert.Equal(0, refund);
        Assert.Equal(100, forfeit);
        Assert.Equal(RefundReason.NoShow, reason);
    }

    [Fact]
    public void CalculateNoShow_ZeroDeposit_StillReturnsReason()
    {
        var (refund, forfeit, reason) = _sut.CalculateNoShow(0);

        Assert.Equal(0, refund);
        Assert.Equal(0, forfeit);
        Assert.Equal(RefundReason.NoShow, reason);
    }

    #endregion

    #region CalculateCafeCancel (BR-REFUND-04)

    [Fact]
    public void CalculateCafeCancel_RefundsFull()
    {
        var (refund, forfeit, reason) = _sut.CalculateCafeCancel(100);

        Assert.Equal(100, refund);
        Assert.Equal(0, forfeit);
        Assert.Equal(RefundReason.StaffOverride, reason);
    }

    #endregion
}
