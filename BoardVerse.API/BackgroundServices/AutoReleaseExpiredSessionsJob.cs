using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// BR-END-05: Quét mỗi 5 phút, auto-release session
/// quá ExtendedEndTime (hoặc ScheduledEndTime) + grace 30 phút.
///
/// Staff quên end session → AutoReleaseExpiredSessionsJob auto-set ActiveSession.Status = Closed.
///
/// EC-09: Tạo WalkInWindow cho phần thời gian còn lại
/// (WindowStart = Session.StartedAt, WindowEnd = Reservation.ScheduledEndTime).
///
/// Dùng index IX_Reservations_ScheduledEndTime_Status.
/// </summary>
public class AutoReleaseExpiredSessionsJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoReleaseExpiredSessionsJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public AutoReleaseExpiredSessionsJob(
        IServiceScopeFactory scopeFactory,
        ILogger<AutoReleaseExpiredSessionsJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AutoReleaseExpiredSessionsJob started. Running every {Interval} minutes",
            _interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunReleaseAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoReleaseExpiredSessionsJob: error during release");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task RunReleaseAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IActiveSessionRepository>();
        var reservationRepo = scope.ServiceProvider.GetRequiredService<IReservationRepository>();
        var walkInService = scope.ServiceProvider.GetRequiredService<IWalkInService>();

        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-30); // BR-END-05: grace 30 phút

        // Lấy Active sessions
        var activeSessions = await sessionRepo.GetExpiredAsync(cutoff, ct);

        // BR-END-05: Auto-release session quá ExtendedEndTime + grace 30 phút.
        // Sử dụng Reservation.ExtendedEndTime ?? ScheduledEndTime làm baseline.
        var expiredSessions = new List<ActiveSession>();
        foreach (var session in activeSessions)
        {
            if (!session.LobbyId.HasValue) continue;

            // BR-END-05: resolve reservation từ Lobby (session.LobbyId = lobby.Id).
            var reservation = await reservationRepo.GetByLobbyIdAsync(session.LobbyId.Value);
            if (reservation == null) continue;

            var endTime = reservation.ExtendedEndTime ?? reservation.ScheduledEndTime;
            if (endTime.AddMinutes(30) < now)
            {
                expiredSessions.Add(session);
            }
        }

        if (expiredSessions.Count == 0)
        {
            _logger.LogDebug("AutoReleaseExpiredSessionsJob: No expired sessions found");
            return;
        }

        _logger.LogInformation(
            "AutoReleaseExpiredSessionsJob: Found {Count} expired sessions to auto-release",
            expiredSessions.Count);

        foreach (var session in expiredSessions)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await ProcessAutoReleaseAsync(session, now, walkInService, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "AutoReleaseExpiredSessionsJob: Failed to auto-release session {SessionId}",
                    session.Id);
            }
        }
    }

    private async Task ProcessAutoReleaseAsync(
        ActiveSession session,
        DateTime now,
        IWalkInService walkInService,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IActiveSessionRepository>();
        var reservationRepo = scope.ServiceProvider.GetRequiredService<IReservationRepository>();

        _logger.LogInformation(
            "AutoReleaseExpiredSessionsJob: Auto-releasing session {SessionId}, lobby {LobbyId}",
            session.Id, session.LobbyId);

        // Update session → Closed
        session.Status = GroupSessionStatus.Closed;
        await sessionRepo.SaveChangesAsync();

        // Release table and box back to Available (they were never released since payment never happened).
        await sessionRepo.ReleaseSessionTableAndBoxAsync(session.Id);

        // BR-END-05: Mark reservation as AutoReleased (SessionEndReason.AutoReleased).
        if (session.LobbyId.HasValue)
        {
            var reservation = await reservationRepo.GetByLobbyIdAsync(session.LobbyId.Value);
            if (reservation != null)
            {
                reservation.Status = ReservationStatus.Completed;
                reservation.ActualEndAt = now;
                reservation.EndReason = SessionEndReason.AutoReleased;
                reservation.PlayedRatio = 1.0m; // Session reached end
                await reservationRepo.UpdateAsync(reservation);
            }
        }

        // EC-09: Create WalkInWindow for remaining time
        // This handles the case where session was never properly checked in
        // or staff forgot to end - we release the seats for walk-in opportunity
        if (session.LobbyId.HasValue)
        {
            try
            {
                var reservation = await reservationRepo.GetByLobbyIdAsync(session.LobbyId.Value);
                if (reservation != null)
                {
                    // Get seat count from session members
                    var releasedSeats = session.Members?.Count ?? reservation.MaxPlayers;

                    var window = await walkInService.CreateWindowFromReservationAsync(
                        reservation,
                        releasedSeats,
                        now);

                    if (window != null)
                    {
                        _logger.LogInformation(
                            "AutoRelease WalkInWindow created: {WindowId}, {Seats} seats, {Start} - {End}",
                            window.Id, releasedSeats, now, reservation.ScheduledEndTime);
                    }
                }
            }
            catch (Exception ex)
            {
                // Non-blocking: log warning, don't fail the auto-release
                _logger.LogWarning(ex,
                    "Failed to create WalkInWindow for auto-released session {SessionId}",
                    session.Id);
            }
        }

        _logger.LogInformation(
            "AutoReleaseExpiredSessionsJob: Session {SessionId} auto-released, table/box released",
            session.Id);
    }
}
