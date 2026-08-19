using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Services.IServices;

/// <summary>
/// BR-KARMA-01 §4.3 + §9.5: High-level Karma operations.
/// Tính level, gửi warning, áp restriction, submit appeal.
/// </summary>
public interface IKarmaService
{
    /// <summary>BR §9.5: Tính <see cref="KarmaLevel"/> từ UserProfile.KarmaPoints.</summary>
    Task<KarmaLevel> GetUserKarmaLevelAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Trả về điểm Karma hiện tại của user (mặc định 100 nếu profile không tồn tại).</summary>
    Task<int> GetUserKarmaPointsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>BR-KARMA-02: Gửi warning nếu user có 3-4 active violations (chưa warning trong 7 ngày).</summary>
    Task<KarmaWarningResult> SendWarningIfNeededAsync(Guid userId, CancellationToken ct = default);

    /// <summary>BR-KARMA-03: Áp dụng restriction (30 ngày) nếu user có 5+ active violations.</summary>
    Task<KarmaRestrictionResult> ApplyRestrictionIfNeededAsync(Guid userId, CancellationToken ct = default);

    /// <summary>BR-KARMA-05: User submit appeal cho 1 violation cụ thể.</summary>
    /// <returns>True nếu appeal được ghi nhận.</returns>
    Task<bool> SubmitAppealAsync(Guid userId, Guid recordId, string reason, CancellationToken ct = default);

    /// <summary>BR-KARMA-04: Background job expire các violation cũ hơn 30 ngày.</summary>
    /// <returns>Số record được expire.</returns>
    Task<int> ResetMonthlyAsync(CancellationToken ct = default);

    /// <summary>BR-KARMA-03: Kiểm tra user có bị restrict slot &lt; 4h hay không.</summary>
    bool IsRestrictedForShortSlots(UserProfile profile, int scheduledMinutes);
}

/// <summary>Kết quả <see cref="IKarmaService.SendWarningIfNeededAsync"/>.</summary>
public class KarmaWarningResult
{
    public bool Sent { get; set; }
    public int ViolationCount { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Kết quả <see cref="IKarmaService.ApplyRestrictionIfNeededAsync"/>.</summary>
public class KarmaRestrictionResult
{
    public bool Applied { get; set; }
    public DateTime? Until { get; set; }
    public int ViolationCount { get; set; }
    public string? Reason { get; set; }
}
