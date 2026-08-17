using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Helpers;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// BR-CHECKIN-02: Quét mỗi 5 phút, tìm Reservation Confirmed
/// nhưng quá 30 phút sau ScheduledStartTime mà chưa check-in → auto NoShow.
///
/// BR-REFUND-03: No-show (grace 30 phút) → 0% refund, DEPOSIT_FORFEIT.
/// BR-REFUND-05: BVC không rút về tiền thật.
/// BR-WALKIN-01: Tạo WalkInWindow khi no-show (§4.7 doc).
/// </summary>
public class ReservationNoShowDetectionJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReservationNoShowDetectionJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public ReservationNoShowDetectionJob(
        IServiceScopeFactory scopeFactory,
        ILogger<ReservationNoShowDetectionJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReservationNoShowDetectionJob started. Running every {Interval} minutes", _interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDetectionAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReservationNoShowDetectionJob: error during detection");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task RunDetectionAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var reservationRepo = scope.ServiceProvider.GetRequiredService<IReservationRepository>();
        var walletService = scope.ServiceProvider.GetRequiredService<IWalletService>();
        var walkInService = scope.ServiceProvider.GetRequiredService<IWalkInService>();
        var outboxRepo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var karmaService = scope.ServiceProvider.GetRequiredService<IPlayerKarmaService>();
        var configProvider = scope.ServiceProvider.GetRequiredService<ISystemConfigurationProvider>();

        // Skip no-show detection nếu bypass đang bật (dev/test only).
        if (await TimeWindowGuard.ShouldBypassAsync(
                configProvider, _logger,
                operation: "ReservationNoShowDetectionJob"))
        {
            _logger.LogInformation(
                "ReservationNoShowDetectionJob: skipped because bypass-time-window is enabled.");
            return;
        }

        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-30); // BR-CHECKIN-02: grace 30 phút

        // Query: Status = Confirmed AND ScheduledStartTime < (now - 30min)
        // Sử dụng index IX_Reservations_ScheduledStartTime_Status
        var noShowCandidates = await reservationRepo.GetNoShowCandidatesAsync(cutoff, ct);

        if (noShowCandidates.Count == 0)
        {
            _logger.LogDebug("ReservationNoShowDetectionJob: No no-show candidates found");
            return;
        }

        _logger.LogInformation(
            "ReservationNoShowDetectionJob: Found {Count} no-show candidates",
            noShowCandidates.Count);

        foreach (var reservation in noShowCandidates)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await ProcessNoShowAsync(reservation, walletService, walkInService, outboxRepo, karmaService, now, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ReservationNoShowDetectionJob: Failed to process NoShow for ReservationId={Id}",
                    reservation.Id);
            }
        }
    }

    private async Task ProcessNoShowAsync(
        Reservation reservation,
        IWalletService walletService,
        IWalkInService walkInService,
        IOutboxRepository outboxRepo,
        IPlayerKarmaService karmaService,
        DateTime now,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing NoShow for ReservationId={Id}, HostId={HostId}, ScheduledStartTime={Start}",
            reservation.Id, reservation.HostId, reservation.ScheduledStartTime);

        // 1. Update status → NoShow
        reservation.Status = ReservationStatus.NoShow;
        reservation.UpdatedAt = now;

        // 2. Forfeit deposit (BR-REFUND-03: 0% refund, BR-REFUND-05: BVC không rút về VND)
        var forfeitIdempotencyKey = $"forfeit-{reservation.Id:N}";
        await walletService.ForfeitDepositAsync(
            reservation.HostId,
            reservation.DepositAmount,
            reservation.LobbyId,
            reservation.Id,
            forfeitIdempotencyKey,
            ct);

        // 3. Release seat + game inventory
        await ReleaseInventoryAsync(reservation, now, ct);

        // 4. Create WalkInWindow (BR-WALKIN-01: §4.7 doc)
        //    WindowStart = ScheduledStartTime (no-show time), WindowEnd = ScheduledEndTime
        //    releasedSeats = MaxPlayers (all seats released since no one showed up)
        var releasedSeats = reservation.MaxPlayers;
        try
        {
            var window = await walkInService.CreateWindowFromReservationAsync(
                reservation,
                releasedSeats,
                now);

            if (window != null)
            {
                _logger.LogInformation(
                    "NoShow WalkInWindow created: {WindowId}, {Seats} seats, {Start} - {End}",
                    window.Id, releasedSeats, now, reservation.ScheduledEndTime);
            }
        }
        catch (Exception ex)
        {
            // Non-blocking: log warning, don't fail the no-show processing
            _logger.LogWarning(ex,
                "Failed to create WalkInWindow for NoShow ReservationId={Id}",
                reservation.Id);
        }

        // 4b. Record karma violation for host (BR §21A.9: -10 karma).
        try
        {
            await karmaService.RecordNoShowAsync(reservation.Id, reservation.HostId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to record no-show karma for ReservationId={Id}, HostId={HostId}",
                reservation.Id, reservation.HostId);
        }

        // 5. Outbox event
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            reservationId = reservation.Id,
            hostId = reservation.HostId,
            lobbyId = reservation.LobbyId,
            cafeId = reservation.CafeId,
            forfeitedBvc = reservation.DepositAmount,
            scheduledStartTime = reservation.ScheduledStartTime,
            noShowAt = now
        });

        await outboxRepo.AddAsync(new OutboxEvent
        {
            Id = Guid.NewGuid(),
            EventType = OutboxEventType.ReservationNoShow,
            Payload = payload,
            IdempotencyKey = forfeitIdempotencyKey,
            ReservationId = reservation.Id,
            UserId = reservation.HostId,
            CreatedAt = now
        });

        _logger.LogInformation(
            "Reservation {Id} marked as NoShow. Forfeited {Bvc} BVC",
            reservation.Id, reservation.DepositAmount);
    }

    private async Task ReleaseInventoryAsync(Reservation reservation, DateTime now, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var seatInvRepo = scope.ServiceProvider.GetRequiredService<ISeatInventoryRepository>();
        var gameInvRepo = scope.ServiceProvider.GetRequiredService<IGameInventoryRepository>();

        // Release seats: use (cafeId, playDate, timeSlot)
        var seatInv = await seatInvRepo.GetAsync(reservation.CafeId, reservation.PlayDate, reservation.TimeSlot);
        if (seatInv != null)
        {
            seatInv.HeldSeats = Math.Max(0, seatInv.HeldSeats - reservation.MaxPlayers);
            seatInv.UpdatedAt = now;
            await seatInvRepo.UpdateAsync(seatInv);
        }

        // Release game copy: use (cafeId, gameId, playDate, timeSlot)
        var gameInv = await gameInvRepo.GetAsync(
            reservation.CafeId, reservation.GameId, reservation.PlayDate, reservation.TimeSlot);
        if (gameInv != null)
        {
            gameInv.HeldCopies = Math.Max(0, gameInv.HeldCopies - 1);
            gameInv.UpdatedAt = now;
            await gameInvRepo.UpdateAsync(gameInv);
        }
    }
}
