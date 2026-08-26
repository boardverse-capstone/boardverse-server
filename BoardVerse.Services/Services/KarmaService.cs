using BoardVerse.Core.Constants;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

/// <summary>
/// BR-KARMA-01 §4.3 + §9.5: High-level Karma operations.
/// - Tính <see cref="KarmaLevel"/> từ <c>UserProfile.KarmaPoints</c>.
/// - Gửi cảnh báo khi user có 3-4 violations (BR-KARMA-02).
/// - Áp dụng restriction khi user có 5+ violations (BR-KARMA-03).
/// - Submit appeal cho user (BR-KARMA-05).
/// </summary>
public class KarmaService : IKarmaService
{
    private readonly IKarmaShortPlayRecordRepository _recordRepo;
    private readonly IUserProfileRepository _userProfileRepo;
    private readonly ILogger<KarmaService> _logger;

    public KarmaService(
        IKarmaShortPlayRecordRepository recordRepo,
        IUserProfileRepository userProfileRepo,
        ILogger<KarmaService> logger)
    {
        _recordRepo = recordRepo;
        _userProfileRepo = userProfileRepo;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<KarmaLevel> GetUserKarmaLevelAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await _userProfileRepo.GetProfileByUserIdAsync(userId);
        if (profile == null)
        {
            return KarmaLevel.Average;
        }

        return CalculateLevel(profile.KarmaPoints);
    }

    /// <inheritdoc />
    public async Task<int> GetUserKarmaPointsAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await _userProfileRepo.GetProfileByUserIdAsync(userId);
        // GAP-R6-KARMA-08 Fix: magic 100 → KarmaLevelThresholds.Default.
        return profile?.KarmaPoints ?? KarmaLevelThresholds.Default;
    }

    /// <inheritdoc />
    public async Task<KarmaWarningResult> SendWarningIfNeededAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await _userProfileRepo.GetProfileByUserIdAsync(userId);
        if (profile == null)
        {
            return new KarmaWarningResult { Sent = false, Reason = "Profile not found" };
        }

        var activeRecords = await _recordRepo.GetActiveCountByUserAsync(userId, ct);
        // BR-KARMA-02: 3-4 violations → warning
        if (activeRecords is < 3 or > 4)
        {
            return new KarmaWarningResult { Sent = false, Reason = $"Active violations = {activeRecords} (need 3-4)" };
        }

        if (profile.LastWarningAt.HasValue && (DateTime.UtcNow - profile.LastWarningAt.Value).TotalDays < 7)
        {
            return new KarmaWarningResult { Sent = false, Reason = "Warning sent within last 7 days" };
        }

        profile.LastWarningAt = DateTime.UtcNow;
        await _userProfileRepo.SaveChangesAsync();

        _logger.LogInformation(
            "KarmaService.SendWarningIfNeededAsync: UserId={UserId} activeViolations={Count} → warning sent",
            userId, activeRecords);

        return new KarmaWarningResult { Sent = true, ViolationCount = activeRecords };
    }

    /// <inheritdoc />
    public async Task<KarmaRestrictionResult> ApplyRestrictionIfNeededAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await _userProfileRepo.GetProfileByUserIdAsync(userId);
        if (profile == null)
        {
            return new KarmaRestrictionResult { Applied = false, Reason = "Profile not found" };
        }

        var activeRecords = await _recordRepo.GetActiveCountByUserAsync(userId, ct);
        // BR-KARMA-03: 5+ violations → restrict to slots >= 4h
        if (activeRecords < 5)
        {
            return new KarmaRestrictionResult { Applied = false, Reason = $"Active violations = {activeRecords} (need >= 5)" };
        }

        if (profile.KarmaRestrictedUntil.HasValue && profile.KarmaRestrictedUntil > DateTime.UtcNow)
        {
            return new KarmaRestrictionResult { Applied = false, Reason = "Already restricted", Until = profile.KarmaRestrictedUntil };
        }

        profile.KarmaRestrictedUntil = DateTime.UtcNow.AddDays(30);
        await _userProfileRepo.SaveChangesAsync();

        _logger.LogInformation(
            "KarmaService.ApplyRestrictionIfNeededAsync: UserId={UserId} activeViolations={Count} → restricted until {Until:O}",
            userId, activeRecords, profile.KarmaRestrictedUntil);

        return new KarmaRestrictionResult { Applied = true, Until = profile.KarmaRestrictedUntil, ViolationCount = activeRecords };
    }

    /// <inheritdoc />
    public async Task<bool> SubmitAppealAsync(Guid userId, Guid recordId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        var record = await _recordRepo.GetByIdAsync(recordId, ct);
        if (record == null || record.UserId != userId)
        {
            return false;
        }

        if (record.AppealReviewedAt.HasValue)
        {
            return false;
        }

        record.AppealRequested = true;
        record.AppealReason = reason;
        await _recordRepo.UpdateAsync(record, ct);

        _logger.LogInformation(
            "KarmaService.SubmitAppealAsync: UserId={UserId} recordId={RecordId} → appeal submitted",
            userId, recordId);

        return true;
    }

    /// <inheritdoc />
    public async Task<int> ResetMonthlyAsync(CancellationToken ct = default)
    {
        // BR-KARMA-04: Expired violations sau 30 ngày không có violation mới.
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var expiredCount = await _recordRepo.ExpireOldRecordsAsync(cutoff, ct);

        _logger.LogInformation(
            "KarmaService.ResetMonthlyAsync: Expired {Count} old karma records older than {Cutoff:O}",
            expiredCount, cutoff);

        return expiredCount;
    }

    /// <inheritdoc />
    public bool IsRestrictedForShortSlots(UserProfile profile, int scheduledMinutes)
    {
        if (!profile.KarmaRestrictedUntil.HasValue || profile.KarmaRestrictedUntil < DateTime.UtcNow)
        {
            return false;
        }

        // BR-KARMA-03: chỉ cho đặt slot >= 4h (240 phút).
        // GAP-R6-KARMA-08 Fix: magic 240 → KarmaLevelThresholds.RestrictedMinimumMinutes.
        return scheduledMinutes < KarmaLevelThresholds.RestrictedMinimumMinutes;
    }

    private static KarmaLevel CalculateLevel(int karmaPoints)
    {
        return karmaPoints switch
        {
            // GAP-R6-KARMA-08 Fix: magic numbers → named constants.
            // Tên và threshold theo BR-KARMA-02 / spec.
            >= KarmaLevelThresholds.Excellent => KarmaLevel.Excellent,
            >= KarmaLevelThresholds.Good => KarmaLevel.Good,
            >= KarmaLevelThresholds.Average => KarmaLevel.Average,
            >= KarmaLevelThresholds.Low => KarmaLevel.Low,
            >= KarmaLevelThresholds.Poor => KarmaLevel.Poor,
            _ => KarmaLevel.Critical
        };
    }
}
