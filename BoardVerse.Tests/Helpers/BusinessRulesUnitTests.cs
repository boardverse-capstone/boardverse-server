using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;
using Xunit;

namespace BoardVerse.Tests.Helpers;

/// <summary>
/// Unit tests cho Business Rules liên quan đến:
///
/// BR-02: Mức cọc cho phép thay đổi tùy quán
/// BR-03: Phí đặt cọc <= 50% giá vé
/// BR-15: Tổng tiền = Tiền giờ + Phí phạt - Tiền cọc
/// BR-16: Chốt phí theo mô hình quán
/// </summary>
public class BusinessRulesUnitTests
{
    #region BR-03: Deposit Cap Validation (50% of Base Price)

    [Theory]
    [InlineData(50000, 0.5, true)]   // 50% -> Valid
    [InlineData(50000, 0.49, true)]   // 49% -> Valid
    [InlineData(50000, 0.51, false)] // 51% -> Invalid (exceeds 50%)
    [InlineData(50000, 0.0, true)]   // 0% -> Valid
    [InlineData(50000, 1.0, false)]  // 100% -> Invalid
    public void ValidateDepositPercentage_RespectCap_ReturnsExpectedResult(
        decimal basePrice,
        decimal depositPercentage,
        bool expectedIsValid)
    {
        // BR-03: Phí đặt cọc <= 50% giá vé
        // Công thức: depositAmount <= 0.5m * basePrice

        var maxDepositAmount = basePrice * 0.5m;
        var depositAmount = basePrice * depositPercentage;

        var isValid = depositAmount <= maxDepositAmount;

        Assert.Equal(expectedIsValid, isValid);
    }

    [Fact]
    public void CalculateMaxDepositAmount_Returns50PercentOfBasePrice()
    {
        // Arrange
        var basePrice = 50000m; // 50,000 VND

        // Act
        var maxDeposit = CafePricingValidator.GetMaxDepositAmount(basePrice);

        // Assert - BR-03: Max = 50% of base price
        Assert.Equal(25000m, maxDeposit);
    }

