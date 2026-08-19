using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;
using Xunit;

namespace BoardVerse.Tests.Helpers;

/// <summary>
/// Phase 5 / EC-11 (BR-REFUND-07 §time-slot-fixed-end v3.0):
/// Unit tests cho <see cref="ActiveSessionBillingCalculator"/>.
/// Tập trung vào pure math (không cần DB, không cần mock).
///
/// Cover:
/// - FlatEntry: subtotal = BasePrice cho mọi elapsed &gt; 0.
/// - TimeBased ≤ 60 min: subtotal = BasePrice (giờ đầu).
/// - TimeBased &gt; 60 min: BasePrice + ⌈(elapsed - 60) / block⌉ × blockRate.
/// - Defensive: TieredBlockRate null/invalid → fallback về BasePrice.
/// - Edge cases: elapsed = 0, blockMinutes default 30.
/// </summary>
public class ActiveSessionBillingCalculatorTests
{
    private static Cafe CreateTimeBasedCafe(decimal basePrice = 60000m, int blockMinutes = 15, decimal? blockRate = 5000m)
    {
        return new Cafe
        {
            Id = Guid.NewGuid(),
            Name = "Test Cafe TimeBased",
            Address = "123 Test St",
            BillingModel = CafePartnerBillingModel.TimeBased,
            BasePrice = basePrice,
            TieredBlockMinutes = blockMinutes,
            TieredBlockRate = blockRate
        };
    }

    private static Cafe CreateFlatEntryCafe(decimal basePrice = 80000m)
    {
        return new Cafe
        {
            Id = Guid.NewGuid(),
            Name = "Test Cafe FlatEntry",
            Address = "456 Test St",
            BillingModel = CafePartnerBillingModel.FlatEntry,
            BasePrice = basePrice
        };
    }

    // ===== TimeBased billing =====

    [Fact]
    public void CalculateRealtimeBilling_Should_ReturnBasePrice_When_TimeBasedAndAt60Minutes()
    {
        // Arrange: giờ đầu chính xác 60 phút.
        var cafe = CreateTimeBasedCafe(basePrice: 60000m);

        // Act
        var subtotal = ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, 60);

