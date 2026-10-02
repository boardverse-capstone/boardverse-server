using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Services.Services;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho DepositCalculator — pure function.
/// Công thức 2026-10-02: perPersonBvc = max(1, floor(basePrice × 20% / 1000));
///                       finalDeposit  = perPersonBvc × maxPlayers × riskMultiplier.
/// </summary>
public class DepositCalculatorTests
{
    private readonly DepositCalculator _calculator = new();

    private static CafeConfig BuildCafeConfig(int capacity = 30)
    {
        return new CafeConfig
        {
            CafeId = Guid.NewGuid(),
            Capacity = capacity,
            // 2026-10-02: các field DepositRatePerPerson/Min/Max/MinDeposit*Days đã obsolete.
            // Giữ default values để verify chúng KHÔNG ảnh hưởng finalDeposit nữa.
            DepositRatePerPerson = 999, // intentionally lớn — test confirm KHÔNG dùng
            MinDepositRatePerPerson = 999,
            MaxDepositRatePerPerson = 999,
            MinDepositSameDay = 999,
            MinDeposit1Day = 999,
            MinDeposit2Days = 999,
            MinDeposit3To4Days = 999,
            MinDeposit5To7Days = 999,
            // Mặc định BR-NEW-01 maxPlayers theo khoảng cách playDate (giữ để test BR-NEW-11 + maxPlayers limit).
            MaxPlayersPerLobbySameDay = 30,
            MaxPlayersPerLobby1Day = 20,
            MaxPlayersPerLobby2Days = 15,
            MaxPlayersPerLobby3To4Days = 10,
            MaxPlayersPerLobby5To7Days = 6,
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

    // ===== 2026-10-02 Simplified formula: perPersonBvc = max(1, floor(basePrice × 20% / 1000)); finalDeposit = perPersonBvc × maxPlayers × riskMultiplier =====

    [Fact]
    public void Calculate_UserExample_10kBasePrice_3Players_Risk1_Gives6Bvc()
    {
        // Spec user (2026-10-02):
        //   basePrice = 10.000 VND (= 10k) → 20% × 10.000 = 2.000 VND = 2 BVC/người
        //   maxPlayers = 3
        //   finalDeposit = 2 × 3 × 1.0 = 6 BVC
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 3);
        var config = BuildCafeConfig();

        var result = _calculator.Calculate(request, config, cafeBasePrice: 10_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now);

        Assert.Equal(2, result.DepositPerPerson);   // 20% × 10.000 / 1.000
        Assert.Equal(6, result.BaseDeposit);        // 2 × 3
        Assert.Equal(6, result.FinalDeposit);       // 6 × 1.0
        Assert.Equal(0.20m, result.DepositPercentage);
        Assert.Equal(10_000m, result.CafeBasePriceVnd);
        Assert.Equal(1.0m, result.RiskMultiplier);
    }

    [Fact]
    public void Calculate_BasicFormula_50kBasePrice_4Players_Risk1_Gives40Bvc()
    {
        // basePrice = 50.000 → perPersonBvc = max(1, floor(50.000 × 0.20 / 1000)) = max(1, 10) = 10
        // baseDeposit = 10 × 4 = 40
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 4);
        var config = BuildCafeConfig();

        var result = _calculator.Calculate(request, config, cafeBasePrice: 50_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now);

        Assert.Equal(10, result.DepositPerPerson);
        Assert.Equal(40, result.BaseDeposit);
        Assert.Equal(40, result.FinalDeposit);
    }

    [Fact]
    public void Calculate_BasicFormula_100kBasePrice_6Players_Risk1_Gives120Bvc()
    {
        // basePrice = 100.000 → perPersonBvc = max(1, floor(100.000 × 0.20 / 1000)) = 20
        // baseDeposit = 20 × 6 = 120
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 6);
        var config = BuildCafeConfig();

        var result = _calculator.Calculate(request, config, cafeBasePrice: 100_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now);

