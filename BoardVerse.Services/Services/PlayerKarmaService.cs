using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

/// <inheritdoc cref="IPlayerKarmaService"/>
public class PlayerKarmaService : IPlayerKarmaService
{
    private readonly IKarmaShortPlayRecordRepository _recordRepo;
    private readonly IUserProfileRepository _userProfileRepo;
    private readonly ILogger<PlayerKarmaService> _logger;

    public PlayerKarmaService(
        IKarmaShortPlayRecordRepository recordRepo,
        IUserProfileRepository userProfileRepo,
        ILogger<PlayerKarmaService> logger)
    {
        _recordRepo = recordRepo;
        _userProfileRepo = userProfileRepo;
        _logger = logger;
    }

    public async Task<bool> RecordShortPlayAsync(
        Guid reservationId,
        Guid userId,
        int playedMinutes,
        int scheduledMinutes,
        CancellationToken ct = default)
    {
        if (scheduledMinutes <= 0)
        {
            return false;
        }

        var ratio = Math.Min(1m, Math.Max(0m, (decimal)playedMinutes / scheduledMinutes));
        if (ratio >= 0.5m)
        {
            return false;
        }

        var existing = await _recordRepo.GetByReservationAndUserAsync(reservationId, userId, ct);
        if (existing != null)
        {
            _logger.LogDebug(
                "RecordShortPlayAsync: ReservationId={ReservationId}, UserId={UserId} đã có record → idempotent skip.",
                reservationId, userId);
            return false;
        }

        var profile = await _userProfileRepo.GetProfileByUserIdAsync(userId);
        if (profile == null)
        {
            _logger.LogWarning(
                "RecordShortPlayAsync: UserProfile {UserId} không tồn tại → skip.", userId);
            return false;
        }

        var karmaDelta = -5; // BR-KARMA-01 §4.3
        var record = new KarmaShortPlayRecord
        {
            Id = Guid.NewGuid(),
            ReservationId = reservationId,
            UserId = userId,
            PlayedMinutes = playedMinutes,
            ScheduledMinutes = scheduledMinutes,
            PlayedRatio = ratio,
            KarmaDelta = karmaDelta,
            KarmaPointsAdded = karmaDelta,
            TotalKarmaScore = profile.KarmaPoints + karmaDelta,
            Status = KarmaRecordStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        await _recordRepo.AddAsync(record, ct);

        profile.KarmaPoints = record.TotalKarmaScore;
        await _userProfileRepo.SaveChangesAsync();

        _logger.LogInformation(
            "RecordShortPlayAsync: UserId={UserId} short-play ratio={Ratio:F4} → karmaDelta={Delta}, total={Total}",
            userId, ratio, karmaDelta, record.TotalKarmaScore);

        return true;
    }

    public async Task<bool> RecordNoShowAsync(Guid reservationId, Guid hostId, CancellationToken ct = default)
    {
        var existing = await _recordRepo.GetByReservationAndUserAsync(reservationId, hostId, ct);
        if (existing != null)
        {
            return false;
        }

        var profile = await _userProfileRepo.GetProfileByUserIdAsync(hostId);
        if (profile == null)
        {
            return false;
        }

        var karmaDelta = -10; // BR §21A.9
        var record = new KarmaShortPlayRecord
        {
            Id = Guid.NewGuid(),
            ReservationId = reservationId,
            UserId = hostId,
            PlayedMinutes = 0,
            ScheduledMinutes = 0,
            PlayedRatio = 0m,
            KarmaDelta = karmaDelta,
            KarmaPointsAdded = karmaDelta,
            TotalKarmaScore = profile.KarmaPoints + karmaDelta,
            Status = KarmaRecordStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        await _recordRepo.AddAsync(record, ct);
        profile.KarmaPoints = record.TotalKarmaScore;
        await _userProfileRepo.SaveChangesAsync();

        _logger.LogInformation(
            "RecordNoShowAsync: HostId={HostId} no-show → karmaDelta={Delta}, total={Total}",
            hostId, karmaDelta, record.TotalKarmaScore);

        return true;
    }

    public async Task<bool> RecordEarlyCheckoutAsync(
        Guid reservationId,
        Guid userId,
        int playedMinutes,
        int scheduledMinutes,
        CancellationToken ct = default)
    {
        var existing = await _recordRepo.GetByReservationAndUserAsync(reservationId, userId, ct);
        if (existing != null)
        {
            return false;
        }

        var ratio = scheduledMinutes > 0
            ? Math.Min(1m, Math.Max(0m, (decimal)playedMinutes / scheduledMinutes))
            : 0m;

        var record = new KarmaShortPlayRecord
        {
            Id = Guid.NewGuid(),
            ReservationId = reservationId,
            UserId = userId,
            PlayedMinutes = playedMinutes,
            ScheduledMinutes = scheduledMinutes,
            PlayedRatio = ratio,
            KarmaDelta = 0,
            KarmaPointsAdded = 0,
            TotalKarmaScore = 0,
            Status = KarmaRecordStatus.Cleared,
            CreatedAt = DateTime.UtcNow
        };

        await _recordRepo.AddAsync(record, ct);
        return true;
    }

    public Task<KarmaShortPlayRecord?> GetLatestByUserAsync(Guid userId, CancellationToken ct = default)
        => _recordRepo.GetLatestByUserAsync(userId, ct);
}