namespace BoardVerse.Core.Entities;

/// <summary>
/// Snapshot cấu hình cọc tại thời điểm host bấm confirm (§4 + §19.1).
/// BR-NEW-12 + 21F.9: cấu hình cafe thay đổi chỉ áp dụng cho lobby mới;
/// lobby đã tạo giữ nguyên snapshot để audit & tính tiền.
/// </summary>
public class DepositSnapshot
{
    /// <summary>Số BVC mỗi người do cafe cấu hình lúc tạo.</summary>
    public long DepositRatePerPerson { get; set; }

    /// <summary>Số người tối đa host đã đặt.</summary>
    public int MaxPlayers { get; set; }

    /// <summary>depositRatePerPerson × maxPlayers (chưa áp riskMultiplier).</summary>
    public long BaseDeposit { get; set; }

    /// <summary>1.0 – 2.0 (BR-RISK-03 + BR-DEPOSIT-04).</summary>
    public decimal RiskMultiplier { get; set; } = 1.0m;

    /// <summary>Số BVC cuối cùng phải hold (≥ MinDepositApplied).</summary>
    public long FinalDeposit { get; set; }

    /// <summary>minDeposit theo khoảng cách playDate (BR-NEW-01 §8).</summary>
    public long MinDepositApplied { get; set; }

    /// <summary>Quán chọn mô hình nào lúc tạo (flat-entry hay block-time).</summary>
    public string? PricingModel { get; set; }
}