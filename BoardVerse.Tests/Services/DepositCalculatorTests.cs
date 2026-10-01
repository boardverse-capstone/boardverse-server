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
            MinDepositRatePerPerson = 1,
            MaxDepositRatePerPerson = 100,
            // Mặc định BR-NEW-01: cùng ngày 50 BVC, +1 ngày 50, +2 100, +3-4 150, +5-7 200.
            // Test muốn assert pure formula thì gọi WithNoMinDepositCap() hoặc override thủ công.
            MaxPlayersPerLobbySameDay = 30,
            MaxPlayersPerLobby1Day = 20,
            MaxPlayersPerLobby2Days = 15,
            MaxPlayersPerLobby3To4Days = 10,
            MaxPlayersPerLobby5To7Days = 6,
            MinDepositSameDay = 0,
            MinDeposit1Day = 0,
            MinDeposit2Days = 0,
            MinDeposit3To4Days = 0,
            MinDeposit5To7Days = 0,
            RequireApprovalForDistant = true,
            DistantThresholdDays = 2,
            RecruitmentDeadlineBufferMinutes = 120
        };
    }

    /// <summary>
    /// Trả về CafeConfig với BR-NEW-01 minimum deposit theo distance áp dụng (test riêng).
    /// </summary>
    private static CafeConfig BuildCafeConfigWithBrNew01Min(
        long minSameDay = 50, long minOneDay = 50, long minTwoDays = 100,
        long min3To4Days = 150, long min5To7Days = 200)
    {
        var config = BuildCafeConfig();
        config.MinDepositSameDay = minSameDay;
        config.MinDeposit1Day = minOneDay;
        config.MinDeposit2Days = minTwoDays;
        config.MinDeposit3To4Days = min3To4Days;
        config.MinDeposit5To7Days = min5To7Days;
        return config;
    }

    private static ReservationQuoteRequestDto BuildRequest(
        DateOnly playDate,
        int minPlayers = 2,
        int maxPlayers = 6)
    {
        return new ReservationQuoteRequestDto
        {
            CafeId = Guid.NewGuid(),
            GameId = Guid.NewGuid(),
            PlayDate = playDate,
            PreferredStartTime = new TimeOnly(18, 0),
            PreferredEndTime = new TimeOnly(22, 0),
            MinPlayers = minPlayers,
            MaxPlayers = maxPlayers,
            IdempotencyKey = $"quote-{Guid.NewGuid():N}"
        };
    }

    // ===== BR-DEPOSIT-02..04 + BR-NEW-01 (2026-10-01): baseDeposit = DepositRatePerPerson × maxPlayers; finalDeposit = max(minDepositByDistance, baseDeposit × riskMultiplier) =====

    [Fact]
    public void Calculate_BR_DEPOSIT_02_BasicFormula_RatePerPersonTimesMaxPlayers()
    {
        // BR-DEPOSIT-02: baseDeposit = DepositRatePerPerson (BVC/người) × finalMaxPlayers.
        // BR-DEPOSIT-04: riskAdjusted = round(baseDeposit × walletRiskMultiplier).
        // BR-NEW-01: finalDeposit = max(minDepositByDistance, riskAdjusted).

        // Arrange: ratePerPerson = 5 BVC/người (từ CafeConfig.DepositRatePerPerson), maxPlayers = 6, riskMultiplier = 1.0
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 6);
        var config = BuildCafeConfig(ratePerPerson: 5);

        // Act: cafeBasePrice = 100,000 VND → BR-03 cap = floor(50,000/1000) = 50 BVC (no cap on perPersonBvc=5).
        //       perPersonBvc = min(clamp(5, 1, 100), 50) = 5
        //       baseDeposit = 5 × 6 = 30
        //       riskAdjusted = round(30 × 1.0) = 30
        //       finalDeposit = max(0, 30) = 30
        var result = _calculator.Calculate(request, config, cafeBasePrice: 100_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now);

        // Assert
        Assert.Equal(5, result.DepositPerPerson);
        Assert.Equal(30, result.BaseDeposit);
        Assert.Equal(30, result.FinalDeposit);
        Assert.Equal(DistanceBucket.SameDay, result.Distance);
    }

    [Fact]
    public void Calculate_BR_DEPOSIT_04_WithRiskMultiplier_AppliesMultiplier()
    {
        // BR-DEPOSIT-04: riskAdjusted = round(baseDeposit × walletRiskMultiplier).
        // BR-RISK-03 mapping: riskMultiplier ∈ [1.0, 2.0]; cooling-off × 2.

        // Arrange: ratePerPerson = 10, maxPlayers = 4, riskMultiplier = 1.25
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 4);
        var config = BuildCafeConfig(ratePerPerson: 10);

        // Act: perPersonBvc = 10, baseDeposit = 10 × 4 = 40
        //       riskAdjusted = round(40 × 1.25) = round(50.0) = 50
        //       finalDeposit = max(0, 50) = 50
        var result = _calculator.Calculate(request, config, cafeBasePrice: 100_000m, walletRiskMultiplier: 1.25m, isCoolingOff: false, isPrivateLobby: false, now: now);

        // Assert
        Assert.Equal(10, result.DepositPerPerson);
        Assert.Equal(40, result.BaseDeposit);
        Assert.Equal(50, result.FinalDeposit);
        Assert.Equal(1.25m, result.RiskMultiplier);
    }

    [Fact]
    public void Calculate_BR_DEPOSIT_04_WithCoolingOffHighMultiplier_DoublesDeposit()
    {
        // BR-NEW-10 + BR-RISK-03: cooling-off → walletRiskMultiplier × 2 (caller đã pre-multiply).
        // Test: ratePerPerson = 10, maxPlayers = 4, riskMultiplier = 2.0 (cooling-off × 2 của riskScore=1.0).

        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 4);
        var config = BuildCafeConfig(ratePerPerson: 10);

        // Act: baseDeposit = 10 × 4 = 40
        //       riskAdjusted = round(40 × 2.0) = 80
        //       finalDeposit = max(0, 80) = 80
        var result = _calculator.Calculate(request, config, cafeBasePrice: 100_000m, walletRiskMultiplier: 2.0m, isCoolingOff: true, isPrivateLobby: false, now: now);

        // Assert
        Assert.Equal(40, result.BaseDeposit);
        Assert.Equal(80, result.FinalDeposit);
        Assert.Equal(2.0m, result.RiskMultiplier);
    }

    [Fact]
    public void Calculate_BR_NEW_01_MinDepositByDistance_OverridesBaseFormula()
    {
        // BR-NEW-01: finalDeposit = max(minDepositByDistance(distance), baseDeposit × riskMultiplier).

        // Arrange: ratePerPerson = 5, maxPlayers = 6 → baseDeposit = 30
        //           SameDay minDeposit = 50 → finalDeposit = max(50, 30) = 50
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 6);
        var config = BuildCafeConfigWithBrNew01Min(minSameDay: 50);

        // Act
        var result = _calculator.Calculate(request, config, cafeBasePrice: 100_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now);

        // Assert: baseDeposit = 30 nhưng finalDeposit lấy minDepositByDistance = 50
        Assert.Equal(30, result.BaseDeposit);
        Assert.Equal(50, result.MinDepositApplied);
        Assert.Equal(50, result.FinalDeposit);
    }

    [Fact]
    public void Calculate_BR_DEPOSIT_03_RatePerPerson_BelowMin_ClampedToMin()
    {
        // BR-DEPOSIT-03: DepositRatePerPerson ∈ [MinDepositRatePerPerson=1, MaxDepositRatePerPerson=100] BVC/người.

        // Arrange: ratePerPerson = 0 (dưới min=1), maxPlayers = 4
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 4);
        var config = BuildCafeConfig(ratePerPerson: 0);

        // Act: perPersonBvc = clamp(0, 1, 100) = 1
        //       baseDeposit = 1 × 4 = 4
        //       finalDeposit = max(0, 4) = 4
        var result = _calculator.Calculate(request, config, cafeBasePrice: 100_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now);

        // Assert
        Assert.Equal(1, result.DepositPerPerson);
        Assert.Equal(4, result.BaseDeposit);
        Assert.Equal(4, result.FinalDeposit);
    }

    [Fact]
    public void Calculate_BR_DEPOSIT_03_RatePerPerson_AboveMax_ClampedToMax()
    {
        // BR-DEPOSIT-03: DepositRatePerPerson ≤ 100 BVC/người.

        // Arrange: ratePerPerson = 200 (trên max=100), maxPlayers = 4
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 4);
        var config = BuildCafeConfig(ratePerPerson: 200);

        // Act: cafeBasePrice = 1,000,000 → BR-03 cap = floor(500,000/1000) = 500 (no cap on perPersonBvc=100).
        //       perPersonBvc = clamp(200, 1, 100) = 100
        //       baseDeposit = 100 × 4 = 400
        //       finalDeposit = max(0, 400) = 400
        var result = _calculator.Calculate(request, config, cafeBasePrice: 1_000_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now);

        // Assert
        Assert.Equal(100, result.DepositPerPerson);
        Assert.Equal(400, result.BaseDeposit);
        Assert.Equal(400, result.FinalDeposit);
    }

    [Fact]
    public void Calculate_BR_03_Cap_PerPersonBvcCappedAt50PercentOfBasePrice()
    {
        // BR-03: "Phí đặt cọc ≤ 50% × Mức phí giờ đầu (hoặc Giá vé vào cổng)".
        // Implementation: perPersonBvc = min(perPersonBvc, floor(50% × cafeBasePrice / 1000)).

        // Arrange: ratePerPerson = 50, cafeBasePrice = 30,000 VND
        //           BR-03 cap = floor(30,000 × 0.5 / 1000) = 15 BVC
        //           perPersonBvc = min(50, 15) = 15 (bị BR-03 cap)
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 4);
        var config = BuildCafeConfig(ratePerPerson: 50);

        // Act
        var result = _calculator.Calculate(request, config, cafeBasePrice: 30_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now);

        // Assert
        Assert.Equal(15, result.DepositPerPerson);
        Assert.Equal(60, result.BaseDeposit); // 15 × 4
        Assert.Equal(60, result.FinalDeposit);
    }

    [Fact]
    public void Calculate_BR_DEPOSIT_02_DifferentMaxPlayers_ReturnsCorrectFinalDeposit()
    {
        // BR-DEPOSIT-02: finalDeposit scales linearly với maxPlayers khi ratePerPerson & riskMultiplier cố định.

        // Arrange: ratePerPerson = 10 (cafe config)
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var config = BuildCafeConfig(ratePerPerson: 10);

        // BR-03 cap = floor(100,000 × 0.5 / 1000) = 50 BVC (no cap on ratePerPerson=10).
        // baseDeposit = 10 × maxPlayers.
        // finalDeposit = max(0, 10 × maxPlayers) = 10 × maxPlayers (no minDeposit, no risk).
        Assert.Equal(10, _calculator.Calculate(BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 1, maxPlayers: 1), config, cafeBasePrice: 100_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now).FinalDeposit);
        Assert.Equal(20, _calculator.Calculate(BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 1, maxPlayers: 2), config, cafeBasePrice: 100_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now).FinalDeposit);
        Assert.Equal(50, _calculator.Calculate(BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 2, maxPlayers: 5), config, cafeBasePrice: 100_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now).FinalDeposit);
        Assert.Equal(80, _calculator.Calculate(BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 2, maxPlayers: 8), config, cafeBasePrice: 100_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now).FinalDeposit);
        Assert.Equal(100, _calculator.Calculate(BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 2, maxPlayers: 10), config, cafeBasePrice: 100_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now).FinalDeposit);
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
        var result = _calculator.Calculate(request, config, cafeBasePrice: 100_000m, 1.0m, false, false, now);

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
        // Arrange: now = 16:00, preferredStart = 18:00
        // recruitmentDeadline = 18:00 - 20min = 17:40 → buffer = 100 phút (>= 60 → warning)
        var now = new DateTime(2026, 8, 2, 16, 0, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date);
        var request = BuildRequest(playDate, maxPlayers: 6);
        var config = BuildCafeConfig(ratePerPerson: 1);

        // Act
        var result = _calculator.Calculate(request, config, cafeBasePrice: 100_000m, 1.0m, false, false, now);

        // Assert
        Assert.Equal(100, result.BufferMinutes);
        Assert.True(result.BufferWarning); // >= 60 → warning
    }

    [Fact]
    public void Calculate_BufferUnder1Hour_Rejects()
    {
        // Arrange: now = 17:30, preferredStart = 18:00
        // recruitmentDeadline = 18:00 - 20min = 17:40 → buffer = 10 phút (< 60 → reject/warning false)
        var now = new DateTime(2026, 8, 2, 17, 30, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date);
        var request = BuildRequest(playDate, maxPlayers: 6);
        var config = BuildCafeConfig(ratePerPerson: 1);

        // Act
        var result = _calculator.Calculate(request, config, cafeBasePrice: 100_000m, 1.0m, false, false, now);

        // Assert
        Assert.Equal(10, result.BufferMinutes);
        Assert.False(result.BufferWarning); // < 60 → reject, no warning
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
            _calculator.Calculate(request, config, cafeBasePrice: 100_000m, 1.0m, false, false, now));
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
            _calculator.Calculate(request, config, cafeBasePrice: 100_000m, 1.0m, false, false, now));
    }

    [Fact]
    public void Calculate_MinPlayersBelow1_ThrowsArgumentException()
    {
        // Arrange - Solo play (MinPlayers = 1) được phép, nhưng MinPlayers < 1 thì không
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 0, maxPlayers: 4);
        var config = BuildCafeConfig();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _calculator.Calculate(request, config, cafeBasePrice: 100_000m, 1.0m, false, false, now));
    }

    [Fact]
    public void Calculate_SoloPlay_MinPlayers1_IsAllowed()
    {
        // Arrange - Solo play (MinPlayers = 1) được phép theo business rule mới
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 1, maxPlayers: 4);
        var config = BuildCafeConfig();

        // Act - Không throw exception
        var result = _calculator.Calculate(request, config, cafeBasePrice: 100_000m, 1.0m, false, false, now);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.FinalDeposit > 0);
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
            _calculator.Calculate(request, config, cafeBasePrice: 100_000m, 1.0m, false, false, now));
    }

    // ===== BUG REPRO: deadline calc dùng DefaultLeadTimeMinutes=20 hard-coded =====

    [Fact]
    public void Calculate_BufferMinutes_MatchesDefaultLeadTimeMinutes()
    {
        // Arrange: now=10:00 today, preferredStart=06:00 tomorrow
        // scheduledTime = tomorrow 06:00
        // deadline = 06:00 - 20 = 05:40 tomorrow → buffer = 19h40m = 1180 phút
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date).AddDays(1);
        var request = new ReservationQuoteRequestDto
        {
            CafeId = Guid.NewGuid(),
            GameId = Guid.NewGuid(),
            PlayDate = playDate,
            PreferredStartTime = new TimeOnly(6, 0), // Morning start
            PreferredEndTime = new TimeOnly(10, 0),
            MinPlayers = 2,
            MaxPlayers = 6,
            IdempotencyKey = $"quote-{Guid.NewGuid():N}"
        };
        var config = BuildCafeConfig();

        // Act
        var result = _calculator.Calculate(request, config, cafeBasePrice: 100_000m, 1.0m, false, false, now);

        // Assert
        Assert.Equal(1180, result.BufferMinutes); // 19h40m
        Assert.False(result.BufferWarning);
    }

    [Fact]
    public void Calculate_BufferMinutes_10_BelowThreshold_NoWarning()
    {
        // For 10p test: now=16:30, preferredStart=17:00 → deadline = 17:00 - 20 = 16:40
        // buffer = 16:40 - 16:30 = 10p
        var now = new DateTime(2026, 8, 2, 16, 30, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date);
        var request = new ReservationQuoteRequestDto
        {
            CafeId = Guid.NewGuid(),
            GameId = Guid.NewGuid(),
            PlayDate = playDate,
            PreferredStartTime = new TimeOnly(17, 0), // Evening start
            PreferredEndTime = new TimeOnly(21, 0),
            MinPlayers = 2,
            MaxPlayers = 6,
            IdempotencyKey = $"quote-{Guid.NewGuid():N}"
        };
        var config = BuildCafeConfig();

        // Act
        var result = _calculator.Calculate(request, config, cafeBasePrice: 100_000m, 1.0m, false, false, now);

        // Assert
        Assert.Equal(10, result.BufferMinutes);
        Assert.False(result.BufferWarning); // < 60 → reject, không warning
    }

    [Fact]
    public void Calculate_BufferMinutes_70_RaisesWarning()
    {
        // Arrange: now=4:30, preferredStart=06:00 same day
        // deadline = 06:00 - 20 = 05:40 → buffer = 70 phút
        var now = new DateTime(2026, 8, 2, 4, 30, 0, DateTimeKind.Utc);
        var playDate = DateOnly.FromDateTime(now.Date);
        var request = new ReservationQuoteRequestDto
        {
            CafeId = Guid.NewGuid(),
            GameId = Guid.NewGuid(),
            PlayDate = playDate,
            PreferredStartTime = new TimeOnly(6, 0), // Morning start
            PreferredEndTime = new TimeOnly(10, 0),
            MinPlayers = 2,
            MaxPlayers = 6,
            IdempotencyKey = $"quote-{Guid.NewGuid():N}"
        };
        var config = BuildCafeConfig();

        // Act
        var result = _calculator.Calculate(request, config, cafeBasePrice: 100_000m, 1.0m, false, false, now);

        // Assert
        Assert.Equal(70, result.BufferMinutes);
        Assert.True(result.BufferWarning); // 60 ≤ 70 < 120 → warning
    }
}
