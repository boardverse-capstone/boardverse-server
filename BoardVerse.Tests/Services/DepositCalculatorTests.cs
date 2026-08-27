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

    // ===== BR-DEPOSIT-02 (2026-08-27): baseDeposit (BVC/người) = 20% × cafeBasePrice → BVC; finalDeposit = baseDeposit × maxPlayers =====

    [Fact]
    public void Calculate_SameDay_Returns20PercentOfBasePricePerPerson()
    {
        // Arrange: cafeBasePrice = 100,000 VND → 20% = 20,000 VND → 20 BVC/người
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 6);
        var config = BuildCafeConfig(ratePerPerson: 5);

        // Act: cafeBasePrice = 100,000 VND, maxPlayers = 6
        // depositVndPerPerson = 20,000 VND → baseDeposit = 20 BVC/người
        // finalDeposit = 20 × 6 = 120 BVC
        var result = _calculator.Calculate(request, config, cafeBasePrice: 100_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now);

        // Assert: new formula 2026-08-27 — baseDeposit per person, finalDeposit = baseDeposit × maxPlayers
        // [2026-08-27] DepositPerPerson/BaseDeposit không còn được set trong result (deprecated, default = 0).
        // Chỉ assert FinalDeposit là đủ (FE chỉ render FinalDeposit).
        Assert.Equal(120, result.FinalDeposit); // 20 × 6
        Assert.Equal(DistanceBucket.SameDay, result.Distance);
        Assert.Equal(0, result.MinDepositApplied); // deprecated
    }

    [Fact]
    public void Calculate_SmallBasePrice_FloorsPerPersonToMin1Bvc()
    {
        // Arrange: cafeBasePrice = 2,000 VND → 20% = 400 VND → 0.4 BVC → floor 1 BVC/người
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 6);
        var config = BuildCafeConfig();

        // Act: depositVndPerPerson = 400 VND → 0.4 BVC → floor 1 BVC
        // finalDeposit = 1 × 6 = 6 BVC
        var result = _calculator.Calculate(request, config, cafeBasePrice: 2_000m, 1.0m, false, false, now);

        // Assert: Math.Max(1, RoundToBvc(400/1000)) = Math.Max(1, 0) = 1 BVC/người
        // [2026-08-27] DepositPerPerson deprecated, không assert. FE chỉ render FinalDeposit.
        Assert.Equal(6, result.FinalDeposit); // 1 × 6
    }

    [Fact]
    public void Calculate_VariousBasePrices_ConvertsCorrectly()
    {
        // Arrange
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 4);
        var config = BuildCafeConfig();

        // Act & Assert
        // cafeBasePrice = 50,000 VND → 20% = 10,000 VND → 10 BVC/người
        // finalDeposit = 10 × 4 = 40 BVC
        var r50 = _calculator.Calculate(request, config, cafeBasePrice: 50_000m, 1.0m, false, false, now);
        Assert.Equal(40, r50.FinalDeposit);

        // cafeBasePrice = 150,000 VND → 20% = 30,000 VND → 30 BVC/người
        // finalDeposit = 30 × 4 = 120 BVC
        var r150 = _calculator.Calculate(request, config, cafeBasePrice: 150_000m, 1.0m, false, false, now);
        Assert.Equal(120, r150.FinalDeposit);

        // cafeBasePrice = 200,000 VND → 20% = 40,000 VND → 40 BVC/người
        // finalDeposit = 40 × 4 = 160 BVC
        var r200 = _calculator.Calculate(request, config, cafeBasePrice: 200_000m, 1.0m, false, false, now);
        Assert.Equal(160, r200.FinalDeposit);
    }

    [Fact]
    public void Calculate_DifferentMaxPlayers_ReturnsCorrectFinalDeposit()
    {
        // Arrange: cafeBasePrice = 50,000 VND → 20% = 10 BVC/người
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var config = BuildCafeConfig();

        // Act & Assert: cùng cafeBasePrice, maxPlayers khác nhau cho finalDeposit khác nhau
        Assert.Equal(10, _calculator.Calculate(BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 1, maxPlayers: 1), config, cafeBasePrice: 50_000m, 1.0m, false, false, now).FinalDeposit);
        Assert.Equal(20, _calculator.Calculate(BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 1, maxPlayers: 2), config, cafeBasePrice: 50_000m, 1.0m, false, false, now).FinalDeposit);
        Assert.Equal(50, _calculator.Calculate(BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 2, maxPlayers: 5), config, cafeBasePrice: 50_000m, 1.0m, false, false, now).FinalDeposit);
        Assert.Equal(80, _calculator.Calculate(BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 2, maxPlayers: 8), config, cafeBasePrice: 50_000m, 1.0m, false, false, now).FinalDeposit);
        Assert.Equal(100, _calculator.Calculate(BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 2, maxPlayers: 10), config, cafeBasePrice: 50_000m, 1.0m, false, false, now).FinalDeposit);
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
