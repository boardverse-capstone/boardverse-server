using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Services.Services;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho DepositCalculator — pure function (BR-DEPOSIT-02..04, BR-NEW-01, BR-LOBBY-01a/b/c).
/// BR §XXI-1 review checklist #2: "Công thức cọc đúng: max(minDeposit(khoảng cách), rate × maxPlayers × riskMultiplier)".
/// </summary>
public class DepositCalculatorTests
{
    private readonly DepositCalculator _calculator = new();

    private static CafeConfig BuildCafeConfig(long ratePerPerson = 5, int capacity = 30)
    {
        return new CafeConfig
        {
            CafeId = Guid.NewGuid(),
            Capacity = capacity,
            DepositRatePerPerson = ratePerPerson,
            MaxPlayersPerLobbySameDay = 30,
            MaxPlayersPerLobby1Day = 20,
            MaxPlayersPerLobby2Days = 15,
            MaxPlayersPerLobby3To4Days = 10,
            MaxPlayersPerLobby5To7Days = 6,
            MinDepositSameDay = 50,
            MinDeposit1Day = 50,
            MinDeposit2Days = 100,
            MinDeposit3To4Days = 150,
            MinDeposit5To7Days = 200,
            RequireApprovalForDistant = true,
            DistantThresholdDays = 2,
            RecruitmentDeadlineBufferMinutes = 120
        };
    }

    private static ReservationQuoteRequestDto BuildRequest(
        DateOnly playDate,
        TimeSlot slot = TimeSlot.Evening,
        int minPlayers = 2,
        int maxPlayers = 6)
    {
        return new ReservationQuoteRequestDto
        {
            CafeId = Guid.NewGuid(),
            GameId = Guid.NewGuid(),
            PlayDate = playDate,
            TimeSlot = slot,
            MinPlayers = minPlayers,
            MaxPlayers = maxPlayers,
            IdempotencyKey = $"quote-{Guid.NewGuid():N}"
        };
    }

    // ===== BR-DEPOSIT-02: baseDeposit = ratePerPerson × maxPlayers =====