        Assert.Equal(20, result.DepositPerPerson);
        Assert.Equal(120, result.BaseDeposit);
        Assert.Equal(120, result.FinalDeposit);
    }

    [Fact]
    public void Calculate_WithRiskMultiplier_AppliesMultiplier()
    {
        // basePrice = 50.000 → perPersonBvc = 10
        // baseDeposit = 10 × 4 = 40
        // riskMultiplier = 1.25 → riskAdjusted = round(40 × 1.25) = 50
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 4);
        var config = BuildCafeConfig();

        var result = _calculator.Calculate(request, config, cafeBasePrice: 50_000m, walletRiskMultiplier: 1.25m, isCoolingOff: false, isPrivateLobby: false, now: now);

        Assert.Equal(10, result.DepositPerPerson);
        Assert.Equal(40, result.BaseDeposit);
        Assert.Equal(50, result.FinalDeposit);
        Assert.Equal(1.25m, result.RiskMultiplier);
    }

    [Fact]
    public void Calculate_WithCoolingOffActive_DoesNotChangeFinalDeposit()
    {
        // 2026-10-02: BR-NEW-10 §XI.2 "cọc ×2 cooling-off" đã bỏ.
        // DepositCalculator không nhân multiplier theo isCoolingOff nữa — multiplier đến từ risk score job.
        // basePrice = 50.000 → perPersonBvc = 10, baseDeposit = 10 × 4 = 40
        // walletRiskMultiplier = 2.0 (giả lập user có risk score cao) → finalDeposit = 40 × 2.0 = 80.
        // Caller truyền isCoolingOff = true không còn làm thay đổi gì.
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 4);
        var config = BuildCafeConfig();

        // Trước fix: walletRiskMultiplier = 2.0m do cooling-off đã pre-multiply → finalDeposit = 80.
        // Sau fix: cooling-off KHÔNG pre-multiply nữa. Caller chỉ truyền walletRiskMultiplier = 1.0 (default).
        var result = _calculator.Calculate(request, config, cafeBasePrice: 50_000m, walletRiskMultiplier: 1.0m, isCoolingOff: true, isPrivateLobby: false, now: now);

        // Cọc giữ nguyên bất kể cooling-off active.
        Assert.Equal(40, result.BaseDeposit);
        Assert.Equal(40, result.FinalDeposit); // baseDeposit × 1.0 = 40
        Assert.Equal(1.0m, result.RiskMultiplier);
    }

    [Fact]
    public void Calculate_BasePriceBelow5kBtc_FloorsPerPersonBvcTo1()
    {
        // basePrice = 4.000 → floor(4.000 × 0.20 / 1000) = floor(0.8) = 0 → max(1, 0) = 1 BVC/người (safety floor)
        // baseDeposit = 1 × 4 = 4
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 4);
        var config = BuildCafeConfig();

        var result = _calculator.Calculate(request, config, cafeBasePrice: 4_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now);

        Assert.Equal(1, result.DepositPerPerson);
        Assert.Equal(4, result.BaseDeposit);
        Assert.Equal(4, result.FinalDeposit);
    }

    [Fact]
    public void Calculate_BasePriceZeroOrNegative_FallsBackToMinPerPersonBvc1()
    {
        // basePrice = 0 → perPersonBvc raw = 0 → max(1, 0) = 1 (safety floor)
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 4);
        var config = BuildCafeConfig();

        var result = _calculator.Calculate(request, config, cafeBasePrice: 0m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now);

        Assert.Equal(1, result.DepositPerPerson);
        Assert.Equal(4, result.BaseDeposit);
        Assert.Equal(4, result.FinalDeposit);
    }

    [Fact]
    public void Calculate_DifferentMaxPlayers_ReturnsCorrectFinalDeposit()
    {
        // basePrice = 50.000 → perPersonBvc = 10
        // baseDeposit = 10 × maxPlayers, finalDeposit = baseDeposit × 1.0
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var config = BuildCafeConfig();

        Assert.Equal(10, _calculator.Calculate(BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 1, maxPlayers: 1), config, cafeBasePrice: 50_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now).FinalDeposit);
        Assert.Equal(20, _calculator.Calculate(BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 1, maxPlayers: 2), config, cafeBasePrice: 50_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now).FinalDeposit);
        Assert.Equal(50, _calculator.Calculate(BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 2, maxPlayers: 5), config, cafeBasePrice: 50_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now).FinalDeposit);
        Assert.Equal(80, _calculator.Calculate(BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 2, maxPlayers: 8), config, cafeBasePrice: 50_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now).FinalDeposit);
        Assert.Equal(100, _calculator.Calculate(BuildRequest(DateOnly.FromDateTime(now.Date), minPlayers: 2, maxPlayers: 10), config, cafeBasePrice: 50_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now).FinalDeposit);
    }

    [Fact]
    public void Calculate_ObsoleteFields_CafeConfig_DoNotAffectFinalDeposit()
    {
        // 2026-10-02: CafeConfig.DepositRatePerPerson (đã obsolete) KHÔNG ảnh hưởng finalDeposit nữa.
        // Test này verify: dù DepositRatePerPerson = 999 (intentionally lớn), finalDeposit chỉ phụ thuộc basePrice.
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 4);
        var config = BuildCafeConfig();
        // config.DepositRatePerPerson = 999 (set trong BuildCafeConfig)

        var result = _calculator.Calculate(request, config, cafeBasePrice: 20_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now);

        // basePrice 20.000 → 20% = 4.000 / 1.000 = 4 BVC/người. KHÔNG bị ảnh hưởng bởi DepositRatePerPerson=999.
        Assert.Equal(4, result.DepositPerPerson);
        Assert.Equal(16, result.BaseDeposit);
        Assert.Equal(16, result.FinalDeposit);
    }

    [Fact]
    public void Calculate_ObsoleteFields_MinDepositByDistance_DoNotAffectFinalDeposit()
    {
        // 2026-10-02: BR-NEW-01 minDepositByDistance floor đã bỏ.
        // CafeConfig.MinDepositSameDay = 999 (set trong BuildCafeConfig) KHÔNG override finalDeposit nữa.
        var now = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var request = BuildRequest(DateOnly.FromDateTime(now.Date), maxPlayers: 4);
        var config = BuildCafeConfig();
        // config.MinDepositSameDay = 999

        var result = _calculator.Calculate(request, config, cafeBasePrice: 10_000m, walletRiskMultiplier: 1.0m, isCoolingOff: false, isPrivateLobby: false, now: now);

        // basePrice 10.000 → perPersonBvc = 2, baseDeposit = 8. KHÔNG bị override đẩy lên 999.
        Assert.Equal(2, result.DepositPerPerson);
        Assert.Equal(8, result.BaseDeposit);
        Assert.Equal(8, result.FinalDeposit);
        Assert.Equal(0, result.MinDepositApplied); // Field luôn = 0 sau khi bỏ floor.
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
        var config = BuildCafeConfig();

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
        var config = BuildCafeConfig();

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
        var config = BuildCafeConfig();

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
        var config = BuildCafeConfig();
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