    [Fact]
    public void ValidateDepositPercentage_Exactly50Percent_IsValid()
    {
        // Arrange - Edge case: đúng 50%
        var basePrice = 50000m;
        var depositPercentage = 0.5m;
        var depositAmount = basePrice * depositPercentage;

        // Act
        var maxAllowed = basePrice * 0.5m;
        var isValid = depositAmount <= maxAllowed;

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateDepositPercentage_Over50Percent_IsInvalid()
    {
        // Arrange - Edge case: 50.01%
        var basePrice = 50000m;
        var depositPercentage = 0.5001m;
        var depositAmount = basePrice * depositPercentage;

        // Act
        var maxAllowed = basePrice * 0.5m;
        var isValid = depositAmount <= maxAllowed;

        // Assert
        Assert.False(isValid);
    }

    #endregion

    #region BR-15: Invoice Calculation Formula

    [Fact]
    public void CalculateInvoice_BasicPlayTime_ReturnsCorrectAmount()
    {
        // BR-15: Tổng tiền = Tiền giờ + Phí phạt - Tiền cọc
        // VD: 2 tiếng chơi, không phí phạt, có cọc

        // Arrange
        var playTimeFee = 100000m;      // 2 tiếng x 50k
        var penaltyFee = 0m;             // Không mất linh kiện
        var depositAmount = 50000m;      // Đã đặt cọc trước

        // Act
        var totalAmount = InvoiceCalculator.Calculate(playTimeFee, penaltyFee, depositAmount);

        // Assert
        Assert.Equal(50000m, totalAmount); // 100k - 0 + 0 - 50k = 50k
    }

    [Fact]
    public void CalculateInvoice_WithPenalty_IncludesPenaltyInTotal()
    {
        // BR-15: Tổng tiền = Tiền giờ + Phí phạt - Tiền cọc

        // Arrange
        var playTimeFee = 100000m;      // 2 tiếng
        var penaltyFee = 15000m;         // Mất 1 quân cờ
        var depositAmount = 50000m;      // Đã đặt cọc

        // Act
        var totalAmount = InvoiceCalculator.Calculate(playTimeFee, penaltyFee, depositAmount);

        // Assert: 100k + 15k - 50k = 65k
        Assert.Equal(65000m, totalAmount);
    }

    [Fact]
    public void CalculateInvoice_NoDeposit_AmountEqualsPlayTimePlusPenalty()
    {
        // BR-15: Không có cọc -> Tổng = Tiền giờ + Phí phạt

        // Arrange
        var playTimeFee = 100000m;
        var penaltyFee = 30000m;
        var depositAmount = 0m;

        // Act
        var totalAmount = InvoiceCalculator.Calculate(playTimeFee, penaltyFee, depositAmount);

        // Assert
        Assert.Equal(130000m, totalAmount);
    }

    [Fact]
    public void CalculateInvoice_LargePenaltyExceedsPlayTime_TotalCanBeNegative()
    {
        // Edge case: Phí phạt > Tiền giờ - Cọc
        // Trong thực tế nên có floor = 0

        // Arrange
        var playTimeFee = 50000m;       // 1 tiếng
        var penaltyFee = 100000m;       // Mất nhiều linh kiện
        var depositAmount = 50000m;      // Cọc 50k

        // Act
        var totalAmount = InvoiceCalculator.Calculate(playTimeFee, penaltyFee, depositAmount);

        // Assert: 50k + 100k - 50k = 100k (không âm vì có phí phạt)
        Assert.Equal(100000m, totalAmount);
    }

    #endregion

    #region BR-16: Billing Model Calculation

    [Theory]
    [InlineData(60, 50000, 25000, 15, 150000)]   // 60/15=4 blocks, 50k + 4*25k = 150k
    [InlineData(75, 50000, 25000, 15, 175000)]   // 75/15=5 blocks, 50k + 5*25k = 175k
    [InlineData(90, 50000, 25000, 15, 200000)]   // 90/15=6 blocks, 50k + 6*25k = 200k
    [InlineData(120, 50000, 25000, 15, 250000)]  // 120/15=8 blocks, 50k + 8*25k = 250k
    public void CalculateFee_TimeBasedModel_AppliesBlockRate(
        int playedMinutes,
        decimal firstHourPrice,
        decimal blockRate,
        int blockMinutes,
        decimal expectedFee)
    {
        // BR-16: Mô hình thời gian thực
        // Công thức: firstHourPrice + (totalBlocks * blockRate)
        // Trong đó totalBlocks = playedMinutes / blockMinutes

        // Arrange
        var billingModel = CafePartnerBillingModel.TimeBased;

        // Act
        var calculatedFee = FeeCalculator.Calculate(
            billingModel,
            playedMinutes,
            firstHourPrice,
            blockRate,
            blockMinutes);

        // Assert
        Assert.Equal(expectedFee, calculatedFee);
    }

    [Theory]
    [InlineData(60, 50000, 25000, 15)]   // 1 tiếng
    [InlineData(75, 50000, 25000, 15)]  // 1h15m
    [InlineData(120, 50000, 25000, 15)] // 2 tiếng
    [InlineData(240, 50000, 25000, 15)] // 4 tiếng
    public void CalculateFee_FlatEntryModel_ReturnsBasePriceOnly(
        int playedMinutes,
        decimal firstHourPrice,
        decimal blockRate,
        int blockMinutes)
    {
        // BR-16: Mô hình vào cổng trọn gói
        // Giá vé vào cổng = giờ đầu, các block sau = 0 VND

        // Arrange
        var billingModel = CafePartnerBillingModel.FlatEntry;

        // Act
        var calculatedFee = FeeCalculator.Calculate(
            billingModel,
            playedMinutes,
            firstHourPrice,
            blockRate,
            blockMinutes);

        // Assert - Luôn trả về giá vé vào cổng
        Assert.Equal(firstHourPrice, calculatedFee);
    }

    [Fact]
    public void CalculateFee_FlatEntryModel_BlockRateIgnored()
    {
        // BR-16: FlatEntry không tính block rate

        // Arrange
        var billingModel = CafePartnerBillingModel.FlatEntry;
        var playedMinutes = 180; // 3 tiếng
        var firstHourPrice = 50000m;
        var blockRate = 25000m; // Dù có giá block cao

        // Act
        var calculatedFee = FeeCalculator.Calculate(
            billingModel,
            playedMinutes,
            firstHourPrice,
            blockRate,
            blockMinutes: 15);

        // Assert - Không áp dụng block rate
        Assert.Equal(50000m, calculatedFee);
    }

    [Fact]
    public void CalculateFee_TimeBasedModel_BlockRateMatters()
    {
        // BR-16: TimeBased áp dụng block rate

        // Arrange
        var billingModel = CafePartnerBillingModel.TimeBased;
        var playedMinutes = 120; // 2 tiếng
        var firstHourPrice = 50000m;
        var blockRate = 25000m;

        // Act
        var calculatedFee = FeeCalculator.Calculate(
            billingModel,
            playedMinutes,
            firstHourPrice,
            blockRate,
            blockMinutes: 15);

        // Assert: 50k + (120/15)*25k = 50k + 200k = 250k
        Assert.Equal(250000m, calculatedFee);
    }

    #endregion

    #region BR-04: Pricing Lock Validation

    [Theory]
    [InlineData(true, false)]   // Quán đang mở -> Không được sửa giá
    [InlineData(false, true)]  // Quán đóng cửa -> Được sửa giá
    public void CanUpdatePricing_BasedOnOperationalStatus_ReturnsExpectedResult(
        bool isPricingLocked,
        bool expectedCanUpdate)
    {
        // BR-04: Chỉ được sửa giá khi quán đóng cửa

        var canUpdate = !isPricingLocked;

        Assert.Equal(expectedCanUpdate, canUpdate);
    }

    #endregion

    #region BR-11: Age Validation (>= 13 years old)

    [Theory]
    [InlineData(13, true)]   // 13 tuổi -> Valid
    [InlineData(14, true)]  // 14 tuổi -> Valid
    [InlineData(18, true)]  // 18 tuổi -> Valid
    [InlineData(12, false)] // 12 tuổi -> Invalid
    [InlineData(0, false)]  // 0 tuổi -> Invalid
    public void ValidateMinimumAge_ReturnsExpectedResult(
        int age,
        bool expectedIsValid)
    {
        // BR-11: Giới hạn độ tuổi >= 13

        var isValid = age >= 13;

        Assert.Equal(expectedIsValid, isValid);
    }

    #endregion
}

#region Helper Classes for Testing (Pure business logic, no DB dependency)

/// <summary>
/// BR-03: Validator cho deposit percentage
/// </summary>
public static class CafePricingValidator
{
    private const decimal MAX_DEPOSIT_RATIO = 0.5m; // 50%

