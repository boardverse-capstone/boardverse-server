using BoardVerse.Core.Constants;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Helpers;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("ReservationNoShowDetectionJob stopped (host shutdown).");
                break;
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
        // TZ-FIX-REVERT (2026-10-03): Revert "FIX TZ-NOSHOW-01" cùng ngày.
        //
        // Lý do revert:
        // - Cùng ngày 2026-10-02, `BuildScheduledStartEndFromPreferred` được fix (TZ-DT-UTC-02)
        //   để return `Kind=Utc` thay vì raw VN local. → `Reservation.ScheduledStartTime` lưu DB
        //   dưới dạng UTC ticks.
        // - Fix "TZ-NOSHOW-01" trước đó assume ScheduledStartTime là "Unspecified VN local raw" —
        //   assumption này không còn đúng sau TZ-DT-UTC-02.
        // - Hệ quả khi giữ "fix" cũ: `ToVietnamLocal(now)` trả Kind=Unspecified với raw ticks =
        //   (UTC + 7h) - 30min. Khi Npgsql gửi raw ticks này vào cột `timestamptz`, nó interpret
        //   thành UTC. → cutoff lệch ~7h so với "now - 30min" thực sự → match cả reservation
        //   trong tương lai gần (đã reproduce 2026-10-03 03:05:34Z, reservation có scheduledStart
        //   03:40:00Z — chỉ 35 phút trong tương lai nhưng bị flip NoShow).
        //
        // Fix đúng: giữ cả 2 vế ở UTC. Cả `now` và `ScheduledStartTime` đều Kind=Utc → so sánh
        // trực tiếp chính xác.
        var cutoff = now.AddMinutes(-30); // BR-CHECKIN-02: grace 30 phút

        // Query: Status = Confirmed AND ScheduledStartTime < cutoff
        // Sử dụng index IX_Reservations_ScheduledStartTime_Status.
        var noShowCandidates = await reservationRepo.GetNoShowCandidatesAsync(cutoff, ct);

        if (noShowCandidates.Count == 0)
        {
            _logger.LogDebug("ReservationNoShowDetectionJob: No no-show candidates found");
            return;
        }

        // GAP-R6-BJ-NOSHOW Fix: atomic flip Confirmed → NoShow + chỉ process side effects cho
        // các reservation thực sự được flip (rowsAffected > 0).
        // Trước đây: load → mutate in-memory → SaveChanges → 2 instance cluster pick cùng reservation
        //   → cả 2 đều gọi ReleaseInventoryAsync → DOUBLE DECREMENT HeldSeats/HeldCopies (không idempotent).
        // Sau: ExecuteUpdateAsync WHERE Status=Confirmed SET Status=NoShow → Postgres MVCC đảm bảo
        //   chỉ 1 instance flip được cho mỗi row. Sau đó re-query những IDs đã flip (Status=NoShow)
        //   và CHỈ process side effects cho những IDs đó. Side effects chạy đúng 1 lần per reservation.
        var candidateIds = noShowCandidates.Select(r => r.Id).ToList();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerse.Data.BoardVerseDbContext>();

        // Step 1: atomic flip. ExecuteUpdateAsync chỉ update rows còn Status=Confirmed,
        // nên 2 instance cùng chạy → 1 flip được, 1 rowsAffected=0.
        var flippedCount = await db.Reservations
            .Where(r => candidateIds.Contains(r.Id) && r.Status == ReservationStatus.Confirmed)
            .ExecuteUpdateAsync(r => r
                .SetProperty(x => x.Status, ReservationStatus.NoShow)
                .SetProperty(x => x.UpdatedAt, now),
                ct);

        if (flippedCount == 0)
        {
            _logger.LogDebug(
                "ReservationNoShowDetectionJob: All {Count} candidates were already processed by another instance.",
                noShowCandidates.Count);
            return;
        }

        // Step 2: lấy lại danh sách reservation ĐÃ được flip (Status=NoShow) để process side effects.
        var flippedReservations = await db.Reservations
            .Where(r => candidateIds.Contains(r.Id) && r.Status == ReservationStatus.NoShow)
            .Include(r => r.Lobby)
            .ToListAsync(ct);

        // Step 2b: GAP-NOSHOW-LOBBY-STATUS Fix (2026-10-02) — atomic flip Lobby.Status
        // từ các trạng thái chờ check-in (Open/Full/Viable/WaitingCheckIn) → TimeoutFailed.
        // Lý do: docs/time-slot-fixed-end-design.md §4.7 bước 2 yêu cầu "UPDATE Lobby.Status = TimeoutFailed"
        // khi reservation chuyển sang NoShow. Trước fix này, lobby vẫn giữ `WaitingCheckIn` → mobile app
        // hiển thị lobby đang chờ check-in mặc dù reservation backend đã NoShow → inconsistent state.
        // Cũng dùng ExecuteUpdateAsync WHERE Status IN (...) để cluster-safe: nếu 2 instance
        // cùng pick 1 batch, chỉ 1 flip được cho mỗi row (Postgres MVCC đảm bảo).
        var lobbyIds = flippedReservations
            .Where(r => r.LobbyId.HasValue)
            .Select(r => r.LobbyId!.Value)
            .Distinct()
            .ToList();

        var flippedLobbyCount = 0;
        if (lobbyIds.Count > 0)
        {
            // Các lobby status mà NoShow có thể transition: Open, Full, Viable, WaitingCheckIn.
            // KHÔNG touch terminal statuses (Closed, Cancelled, InProgress, ...).
            // FIX 2026-10-02: so sánh trực tiếp enum thay vì .ToString() — EF Core/Npgsql
            // không thể dịch `enum.ToString()` sang SQL (InvalidOperationException khi ExecuteUpdateAsync).
            var activeStatuses = new[]
            {
                LobbyStatus.Open,
                LobbyStatus.Full,
                LobbyStatus.Viable,
                LobbyStatus.WaitingCheckIn
            };
            var closedReasonNoShow = $"Reservation NoShow lúc {now:HH:mm dd/MM/yyyy} (quá 30 phút sau ScheduledStartTime).";

            flippedLobbyCount = await db.Lobbies
                .Where(l => lobbyIds.Contains(l.Id) && activeStatuses.Contains(l.Status))
                .ExecuteUpdateAsync(l => l
                    .SetProperty(x => x.Status, LobbyStatus.TimeoutFailed)
                    .SetProperty(x => x.ClosedAt, (DateTime?)now)
                    .SetProperty(x => x.ClosedReason, closedReasonNoShow)
                    .SetProperty(x => x.UpdatedAt, now),
                    ct);
        }

        _logger.LogInformation(
            "ReservationNoShowDetectionJob: atomic-flipped {Flipped} reservations + {LobbyCount} lobbies (from {Candidate} candidates); processing side effects.",
            flippedCount, flippedLobbyCount, noShowCandidates.Count);

        foreach (var reservation in flippedReservations)
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

        // GAP-R6-BJ-NOSHOW Fix: Status flip đã được thực hiện atomic ở RunDetectionAsync
        // (ExecuteUpdateAsync WHERE Status=Confirmed). KHÔNG mutate Status ở đây — tránh
        // EF tracker conflict với atomic UPDATE đã chạy.

        // 1. Forfeit deposit (BR-REFUND-03: 0% refund, BR-REFUND-05: BVC không rút về VND)
        //    Idempotency key `forfeit-{reservation.Id:N}` đảm bảo chỉ ghi ledger 1 lần
        //    (kể cả khi 2 instance cluster cùng chạy — chỉ 1 sẽ pass check).
        //    Skip nếu DepositAmount = 0 (đã được xử lý trước đó, ví dụ lobby merge).
        var forfeitIdempotencyKey = $"forfeit-{reservation.Id:N}";
        if (reservation.DepositAmount > 0)
        {
            await walletService.ForfeitDepositAsync(
                reservation.HostId,
                reservation.DepositAmount,
                reservation.LobbyId,
                reservation.Id,
                forfeitIdempotencyKey,
                ct);
        }

        // 2. Release seat + game inventory (chạy đúng 1 lần vì chỉ instance flip được mới gọi hàm này)
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

        // 5. Outbox event cho Reservation NoShow (host wallet, ledger forfeit, walk-in window).
        //    RealOutboxPublisher chưa có handler cho ReservationNoShow (enum=14) — chỉ log
        //    warning default. SignalR + push cho host sẽ được gửi qua event LobbyNoShow bên dưới.
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

        // 6. Outbox event cho Lobby cancellation (SignalR + push tới members).
        //    GAP-NOSHOW-LOBBY-STATUS Fix (2026-10-02): gửi kèm `LobbyNoShow` để:
        //      - NotifyLobbyCancelled broadcast tới SignalR group `lobby:{lobbyId}` (mobile app
        //        tự động refresh lobby status → TimeoutFailed thay vì WaitingCheckIn).
        //      - Push FCM tới host "No-show được ghi nhận" (giống flow LobbyCancelledByHost).
        //    Idempotency key `lobby-noshow-{reservation.Id:N}` khác với forfeit key để tránh
        //    dedup collision (cùng reservation nhưng 2 loại event khác nhau).
        if (reservation.LobbyId.HasValue)
        {
            var lobbyPayload = System.Text.Json.JsonSerializer.Serialize(new
            {
                reservationId = reservation.Id,
                lobbyId = reservation.LobbyId.Value,
                cafeId = reservation.CafeId,
                hostId = reservation.HostId,
                reason = "ReservationNoShow",
                noShowAt = now
            });

            await outboxRepo.AddAsync(new OutboxEvent
            {
                Id = Guid.NewGuid(),
                EventType = OutboxEventType.LobbyNoShow,
                Payload = lobbyPayload,
                IdempotencyKey = $"lobby-noshow-{reservation.Id:N}",
                LobbyId = reservation.LobbyId.Value,
                ReservationId = reservation.Id,
                UserId = reservation.HostId,
                CreatedAt = now
            });
        }

        _logger.LogInformation(
            "Reservation {Id} marked as NoShow. Forfeited {Bvc} BVC. Lobby {LobbyId} → TimeoutFailed.",
            reservation.Id, reservation.DepositAmount, reservation.LobbyId);
    }

    private async Task ReleaseInventoryAsync(Reservation reservation, DateTime now, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var seatInvRepo = scope.ServiceProvider.GetRequiredService<ISeatInventoryRepository>();
        var gameInvRepo = scope.ServiceProvider.GetRequiredService<IGameInventoryRepository>();

        // Release seats: use (cafeId, playDate, startTime, endTime)
        // FIX TZ-DT-UTC-03 (2026-10-02): wrap với CafeSchedule.ToVietnamLocal trước khi extract
        // TimeOnly, vì ScheduledStartTime/EndTime bây giờ Kind=Utc (UTC ticks), không phải VN
        // local. Nếu không wrap, lobby 20:45 VN sẽ bị coi như 20:45 UTC → sai inventory slot.
        var startTime = reservation.PreferredStartTime ?? TimeOnly.FromDateTime(CafeSchedule.ToVietnamLocal(reservation.ScheduledStartTime));
        var endTime = reservation.PreferredEndTime ?? TimeOnly.FromDateTime(CafeSchedule.ToVietnamLocal(reservation.ScheduledEndTime));
        var seatInv = await seatInvRepo.GetAsync(reservation.CafeId, reservation.PlayDate, startTime, endTime);
        if (seatInv != null)
        {
            // FIX 2026-10-02: AdjustCountersAsync — bypass EF tracker.
            await seatInvRepo.AdjustCountersAsync(
                seatInv.Id,
                heldDelta: -reservation.MaxPlayers,
                inUseDelta: 0,
                cancellationToken: ct);
        }

        // Release game copy: use (cafeId, gameId, playDate, startTime, endTime)
        var gameInv = await gameInvRepo.GetAsync(
            reservation.CafeId, reservation.GameId, reservation.PlayDate, startTime, endTime);
        if (gameInv != null)
        {
            await gameInvRepo.AdjustCountersAsync(
                gameInv.Id,
                heldDelta: -1,
                inUseDelta: 0,
                cancellationToken: ct);
        }
    }
}
