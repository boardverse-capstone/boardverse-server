using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("AutoReleaseExpiredSessionsJob stopped (host shutdown).");
                break;
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
        var dbContext = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();

        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-30); // BR-END-05: grace 30 phút

        // GAP-R4-A4 Fix: Mở transaction TRƯỚC khi fetch batch để FOR UPDATE SKIP LOCKED
        // có hiệu lực trên Postgres. Nếu deploy cluster, 2 instance sẽ skip qua row đã lock
        // → mỗi session chỉ được release đúng 1 lần.
        // Lưu ý: ProcessAutoReleaseAsync tạo scope riêng (mỗi session 1 scope) — sẽ tái sử dụng
        // sessionRepo ở đây cho cluster-safe fetch, nhưng flow thực thi vẫn tách scope để đảm bảo
        // Dispose kịp thời các DbContext. Mỗi session trong batch vẫn được process với scope riêng.
        await using var batchTx = await dbContext.Database.BeginTransactionAsync(ct);

        var expiredSessions = await sessionRepo.GetExpiredForUpdateAsync(cutoff, ct);
        if (expiredSessions.Count == 0)
        {
            await batchTx.CommitAsync(ct);
            _logger.LogDebug("AutoReleaseExpiredSessionsJob: No expired sessions found");
            return;
        }

        _logger.LogInformation(
            "AutoReleaseExpiredSessionsJob: Found {Count} expired sessions to auto-release",
            expiredSessions.Count);

        // GAP-R6-BJ-01 Fix: phải SaveChanges trước khi commit batchTx.
        // Trước đây: process từng session bằng cách load lại GetByIdAsync rồi mutate trong
        // scope riêng. Sau khi batchTx.Commit ở đây → mất FOR UPDATE SKIP LOCKED lock,
        // 2 instance cluster có thể pick cùng sessionId và process trùng.
        // Fix: dùng atomic TryUpdateStatusAsync (UPDATE...WHERE Status=Active RETURNING)
        // để flip Closed ở ngoài batchTx. Sau đó mới commit batchTx → side effects chỉ chạy 1 lần.
        var nowEpoch = now;
        var sessionIds = new List<Guid>(expiredSessions.Count);
        foreach (var session in expiredSessions)
        {
            // Atomic flip Active → Closed, RETURNING id nếu flip thành công.
            var flipped = await sessionRepo.TryUpdateStatusAsync(
                session.Id,
                GroupSessionStatus.Active,
                GroupSessionStatus.Closed,
                ct);
            if (flipped)
            {
                sessionIds.Add(session.Id);
            }
        }

        await batchTx.CommitAsync(ct);

        if (sessionIds.Count == 0)
        {
            _logger.LogDebug(
                "AutoReleaseExpiredSessionsJob: All {Count} sessions were already closed by another instance",
                expiredSessions.Count);
            return;
        }

        foreach (var sessionId in sessionIds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await ProcessAutoReleaseSideEffectsAsync(sessionId, nowEpoch, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "AutoReleaseExpiredSessionsJob: Failed to process side effects for session {SessionId}",
                    sessionId);
            }
        }
    }

    /// <summary>
    /// GAP-R6-BJ-01: side effects (reservation update, walk-in window, release table/box) sau khi
    /// session đã atomic-flipped sang Closed. KHÔNG bao gồm session status update (đã làm ở ngoài).
    /// Failure của side effects KHÔNG roll back status flip — đã là best-effort cleanup.
    /// </summary>
    private async Task ProcessAutoReleaseSideEffectsAsync(
        Guid sessionId,
        DateTime now,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IActiveSessionRepository>();
        var reservationRepo = scope.ServiceProvider.GetRequiredService<IReservationRepository>();
        var walkInService = scope.ServiceProvider.GetRequiredService<IWalkInService>();
        var lobbyRepo = scope.ServiceProvider.GetRequiredService<ILobbyRepository>();
        var dbContext = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();

        var session = await sessionRepo.GetByIdAsync(sessionId);
        if (session == null)
        {
            _logger.LogWarning(
                "AutoReleaseExpiredSessionsJob: Session {SessionId} disappeared between flip and side effects",
                sessionId);
            return;
        }

        // Re-check guard — nếu instance khác đã xử lý rồi (race khi delay giữa flip và side effects).
        if (session.Status != GroupSessionStatus.Closed)
        {
            _logger.LogWarning(
                "AutoReleaseExpiredSessionsJob: Session {SessionId} status is {Status} (expected Closed) — skipping side effects",
                sessionId, session.Status);
            return;
        }

        _logger.LogInformation(
            "AutoReleaseExpiredSessionsJob: Processing side effects for session {SessionId}, lobby {LobbyId}",
            session.Id, session.LobbyId);

        IDbContextTransaction? ownedTx = null;
        try
        {
            ownedTx = await dbContext.Database.BeginTransactionAsync(ct);

            // Release table and box back to Available.
            await sessionRepo.ReleaseSessionTableAndBoxAsync(session.Id);

            // BR-END-05: Mark reservation as AutoReleased if linked.
            Reservation? reservation = null;
            Lobby? lobby = null;
            if (session.LobbyId.HasValue)
            {
                reservation = await reservationRepo.GetByLobbyIdAsync(session.LobbyId.Value);

                // FIX 2026-08-24: Đồng bộ Lobby.Status = Closed khi Reservation → Completed.
                // Trước đây AutoRelease chỉ update Reservation, để Lobby ở InProgress → FE
                // hiển thị lịch hẹn "Completed" nhưng lobby vẫn "InProgress" → inconsistent state.
                // (Issue từ data thật: reservation.Status=Completed, endReason=AutoReleased,
                // lobbyStatus=InProgress trong cùng payload.)
                lobby = await lobbyRepo.GetByIdAsync(session.LobbyId.Value);

                if (reservation != null)
                {
                    reservation.Status = ReservationStatus.Completed;
                    reservation.ActualEndAt = now;
                    reservation.EndReason = SessionEndReason.AutoReleased;
                    reservation.PlayedRatio = 1.0m;
                    await reservationRepo.UpdateAsync(reservation);
                    // GAP-R6-BJ-01 Fix: SaveChangesAsync TRƯỚC CommitAsync — nếu thiếu, EF
                    // không flush UPDATE Reservations vào DB → AutoReleased status bị mất.
                    await reservationRepo.SaveChangesAsync(ct);
                }

                if (lobby != null
                    && lobby.Status != LobbyStatus.Closed
                    && lobby.Status != LobbyStatus.TimeoutFailed
                    && lobby.Status != LobbyStatus.HostCancelled
                    && lobby.Status != LobbyStatus.RejectedByCafe
                    && lobby.Status != LobbyStatus.ExpiredByCafe)
                {
                    lobby.Status = LobbyStatus.Closed;
                    lobby.ClosedAt = now;
                    lobby.UpdatedAt = now;

                    // Deactivate members tương tự ReservationService.CompleteAndCaptureAsync
                    // để các API list lobby không trả về member "active" cho lobby đã đóng.
                    if (lobby.Members != null)
                    {
                        foreach (var member in lobby.Members.Where(m => m.IsActive))
                        {
                            member.IsActive = false;
                            member.Status = LobbyMemberStatus.LobbyTerminated;
                            member.LeftAt ??= now;
                        }
                    }

                    await lobbyRepo.UpdateAsync(lobby);
                    await lobbyRepo.SaveChangesAsync(ct);

                    _logger.LogInformation(
                        "AutoReleaseExpiredSessionsJob: Lobby {LobbyId} status synced to Closed",
                        lobby.Id);
                }
            }

            await ownedTx.CommitAsync(ct);

            // EC-09: Create WalkInWindow cho phần thời gian còn lại (sau commit, side effect best-effort).
            if (reservation != null)
            {
                try
                {
                    var releasedSeats = session.Members?
                        .Count(m => !m.IsGuestSlot
                            && m.Status != IndividualSessionStatus.SuspendedMutation
                            && m.Status != IndividualSessionStatus.Finished) ?? 0;
                    var window = await walkInService.CreateWindowFromReservationAsync(
                        reservation, releasedSeats, now);
                    if (window != null)
                    {
                        _logger.LogInformation(
                            "AutoRelease WalkInWindow created: {WindowId}, {Seats} seats",
                            window.Id, releasedSeats);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to create WalkInWindow for session {SessionId}", session.Id);
                }
            }
        }
        catch
        {
            if (ownedTx != null)
                await ownedTx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (ownedTx != null)
                await ownedTx.DisposeAsync();
        }

        _logger.LogInformation(
            "AutoReleaseExpiredSessionsJob: Session {SessionId} side effects completed", session.Id);
    }
}