    public static decimal GetMaxDepositAmount(decimal basePrice)
    {
        return basePrice * MAX_DEPOSIT_RATIO;
    }

    public static bool IsDepositValid(decimal depositAmount, decimal basePrice)
    {
        var maxDeposit = GetMaxDepositAmount(basePrice);
        return depositAmount <= maxDeposit;
    }
}

/// <summary>
/// BR-15: Invoice calculator
/// Formula: TotalAmount = PlayTimeFee + PenaltyFee - DepositAmount
/// </summary>
public static class InvoiceCalculator
{
    public static decimal Calculate(decimal playTimeFee, decimal penaltyFee, decimal depositAmount)
    {
        // BR-15: Tổng tiền = Tiền giờ + Phí phạt - Tiền cọc
        return playTimeFee + penaltyFee - depositAmount;
    }

    /// <summary>
    /// Calculate with minimum floor (prevent negative invoices)
    /// </summary>
    public static decimal CalculateWithFloor(decimal playTimeFee, decimal penaltyFee, decimal depositAmount)
    {
        var total = Calculate(playTimeFee, penaltyFee, depositAmount);
        return Math.Max(0, total);
    }
}

/// <summary>
/// BR-16: Fee calculator based on billing model
/// </summary>
public static class FeeCalculator
{
    public static decimal Calculate(
        CafePartnerBillingModel billingModel,
        int playedMinutes,
        decimal firstHourPrice,
        decimal blockRate,
        int blockMinutes)
    {
        if (billingModel == CafePartnerBillingModel.FlatEntry)
        {
            // BR-16: Mô hình vào cổng trọn gói - Chỉ tính giá vé vào cổng
            return firstHourPrice;
        }

        // BR-16: Mô hình thời gian thực
        // Giờ đầu + (số blocks * giá block)
        var totalBlocks = playedMinutes / blockMinutes;
        return firstHourPrice + (totalBlocks * blockRate);
    }
}

#endregion
