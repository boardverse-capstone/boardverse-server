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
    public void CalculateRealtimeBilling_Should_ChargeBasePrice_When_ElapsedIsZero_AntiAbuse()
    {
        // Arrange: cả TimeBased và FlatEntry với elapsed = 0 → vẫn trả BasePrice.
        // Lý do: chống player mở bàn rồi nghỉ (BR-09 / §time-slot-fixed-end v3.0 anti-abuse).
        var timeBasedCafe = CreateTimeBasedCafe(basePrice: 60000m);
        var flatEntryCafe = CreateFlatEntryCafe(basePrice: 80000m);

        // Act + Assert
        Assert.Equal(60000m, ActiveSessionBillingCalculator.CalculateRealtimeBilling(timeBasedCafe, 0));
        Assert.Equal(80000m, ActiveSessionBillingCalculator.CalculateRealtimeBilling(flatEntryCafe, 0));
    }

    [Fact]
    public void CalculateRealtimeBilling_Should_Throw_When_ElapsedIsNegative()
    {
        // Arrange: defensive — elapsed < 0 → throw để caller biết tính sai.
        var cafe = CreateTimeBasedCafe();

        // Act + Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, -10));
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

    // ===== Anti-abuse scenarios (chống player mở bàn rồi nghỉ) =====

    [Theory]
    [InlineData(0)]   // Player mở bàn rồi bỏ đi ngay
    [InlineData(1)]   // Chỉ ngồi 1 phút
    [InlineData(5)]   // Chỉ ngồi 5 phút
    public void CalculateRealtimeBilling_Should_ChargeAtLeastBasePrice_For_AbandonedSession(int elapsedMinutes)
    {
        // Arrange: Player mở bàn nhưng rời đi rất sớm → vẫn phải trả giờ đầu.
        // Trước đây helper trả 0 cho elapsed = 0 → bị abuse (mở bàn miễn phí).
        // TimeBased và FlatEntry đều áp dụng rule này (giờ đầu là đơn vị cơ bản).
        var timeBasedCafe = CreateTimeBasedCafe(basePrice: 60000m);
        var flatEntryCafe = CreateFlatEntryCafe(basePrice: 80000m);

        // Act
        var timeBasedSubtotal = ActiveSessionBillingCalculator.CalculateRealtimeBilling(timeBasedCafe, elapsedMinutes);
        var flatEntrySubtotal = ActiveSessionBillingCalculator.CalculateRealtimeBilling(flatEntryCafe, elapsedMinutes);

        // Assert: subtotal >= BasePrice (không bao giờ = 0 cho session opened).
        Assert.True(timeBasedSubtotal >= timeBasedCafe.BasePrice,
            $"TimeBased subtotal {timeBasedSubtotal} phải >= BasePrice {timeBasedCafe.BasePrice} khi elapsed = {elapsedMinutes}");
        Assert.True(flatEntrySubtotal >= flatEntryCafe.BasePrice,
            $"FlatEntry subtotal {flatEntrySubtotal} phải >= BasePrice {flatEntryCafe.BasePrice} khi elapsed = {elapsedMinutes}");
    }

    [Fact]
    public void CalculateRealtimeBilling_Should_NotExceedBasePricePlusBlocks_For_AbandonedSession()
    {
        // Arrange: Player mở bàn, ngồi 30 phút rồi bỏ → chỉ trả giờ đầu, KHÔNG cộng block lũy tiến.
        var cafe = CreateTimeBasedCafe(basePrice: 60000m, blockMinutes: 15, blockRate: 5000m);

        // Act
        var subtotal = ActiveSessionBillingCalculator.CalculateRealtimeBilling(cafe, 30);

        // Assert: elapsed = 30 ≤ 60 → không có block lũy tiến, chỉ giờ đầu.
        Assert.Equal(60000m, subtotal);
    }
}