    [Fact]
    public void Calculate_SameDay_NormalRisk_BasesOnRatePerPerson()
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 6);
        var config = BuildCafeConfig(ratePerPerson: 5);

        // Act
        var result = _calculator.Calculate(request, config, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now);

        // Assert
        Assert.Equal(30, result.BaseDeposit); // 5 × 6
        Assert.Equal(1.0m, result.RiskMultiplier);
        Assert.Equal(DistanceBucket.SameDay, result.Distance);
        Assert.Equal(50, result.FinalDeposit); // minDeposit = 50 BVC dominates
        Assert.Equal(50, result.MinDepositApplied);
    }

    // ===== BR-NEW-01 §VIII: minDeposit theo khoảng cách playDate =====

    [Theory]
    [InlineData(0, 50)] // SameDay
    [InlineData(1, 50)] // OneDay
    [InlineData(2, 100)] // TwoDays
    [InlineData(3, 150)] // 3 days
    [InlineData(4, 150)] // 4 days
    [InlineData(5, 200)] // 5 days
    [InlineData(7, 200)] // 7 days
    public void Calculate_MinDeposit_MatchesDistanceBucket(int daysInFuture, long expectedMinDeposit)
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date).AddDays(daysInFuture);
        var request = BuildRequest(playDate, maxPlayers: 6);
        var config = BuildCafeConfig(ratePerPerson: 1); // rate × 6 = 6 BVC, much below minDeposit

        // Act
        var result = _calculator.Calculate(request, config, 1.0m, false, false, now);

        // Assert
        Assert.Equal(expectedMinDeposit, result.MinDepositApplied);
        Assert.Equal(expectedMinDeposit, result.FinalDeposit);
    }

    // ===== BR-NEW-01 §VIII: maxPlayers clamped theo khoảng cách =====

    [Fact]
    public void Calculate_MaxPlayersOverLimit_Distance2Days_ClampsTo15()
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date).AddDays(2);
        var request = BuildRequest(playDate, maxPlayers: 30); // yêu cầu 30
        var config = BuildCafeConfig(ratePerPerson: 1);

        // Act
        var result = _calculator.Calculate(request, config, 1.0m, false, false, now);

        // Assert
        Assert.Equal(15, result.MaxPlayersApplied); // clamped xuống 15
        Assert.Equal(DistanceBucket.TwoDays, result.Distance);
    }

    [Fact]
    public void Calculate_MaxPlayersOverLimit_5Days_ClampsTo6()
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date).AddDays(5);
        var request = BuildRequest(playDate, maxPlayers: 20);
        var config = BuildCafeConfig(ratePerPerson: 1);

        // Act
        var result = _calculator.Calculate(request, config, 1.0m, false, false, now);

        // Assert
        Assert.Equal(6, result.MaxPlayersApplied);
        Assert.Equal(DistanceBucket.FiveToSevenDays, result.Distance);
    }

    // ===== BR-DEPOSIT-04 + BR-RISK-03: riskMultiplier áp dụng =====

    [Fact]
    public void Calculate_RiskMultiplier1_5_IncreasesAdjustedDeposit()
    {
        // Arrange: rate=10, maxPlayers=100 → base=1000; riskMultiplier=1.5 → adjusted=1500
        // minDeposit = 200 dominating, nên test minDeposit=0 để thấy adjusted
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date).AddDays(5); // 5 days
        var request = BuildRequest(playDate, maxPlayers: 6);
        var config = BuildCafeConfig(ratePerPerson: 10);
        config.MinDeposit5To7Days = 0; // tắt minDeposit để thấy adjusted

        // Act
        var result = _calculator.Calculate(request, config, walletRiskMultiplier: 1.5m, isCoolingOff: false, isPrivateLobby: false, now: now);

        // Assert
        Assert.Equal(60, result.BaseDeposit); // 10 × 6
        Assert.Equal(1.5m, result.RiskMultiplier);
        Assert.Equal(90, result.FinalDeposit); // 60 × 1.5 = 90
    }

    [Fact]
    public void Calculate_RiskMultiplierAbove2_ClampsTo2()
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date); // same day
        var request = BuildRequest(playDate, maxPlayers: 4);
        var config = BuildCafeConfig(ratePerPerson: 1);
        config.MinDepositSameDay = 0;

        // Act
        var result = _calculator.Calculate(request, config, walletRiskMultiplier: 5.0m, isCoolingOff: false, isPrivateLobby: false, now: now);

        // Assert
        Assert.Equal(2.0m, result.RiskMultiplier); // clamped
        Assert.Equal(8, result.FinalDeposit); // 1 × 4 × 2 = 8
    }

    // ===== BR-NEW-10: cooling-off × 2 riskMultiplier =====

    [Fact]
    public void Calculate_CoolingOff_DoublesEffectiveRiskMultiplier()
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date);
        var request = BuildRequest(playDate, maxPlayers: 4);
        var config = BuildCafeConfig(ratePerPerson: 1);
        config.MinDepositSameDay = 0;

        // Act
        var result = _calculator.Calculate(request, config, walletRiskMultiplier: 1.0m, isCoolingOff: true, isPrivateLobby: false, now: now);

        // Assert
        Assert.Equal(2.0m, result.RiskMultiplier); // 1.0 × 2 = 2.0 (clamped at 2.0)
        Assert.Equal(8, result.FinalDeposit); // 1 × 4 × 2 = 8
    }

    [Fact]
    public void Calculate_CoolingOff_WithHalfRisk_StillDoublesTo2()
    {
        // Arrange: riskMultiplier = 1.25 → cooling-off = 2.5 → clamped to 2.0
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date);
        var request = BuildRequest(playDate, maxPlayers: 4);
        var config = BuildCafeConfig(ratePerPerson: 1);
        config.MinDepositSameDay = 0;

        // Act
        var result = _calculator.Calculate(request, config, walletRiskMultiplier: 1.25m, isCoolingOff: true, isPrivateLobby: false, now: now);

        // Assert
        Assert.Equal(2.0m, result.RiskMultiplier);
    }

    // ===== BR-DEPOSIT-03: rate per person ∈ [1, 100] =====

    [Theory]
    [InlineData(0, 1)] // below min → clamp to 1
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(150, 100)] // above max → clamp to 100
    public void Calculate_RatePerPerson_ClampedToValidRange(int rawRate, int expectedAppliedRate)
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date);
        var request = BuildRequest(playDate, maxPlayers: 6);
        var config = BuildCafeConfig(ratePerPerson: rawRate);
        config.MinDepositSameDay = 0;

        // Act
        var result = _calculator.Calculate(request, config, 1.0m, false, false, now);

        // Assert
        Assert.Equal((long)expectedAppliedRate * 6, result.BaseDeposit);
    }

    // ===== BR-NEW-11: cafe approval =====

    [Theory]
    [InlineData(0, false)] // same day
    [InlineData(1, false)] // 1 day
    [InlineData(2, true)] // 2 days (maxPlayers > 10 OR RequireApprovalForDistant=true)
    [InlineData(3, true)] // 3-4 days
    [InlineData(5, true)] // 5-7 days
    public void Calculate_RequiresCafeApproval_TrueForDistantPlayDate(int daysInFuture, bool expected)
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date).AddDays(daysInFuture);
        var request = BuildRequest(playDate, maxPlayers: 6);
        var config = BuildCafeConfig(ratePerPerson: 1);

        // Act
        var result = _calculator.Calculate(request, config, 1.0m, false, false, now);

        // Assert
        Assert.Equal(expected, result.RequiresCafeApproval);
    }

    // ===== BR-LOBBY-01a/b/c: buffer validation =====

    [Theory]
    [InlineData(30, false, false)] // < 60 → reject
    [InlineData(60, true, true)] // 60–119 → warning
    [InlineData(119, true, true)]
    [InlineData(120, true, false)] // ≥ 120 → ok
    [InlineData(600, true, false)]
    public void EvaluateBuffer_ReturnsExpectedTuple(int bufferMinutes, bool expectedOk, bool expectedWarning)
    {
        // Act
        var (isAllowed, needsWarning) = DepositCalculator.EvaluateBuffer(bufferMinutes);

        // Assert
        Assert.Equal(expectedOk, isAllowed);
        Assert.Equal(expectedWarning, needsWarning);
    }

    // ===== BR-NEW-15: distance bucket mapping =====

    [Theory]
    [InlineData(0, DistanceBucket.SameDay)]
    [InlineData(1, DistanceBucket.OneDay)]
    [InlineData(2, DistanceBucket.TwoDays)]
    [InlineData(3, DistanceBucket.ThreeToFourDays)]
    [InlineData(4, DistanceBucket.ThreeToFourDays)]
    [InlineData(5, DistanceBucket.FiveToSevenDays)]
    [InlineData(7, DistanceBucket.FiveToSevenDays)]
    [InlineData(-1, DistanceBucket.OutOfRange)]
    [InlineData(8, DistanceBucket.OutOfRange)]
    public void MapDistanceBucket_ReturnsCorrectBucket(int daysInFuture, DistanceBucket expected)
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date).AddDays(daysInFuture);

        // Act
        var result = DepositCalculator.MapDistanceBucket(playDate, now);

        // Assert
        Assert.Equal(expected, result);
    }

    // ===== BR-LOBBY-01c: buffer warning flag =====

    [Fact]
    public void Calculate_BufferUnder2HoursButOver1Hour_SetsBufferWarning()
    {
        // Arrange: now = 17:00, playDate evening 18:00 → scheduledTime = 18:00
        // recruitmentDeadline = 18:00 - 20min = 17:40 → buffer = 40 phút (< 60 → reject)
        var now = new DateTime(2026, 8, 2, 17, 0, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date);
        var request = BuildRequest(playDate, TimeSlot.Evening, maxPlayers: 6);
        var config = BuildCafeConfig(ratePerPerson: 1);

        // Act
        var result = _calculator.Calculate(request, config, 1.0m, false, false, now);

        // Assert
        Assert.Equal(40, result.BufferMinutes);
        Assert.False(result.BufferWarning); // 40 < 60 → không warning, reject
    }

    [Fact]
    public void Calculate_BufferBetween60And120_SetsBufferWarningTrue()
    {
        // Arrange: now = 16:00, scheduledTime = 18:00 → buffer = 120-20 = 100 phút
        var now = new DateTime(2026, 8, 2, 16, 0, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date);
        var request = BuildRequest(playDate, TimeSlot.Evening, maxPlayers: 6);
        var config = BuildCafeConfig(ratePerPerson: 1);

        // Act
        var result = _calculator.Calculate(request, config, 1.0m, false, false, now);

        // Assert
        Assert.Equal(100, result.BufferMinutes);
        Assert.True(result.BufferWarning);
    }

    // ===== Validation throws =====

    [Fact]
    public void Calculate_PlayDateInPast_ThrowsArgumentException()
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date).AddDays(-1));
        var config = BuildCafeConfig();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _calculator.Calculate(request, config, 1.0m, false, false, now));
    }

    [Fact]
    public void Calculate_MaxPlayersBelowMinPlayers_ThrowsArgumentException()
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 5, maxPlayers: 3);
        var config = BuildCafeConfig();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _calculator.Calculate(request, config, 1.0m, false, false, now));
    }

    [Fact]
    public void Calculate_MinPlayersBelow2_ThrowsArgumentException()
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 1, maxPlayers: 4);
        var config = BuildCafeConfig();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _calculator.Calculate(request, config, 1.0m, false, false, now));
    }

    [Fact]
    public void Calculate_MaxPlayersExceedsCafeConfigCapacity_ThrowsArgumentException()
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        // SameDay limit = 30, nhưng cafeConfig.Capacity = 5 → must throw
        var playDate = DateOnly.FromDateTime(now.Date);
        var request = BuildRequest(playDate, maxPlayers: 6);
        var config = BuildCafeConfig(ratePerPerson: 1);
        config.Capacity = 5;

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _calculator.Calculate(request, config, 1.0m, false, false, now));
    }
}