        // Assert
        Assert.Equal(60000m, subtotal);
    }

    [Fact]
    public void CalculateRealtimeBilling_Should_ReturnBasePrice_When_TimeBasedAndBelow60Minutes()
    {
        // Arrange: 45 phút → còn trong giờ đầu.
        var cafe = CreateTimeBasedCafe(basePrice: 60000m);

        // Act
        var subtotal = ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, 45);

        // Assert
        Assert.Equal(60000m, subtotal);
    }

    [Fact]
    public void CalculateRealtimeBilling_Should_AddOneBlock_When_TimeBasedAndJustOver60Minutes()
    {
        // Arrange: 61 phút → 1 phút vượt → 1 block lũy tiến.
        var cafe = CreateTimeBasedCafe(basePrice: 60000m, blockMinutes: 15, blockRate: 5000m);

        // Act
        var subtotal = ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, 61);

        // Assert
        // Math.Ceiling(1 / 15) = 1 → 60000 + 1*5000 = 65000
        Assert.Equal(65000m, subtotal);
    }

    [Fact]
    public void CalculateRealtimeBilling_Should_AddFourBlocks_When_TimeBasedAndAt120Minutes()
    {
        // Arrange: 120 phút → 60 vượt / 15 block = 4 blocks.
        var cafe = CreateTimeBasedCafe(basePrice: 60000m, blockMinutes: 15, blockRate: 5000m);

        // Act
        var subtotal = ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, 120);

        // Assert
        // Math.Ceiling(60 / 15) = 4 → 60000 + 4*5000 = 80000
        Assert.Equal(80000m, subtotal);
    }

    [Fact]
    public void CalculateRealtimeBilling_Should_RoundUpPartialBlock_When_TimeBased()
    {
        // Arrange: 76 phút → 16 vượt → Math.Ceiling(16/15) = 2 blocks.
        var cafe = CreateTimeBasedCafe(basePrice: 60000m, blockMinutes: 15, blockRate: 5000m);

        // Act
        var subtotal = ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, 76);

        // Assert
        Assert.Equal(70000m, subtotal); // 60000 + 2*5000
    }

    [Fact]
    public void CalculateRealtimeBilling_Should_FallbackToBasePrice_When_TieredBlockRateIsNull()
    {
        // Arrange: TieredBlockRate null → fallback an toàn.
        var cafe = CreateTimeBasedCafe(basePrice: 60000m, blockMinutes: 15, blockRate: null);

        // Act
        var subtotal = ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, 120);

        // Assert: chỉ giờ đầu = 60000.
        Assert.Equal(60000m, subtotal);
    }

    [Fact]
    public void CalculateRealtimeBilling_Should_FallbackToBasePrice_When_TieredBlockRateIsZero()
    {
        // Arrange: TieredBlockRate = 0 → invalid.
        var cafe = CreateTimeBasedCafe(basePrice: 60000m, blockMinutes: 15, blockRate: 0m);

        // Act
        var subtotal = ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, 120);

        // Assert
        Assert.Equal(60000m, subtotal);
    }

    [Fact]
    public void CalculateRealtimeBilling_Should_UseDefaultBlock30Minutes_When_BlockMinutesIsZero()
    {
        // Arrange: TieredBlockMinutes = 0 (invalid) → fallback 30 phút.
        var cafe = CreateTimeBasedCafe(basePrice: 60000m, blockMinutes: 0, blockRate: 5000m);

        // Act: 91 phút → 31 vượt → Math.Ceiling(31/30) = 2 blocks.
        var subtotal = ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, 91);

        // Assert
        Assert.Equal(70000m, subtotal); // 60000 + 2*5000
    }

    // ===== FlatEntry billing =====

    [Fact]
    public void CalculateRealtimeBilling_Should_ReturnBasePrice_When_FlatEntryAnyDuration()
    {
        // Arrange: FlatEntry chỉ trả 1 lần BasePrice, không quan tâm minutes.
        var cafe = CreateFlatEntryCafe(basePrice: 80000m);

        // Act + Assert: nhiều duration khác nhau đều trả cùng 80000.
        Assert.Equal(80000m, ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, 30));
        Assert.Equal(80000m, ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, 60));
        Assert.Equal(80000m, ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, 240));
        Assert.Equal(80000m, ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, 1440));
    }

    // ===== Edge cases =====

    [Fact]
    public void CalculateRealtimeBilling_Should_ReturnZero_When_ElapsedIsZero()
    {
        // Arrange: cả TimeBased và FlatEntry với elapsed = 0 → 0.
        var timeBasedCafe = CreateTimeBasedCafe();
        var flatEntryCafe = CreateFlatEntryCafe();

        // Act + Assert
        Assert.Equal(0m, ActiveSessionBillingCalculator.CalculateRealtimeBilling(timeBasedCafe, 0));
        Assert.Equal(0m, ActiveSessionBillingCalculator.CalculateRealtimeBilling(flatEntryCafe, 0));
    }

    [Fact]
    public void CalculateRealtimeBilling_Should_ReturnZero_When_ElapsedIsNegative()
    {
        // Arrange: defensive — elapsed < 0 → 0.
        var cafe = CreateTimeBasedCafe();

        // Act
        var subtotal = ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, -10);

        // Assert
        Assert.Equal(0m, subtotal);
    }

    [Fact]
    public void CalculateRealtimeBilling_Should_HandleLongSessions_For_ManagerOverride()
    {
        // Arrange: Manager override scenario — session kéo dài 8 giờ.
        var cafe = CreateTimeBasedCafe(basePrice: 60000m, blockMinutes: 15, blockRate: 5000m);

        // Act: 480 phút → 420 vượt → Math.Ceiling(420/15) = 28 blocks.
        var subtotal = ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, 480);

        // Assert
        Assert.Equal(200000m, subtotal); // 60000 + 28*5000
    }
}
