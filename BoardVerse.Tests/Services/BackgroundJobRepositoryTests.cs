using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Phase 3 edge case tests: Background job logic (NoShow detection, Auto-release).
/// Tests verify the repository query logic that the jobs use.
/// </summary>
public class BackgroundJobRepositoryTests : IDisposable
{
    private readonly BoardVerseDbContext _db;

    public BackgroundJobRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new BoardVerseDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    #region ReservationNoShowCandidates

    [Fact]
    public async Task GetNoShowCandidates_Should_ReturnConfirmedPastDeadline()
    {
        // Arrange: reservation confirmed + past ScheduledStartTime + 30 min grace
        var past = DateTime.UtcNow.AddMinutes(-45);
        var reservation = CreateReservation(ReservationStatus.Confirmed, past);
        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync();

        // Act: simulate the job query
        var deadline = DateTime.UtcNow.AddMinutes(-30);
        var candidates = await _db.Reservations
            .Where(r => r.Status == ReservationStatus.Confirmed
                     && r.ScheduledStartTime <= deadline)
            .ToListAsync();

        // Assert
        Assert.Single(candidates);
        Assert.Equal(reservation.Id, candidates[0].Id);
    }

    [Fact]
    public async Task GetNoShowCandidates_Should_ExcludeOnTimeReservation()
    {
        // Arrange: confirmed but not past deadline yet
        var future = DateTime.UtcNow.AddMinutes(15);
        var reservation = CreateReservation(ReservationStatus.Confirmed, future);
        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync();

        // Act
        var deadline = DateTime.UtcNow.AddMinutes(-30);
        var candidates = await _db.Reservations
            .Where(r => r.Status == ReservationStatus.Confirmed
                     && r.ScheduledStartTime <= deadline)
            .ToListAsync();

        // Assert
        Assert.Empty(candidates);
    }

    [Fact]
    public async Task GetNoShowCandidates_Should_ExcludeNonConfirmedStatus()
    {
        // Arrange: Holding (not confirmed)
        var past = DateTime.UtcNow.AddMinutes(-45);
        var reservation = CreateReservation(ReservationStatus.Holding, past);
        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync();

        // Act
        var deadline = DateTime.UtcNow.AddMinutes(-30);
        var candidates = await _db.Reservations
            .Where(r => r.Status == ReservationStatus.Confirmed
                     && r.ScheduledStartTime <= deadline)
            .ToListAsync();

        // Assert
        Assert.Empty(candidates);
    }

    [Fact]
    public async Task GetNoShowCandidates_Should_ExcludeCheckedInReservation()
    {
        // Arrange: confirmed but already checked-in
        var past = DateTime.UtcNow.AddMinutes(-45);
        var reservation = CreateReservation(ReservationStatus.CheckedIn, past);
        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync();

        // Act
        var deadline = DateTime.UtcNow.AddMinutes(-30);
        var candidates = await _db.Reservations
            .Where(r => r.Status == ReservationStatus.Confirmed
                     && r.ScheduledStartTime <= deadline)
            .ToListAsync();

        // Assert
        Assert.Empty(candidates);
    }

    #endregion

    #region AutoReleaseExpiredSessions

    [Fact]
    public async Task GetExpiredSessions_Should_ReturnActivePastGrace()
    {
        // Arrange: active session linked to reservation via Lobby, past grace period
        var endTime = DateTime.UtcNow.AddMinutes(-45); // 45 min past end
        var reservation = CreateReservation(ReservationStatus.CheckedIn,
            DateTime.UtcNow.AddMinutes(-90));
        reservation.ScheduledEndTime = endTime;
        _db.Reservations.Add(reservation);

        var lobby = new Lobby
        {
            Id = Guid.NewGuid(),
            HostUserId = Guid.NewGuid(),
            ReservationId = reservation.Id,
            Status = LobbyStatus.InProgress
        };
        _db.Lobbies.Add(lobby);

        var session = new ActiveSession
        {
            Id = Guid.NewGuid(),
            LobbyId = lobby.Id,
            CafeId = reservation.CafeId,
            HostId = reservation.HostId,
            GameTemplateId = reservation.GameId,
            Status = GroupSessionStatus.Active,
            StartedAt = DateTime.UtcNow.AddHours(-5),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ActiveSessions.Add(session);
        await _db.SaveChangesAsync();

        // Act: simplified query - find session by LobbyId FK and check via Reservation
        // InMemory DB limitation: we test the JOIN logic directly
        var graceCutoff = DateTime.UtcNow.AddMinutes(-30);

        // Find all Active sessions with LobbyId
        var activeSessions = await _db.ActiveSessions
            .Where(s => s.Status == GroupSessionStatus.Active && s.LobbyId != null)
            .Select(s => new { s.Id, s.LobbyId })
            .ToListAsync();

        // Get Lobby -> Reservation for each session
        var expiredSessionIds = new List<Guid>();
        foreach (var s in activeSessions)
        {
            var lobbyForSession = await _db.Lobbies.FirstOrDefaultAsync(l => l.Id == s.LobbyId);
            if (lobbyForSession?.ReservationId != null)
            {
                var res = await _db.Reservations.FirstOrDefaultAsync(r => r.Id == lobbyForSession.ReservationId);
                if (res != null && res.ScheduledEndTime <= graceCutoff)
                {
                    expiredSessionIds.Add(s.Id);
                }
            }
        }

        // Assert
        Assert.Single(expiredSessionIds);
        Assert.Equal(session.Id, expiredSessionIds[0]);
    }

    [Fact]
    public async Task GetExpiredSessions_Should_ExcludeSessionStillInGrace()
    {
        // Arrange: active but within 30 min grace
        var endTime = DateTime.UtcNow.AddMinutes(-15);
        var reservation = CreateReservation(ReservationStatus.CheckedIn, DateTime.UtcNow.AddMinutes(-60));
        reservation.ScheduledEndTime = endTime;
        _db.Reservations.Add(reservation);

        var lobby = new Lobby
        {
            Id = Guid.NewGuid(), HostUserId = Guid.NewGuid(),
            ReservationId = reservation.Id, Status = LobbyStatus.InProgress
        };
        _db.Lobbies.Add(lobby);

        var session = new ActiveSession
        {
            Id = Guid.NewGuid(), LobbyId = lobby.Id,
            CafeId = reservation.CafeId, HostId = reservation.HostId,
            GameTemplateId = reservation.GameId,
            Status = GroupSessionStatus.Active,
            StartedAt = DateTime.UtcNow.AddHours(-1),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _db.ActiveSessions.Add(session);
        await _db.SaveChangesAsync();

        // Act: use manual query to avoid InMemory Include/ThenInclude limitation
        var graceCutoff = DateTime.UtcNow.AddMinutes(-30);
        var sessionIds = await _db.ActiveSessions
            .Where(s => s.Status == GroupSessionStatus.Active && s.LobbyId != null)
            .Select(s => s.Id)
            .ToListAsync();

        var lobbyIds = await _db.Lobbies
            .Where(l => sessionIds.Contains(l.Id) && l.ReservationId != null)
            .Select(l => l.ReservationId!.Value)
            .ToListAsync();

        var expiredSessionIds = await _db.Reservations
            .Where(r => lobbyIds.Contains(r.Id) && r.ScheduledEndTime <= graceCutoff)
            .SelectMany(r => _db.Lobbies.Where(l => l.ReservationId == r.Id))
            .Select(l => l.Id)
            .ToListAsync();

        var expired = await _db.ActiveSessions
            .Where(s => expiredSessionIds.Contains(s.Id))
            .ToListAsync();

        // Assert: within grace period so should be empty
        Assert.Empty(expired);
    }

    [Fact]
    public async Task GetExpiredSessions_Should_ExcludePaidSessions()
    {
        // Arrange: already paid
        var endTime = DateTime.UtcNow.AddMinutes(-45);
        var reservation = CreateReservation(ReservationStatus.CheckedIn, DateTime.UtcNow.AddMinutes(-90));
        reservation.ScheduledEndTime = endTime;
        _db.Reservations.Add(reservation);

        var lobby = new Lobby
        {
            Id = Guid.NewGuid(), HostUserId = Guid.NewGuid(),
            ReservationId = reservation.Id, Status = LobbyStatus.Closed
        };
        _db.Lobbies.Add(lobby);

        var session = new ActiveSession
        {
            Id = Guid.NewGuid(), LobbyId = lobby.Id,
            CafeId = reservation.CafeId, HostId = reservation.HostId,
            GameTemplateId = reservation.GameId,
            Status = GroupSessionStatus.Paid,
            StartedAt = DateTime.UtcNow.AddHours(-5),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _db.ActiveSessions.Add(session);
        await _db.SaveChangesAsync();

        // Act: use manual query to avoid InMemory Include/ThenInclude limitation
        var graceCutoff = DateTime.UtcNow.AddMinutes(-30);
        var sessionIds = await _db.ActiveSessions
            .Where(s => s.Status == GroupSessionStatus.Active && s.LobbyId != null)
            .Select(s => s.Id)
            .ToListAsync();

        var lobbyIds = await _db.Lobbies
            .Where(l => sessionIds.Contains(l.Id) && l.ReservationId != null)
            .Select(l => l.ReservationId!.Value)
            .ToListAsync();

        var expiredSessionIds = await _db.Reservations
            .Where(r => lobbyIds.Contains(r.Id) && r.ScheduledEndTime <= graceCutoff)
            .SelectMany(r => _db.Lobbies.Where(l => l.ReservationId == r.Id))
            .Select(l => l.Id)
            .ToListAsync();

        var expired = await _db.ActiveSessions
            .Where(s => expiredSessionIds.Contains(s.Id))
            .ToListAsync();

        // Assert: status is Paid not Active, should be empty
        Assert.Empty(expired);
    }

    [Fact]
    public async Task GetExpiredSessions_Should_ExcludeClosedSessions()
    {
        // Arrange: already closed
        var endTime = DateTime.UtcNow.AddMinutes(-45);
        var reservation = CreateReservation(ReservationStatus.CheckedIn, DateTime.UtcNow.AddMinutes(-90));
        reservation.ScheduledEndTime = endTime;
        _db.Reservations.Add(reservation);

        var lobby = new Lobby
        {
            Id = Guid.NewGuid(), HostUserId = Guid.NewGuid(),
            ReservationId = reservation.Id, Status = LobbyStatus.Closed
        };
        _db.Lobbies.Add(lobby);

        var session = new ActiveSession
        {
            Id = Guid.NewGuid(), LobbyId = lobby.Id,
            CafeId = reservation.CafeId, HostId = reservation.HostId,
            GameTemplateId = reservation.GameId,
            Status = GroupSessionStatus.Closed,
            StartedAt = DateTime.UtcNow.AddHours(-5),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        _db.ActiveSessions.Add(session);
        await _db.SaveChangesAsync();

        // Act: use manual query to avoid InMemory Include/ThenInclude limitation
        var graceCutoff = DateTime.UtcNow.AddMinutes(-30);
        var sessionIds = await _db.ActiveSessions
            .Where(s => s.Status == GroupSessionStatus.Active && s.LobbyId != null)
            .Select(s => s.Id)
            .ToListAsync();

        var lobbyIds = await _db.Lobbies
            .Where(l => sessionIds.Contains(l.Id) && l.ReservationId != null)
            .Select(l => l.ReservationId!.Value)
            .ToListAsync();

        var expiredSessionIds = await _db.Reservations
            .Where(r => lobbyIds.Contains(r.Id) && r.ScheduledEndTime <= graceCutoff)
            .SelectMany(r => _db.Lobbies.Where(l => l.ReservationId == r.Id))
            .Select(l => l.Id)
            .ToListAsync();

        var expired = await _db.ActiveSessions
            .Where(s => expiredSessionIds.Contains(s.Id))
            .ToListAsync();

        // Assert: status is Closed not Active, should be empty
        Assert.Empty(expired);
    }

    #endregion

    #region NoShow Lobby Status Transition (GAP-NOSHOW-LOBBY-STATUS 2026-10-02)

    /// <summary>
    /// GAP-NOSHOW-LOBBY-STATUS Fix (2026-10-02): Khi reservation chuyển sang NoShow,
    /// lobby liên kết cũng phải chuyển sang TimeoutFailed. Test verify query filter
    /// chỉ pick lobby ở trạng thái active (Open/Full/Viable/WaitingCheckIn), KHÔNG
    /// touch terminal statuses (Closed/HostCancelled/InProgress/etc).
    /// </summary>
    [Theory]
    [InlineData(LobbyStatus.Open, true)]
    [InlineData(LobbyStatus.Full, true)]
    [InlineData(LobbyStatus.Viable, true)]
    [InlineData(LobbyStatus.WaitingCheckIn, true)]
    [InlineData(LobbyStatus.Closed, false)] // terminal → skip
    [InlineData(LobbyStatus.HostCancelled, false)] // terminal → skip
    [InlineData(LobbyStatus.TimeoutFailed, false)] // already terminal → skip
    [InlineData(LobbyStatus.InProgress, false)] // đã check-in, không touch
    [InlineData(LobbyStatus.RatingOpen, false)] // đang trong rating window
    [InlineData(LobbyStatus.RejectedByCafe, false)] // terminal (cafe reject)
    [InlineData(LobbyStatus.ExpiredByCafe, false)] // terminal (cafe không duyệt)
    [InlineData(LobbyStatus.Dissolved, false)] // host dissolve
    [InlineData(LobbyStatus.PendingCafeApproval, false)] // chưa publish
    [InlineData(LobbyStatus.PendingActivation, false)] // đang atomic txn
    public async Task NoShowLobbyFlip_Should_OnlyPickActiveStatuses(LobbyStatus lobbyStatus, bool shouldBePicked)
    {
        // Arrange: lobby with given status
        var lobby = new Lobby
        {
            Id = Guid.NewGuid(),
            HostUserId = Guid.NewGuid(),
            GameTemplateId = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            Status = lobbyStatus,
            MaxMembers = 4,
            MinPlayers = 2,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Lobbies.Add(lobby);
        await _db.SaveChangesAsync();

        // Act: query giống hệt ReservationNoShowDetectionJob — chỉ flip active statuses.
        var activeStatuses = new[]
        {
            LobbyStatus.Open.ToString(),
            LobbyStatus.Full.ToString(),
            LobbyStatus.Viable.ToString(),
            LobbyStatus.WaitingCheckIn.ToString()
        };

        var pickedLobbies = await _db.Lobbies
            .Where(l => l.Id == lobby.Id && activeStatuses.Contains(l.Status.ToString()))
            .ToListAsync();

        // Assert
        if (shouldBePicked)
        {
            Assert.Single(pickedLobbies);
            Assert.Equal(lobby.Id, pickedLobbies[0].Id);
        }
        else
        {
            Assert.Empty(pickedLobbies);
        }
    }

    /// <summary>
    /// GAP-NOSHOW-LOBBY-STATUS Fix (2026-10-02): Reservation ở status NoShow + Lobby
    /// ở WaitingCheckIn → sau khi job chạy, lobby phải ở TimeoutFailed (transition đúng).
    /// </summary>
    [Fact]
    public async Task NoShowFlip_Should_TransitionLobby_FromWaitingCheckIn_ToTimeoutFailed()
    {
        // Arrange: reservation đã NoShow + lobby WaitingCheckIn (case thực tế user report).
        var reservation = CreateReservation(ReservationStatus.NoShow, DateTime.UtcNow.AddHours(-1));
        var lobby = new Lobby
        {
            Id = Guid.NewGuid(),
            HostUserId = reservation.HostId,
            GameTemplateId = reservation.GameId,
            CafeId = reservation.CafeId,
            ReservationId = reservation.Id,
            Status = LobbyStatus.WaitingCheckIn,
            MaxMembers = reservation.MaxPlayers,
            MinPlayers = reservation.MinPlayers,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Reservations.Add(reservation);
        _db.Lobbies.Add(lobby);
        await _db.SaveChangesAsync();

        // Act: simulate the job's atomic flip logic.
        var now = DateTime.UtcNow;
        var activeStatuses = new[]
        {
            LobbyStatus.Open.ToString(),
            LobbyStatus.Full.ToString(),
            LobbyStatus.Viable.ToString(),
            LobbyStatus.WaitingCheckIn.ToString()
        };
        var closedReason = $"Reservation NoShow lúc {now:HH:mm dd/MM/yyyy} (quá 30 phút sau ScheduledStartTime).";

        // InMemory không support ExecuteUpdateAsync — simulate bằng cách load + update.
        // Trong production, ReservationNoShowDetectionJob dùng ExecuteUpdateAsync WHERE Status IN (activeStatuses)
        // để cluster-safe atomic flip.
        var flippedLobby = await _db.Lobbies
            .Where(l => l.Id == lobby.Id && activeStatuses.Contains(l.Status.ToString()))
            .FirstOrDefaultAsync();

        Assert.NotNull(flippedLobby);
        flippedLobby.Status = LobbyStatus.TimeoutFailed;
        flippedLobby.ClosedAt = now;
        flippedLobby.ClosedReason = closedReason;
        flippedLobby.UpdatedAt = now;
        await _db.SaveChangesAsync();

        // Assert: verify final state
        var result = await _db.Lobbies.FindAsync(lobby.Id);
        Assert.NotNull(result);
        Assert.Equal(LobbyStatus.TimeoutFailed, result!.Status);
        Assert.NotNull(result.ClosedAt);
        Assert.Contains("NoShow", result.ClosedReason);
    }

    /// <summary>
    /// GAP-NOSHOW-LOBBY-STATUS Fix (2026-10-02): Lobby đã ở Closed (terminal) thì KHÔNG
    /// flip lại TimeoutFailed — đây là idempotent safety để tránh regression nếu job chạy
    /// 2 lần hoặc sau khi lobby đã đóng theo flow khác.
    /// </summary>
    [Fact]
    public async Task NoShowFlip_Should_NotTouchAlreadyTerminalLobbies()
    {
        // Arrange: lobby đã Closed trước đó (vd: lobby đã được settle xong rồi)
        var reservation = CreateReservation(ReservationStatus.NoShow, DateTime.UtcNow.AddHours(-1));
        var lobby = new Lobby
        {
            Id = Guid.NewGuid(),
            HostUserId = reservation.HostId,
            GameTemplateId = reservation.GameId,
            CafeId = reservation.CafeId,
            ReservationId = reservation.Id,
            Status = LobbyStatus.Closed, // already terminal
            MaxMembers = reservation.MaxPlayers,
            MinPlayers = reservation.MinPlayers,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Reservations.Add(reservation);
        _db.Lobbies.Add(lobby);
        await _db.SaveChangesAsync();

        // Act
        var activeStatuses = new[]
        {
            LobbyStatus.Open.ToString(),
            LobbyStatus.Full.ToString(),
            LobbyStatus.Viable.ToString(),
            LobbyStatus.WaitingCheckIn.ToString()
        };

        var flippedLobby = await _db.Lobbies
            .Where(l => l.Id == lobby.Id && activeStatuses.Contains(l.Status.ToString()))
            .FirstOrDefaultAsync();

        // Assert: Closed status không bị flip → query trả empty
        Assert.Null(flippedLobby);
    }

    #endregion

    #region WalkInWindow Overlap Query

    [Fact]
    public async Task WalkInWindow_OverlapQuery_Should_FindOverlappingWindow()
    {
        // Arrange: existing window from 10:00 to 13:00
        var cafeId = Guid.NewGuid();
        var existing = new WalkInWindow
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            WindowStart = DateTime.UtcNow.Date.AddHours(10),
            WindowEnd = DateTime.UtcNow.Date.AddHours(13),
            TotalSeats = 4,
            AvailableSeats = 4,
            HeldSeats = 0,
            InUseSeats = 0,
            Status = WalkInWindowStatus.Available,
            ExpiresAt = DateTime.UtcNow.AddHours(5),
            CreatedAt = DateTime.UtcNow
        };
        _db.WalkInWindows.Add(existing);
        await _db.SaveChangesAsync();

        // Act: check overlap with a new window from 12:00 to 14:00
        var overlap = await _db.WalkInWindows
            .Where(w => w.CafeId == cafeId
                     && w.Status == WalkInWindowStatus.Available
                     && w.WindowStart < DateTime.UtcNow.Date.AddHours(14)
                     && w.WindowEnd > DateTime.UtcNow.Date.AddHours(12))
            .ToListAsync();

        // Assert
        Assert.Single(overlap);
        Assert.Equal(existing.Id, overlap[0].Id);
    }

    [Fact]
    public async Task WalkInWindow_OverlapQuery_Should_NotFindNonOverlappingWindow()
    {
        // Arrange: existing window from 10:00 to 12:00
        var cafeId = Guid.NewGuid();
        var existing = new WalkInWindow
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            WindowStart = DateTime.UtcNow.Date.AddHours(10),
            WindowEnd = DateTime.UtcNow.Date.AddHours(12),
            TotalSeats = 4,
            AvailableSeats = 4,
            HeldSeats = 0,
            InUseSeats = 0,
            Status = WalkInWindowStatus.Available,
            ExpiresAt = DateTime.UtcNow.AddHours(5),
            CreatedAt = DateTime.UtcNow
        };
        _db.WalkInWindows.Add(existing);
        await _db.SaveChangesAsync();

        // Act: check overlap with a non-overlapping window from 13:00 to 15:00
        var overlap = await _db.WalkInWindows
            .Where(w => w.CafeId == cafeId
                     && w.Status == WalkInWindowStatus.Available
                     && w.WindowStart < DateTime.UtcNow.Date.AddHours(15)
                     && w.WindowEnd > DateTime.UtcNow.Date.AddHours(13))
            .ToListAsync();

        // Assert
        Assert.Empty(overlap);
    }

    #endregion

    #region TZ-LOBBY-NOTIF-01 — LobbyNotificationJob Timezone Fix (2026-10-03)

    /// <summary>
    /// FIX TZ-LOBBY-NOTIF-01 (2026-10-03): Trước đây LobbyNotificationJob tính
    /// <c>scheduledTime = lobby.PlayDate.Value.ToDateTime(lobby.PreferredStartTime.Value)</c>
    /// → Kind=Unspecified, raw ticks = VN local wall clock. Khi so với <c>DateTime.UtcNow</c>
    /// trong ProcessMilestoneAsync, C# không convert Kind → lệch 7 giờ.
    ///
    /// Test này verify rằng với lobby có <c>ScheduledStartTime</c> Kind=Utc (post-fix), logic
    /// resolve cho ra cùng giá trị raw ticks (UTC), dùng được so sánh với <c>DateTime.UtcNow</c>
    /// mà không lệch 7h.
    /// </summary>
    [Fact]
    public void ResolveScheduledStartUtc_Should_ReturnUtcTicks_WhenScheduledStartTimeIsUtc()
    {
        // Arrange: lobby với ScheduledStartTime = 20:45 UTC (đã là Kind=Utc sau fix).
        // (User VN nhập 03:45 ngày hôm sau, sau khi convert VN→UTC sẽ ra 20:45 UTC cùng ngày.)
        var lobby = new Lobby
        {
            Id = Guid.NewGuid(),
            HostUserId = Guid.NewGuid(),
            GameTemplateId = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            PlayDate = new DateOnly(2026, 10, 3),
            PreferredStartTime = new TimeOnly(3, 45),
            PreferredEndTime = new TimeOnly(7, 45),
            ScheduledStartTime = new DateTime(2026, 10, 3, 20, 45, 0, DateTimeKind.Utc),
            Status = LobbyStatus.Open,
            MaxMembers = 4,
            MinPlayers = 2,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act: compute giống hệt logic trong LobbyNotificationJob.ResolveScheduledStartUtc.
        // (Inline để test không phụ thuộc vào internal method của BackgroundService class.)
        DateTime resolved;
        if (lobby.ScheduledStartTime.HasValue)
        {
            resolved = BoardVerse.Core.Constants.CafeSchedule.ToUtcAssumingVietnamLocal(
                lobby.ScheduledStartTime.Value);
        }
        else
        {
            var (start, _) = BoardVerse.Core.Constants.CafeSchedule.BuildScheduledStartEndFromPreferred(
                lobby.PlayDate!.Value, lobby.PreferredStartTime!.Value, lobby.PreferredEndTime!.Value);
            resolved = start;
        }

        // Assert: Kind phải là Utc, raw ticks phải khớp 20:45 UTC (không offset 7h).
        Assert.Equal(DateTimeKind.Utc, resolved.Kind);
        Assert.Equal(
            new DateTime(2026, 10, 3, 20, 45, 0, DateTimeKind.Utc).Ticks,
            resolved.Ticks);
    }

    /// <summary>
    /// FIX TZ-LOBBY-NOTIF-01 (2026-10-03): Test phương án fallback — lobby cũ chưa có
    /// <c>ScheduledStartTime</c> nhưng có <c>PlayDate + PreferredStartTime</c>. Helper phải
    /// build Kind=Utc bằng <see cref="BoardVerse.Core.Constants.CafeSchedule.BuildScheduledStartEndFromPreferred"/>.
    /// Trước fix: job dùng raw <c>playDate.ToDateTime(preferredStart)</c> → Kind=Unspecified.
    /// </summary>
    [Fact]
    public void ResolveScheduledStartUtc_Should_BuildUtcFromLegacyFields_WhenScheduledStartTimeMissing()
    {
        // Arrange: lobby cũ (legacy) chỉ có PlayDate + PreferredStartTime (UTC chưa được set).
        // User VN nhập 20:45 ngày 03/10/2026 → build Kind=Utc phải ra 13:45 UTC cùng ngày.
        var lobby = new Lobby
        {
            Id = Guid.NewGuid(),
            HostUserId = Guid.NewGuid(),
            GameTemplateId = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            PlayDate = new DateOnly(2026, 10, 3),
            PreferredStartTime = new TimeOnly(20, 45),
            PreferredEndTime = new TimeOnly(23, 45),
            // ScheduledStartTime null → fallback vào BuildScheduledStartEndFromPreferred.
            Status = LobbyStatus.Open,
            MaxMembers = 4,
            MinPlayers = 2,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act
        DateTime resolved;
        if (lobby.ScheduledStartTime.HasValue)
        {
            resolved = BoardVerse.Core.Constants.CafeSchedule.ToUtcAssumingVietnamLocal(
                lobby.ScheduledStartTime.Value);
        }
        else
        {
            var (start, _) = BoardVerse.Core.Constants.CafeSchedule.BuildScheduledStartEndFromPreferred(
                lobby.PlayDate!.Value, lobby.PreferredStartTime!.Value, lobby.PreferredEndTime!.Value);
            resolved = start;
        }

        // Assert: Kind=Utc, raw ticks = 13:45 (20:45 VN - 7h = 13:45 UTC).
        Assert.Equal(DateTimeKind.Utc, resolved.Kind);
        Assert.Equal(
            new DateTime(2026, 10, 3, 13, 45, 0, DateTimeKind.Utc).Ticks,
            resolved.Ticks);
    }

    /// <summary>
    /// FIX TZ-LOBBY-NOTIF-01 (2026-10-03): Reproduce lỗi cũ để verify contract.
    /// Nếu dev quên fix và revert về code cũ (<c>playDate.ToDateTime(preferredStart)</c>),
    /// result sẽ là Kind=Unspecified với raw ticks = 20:45 (VN local) — KHÔNG PHẢI 13:45 (UTC).
    /// </summary>
    [Fact]
    public void OldLogic_Should_ReturnUnspecified_VnLocalTicks_WhichCauses7hOffset()
    {
        // Arrange
        var playDate = new DateOnly(2026, 10, 3);
        var preferredStart = new TimeOnly(20, 45);

        // Act: code cũ (trước fix)
        var oldResult = playDate.ToDateTime(preferredStart);

        // Assert: Kind=Unspecified, raw ticks = 20:45 (không phải 13:45).
        Assert.Equal(DateTimeKind.Unspecified, oldResult.Kind);
        Assert.Equal(
            new DateTime(2026, 10, 3, 20, 45, 0, DateTimeKind.Unspecified).Ticks,
            oldResult.Ticks);

        // Demonstrate: subtract raw này với DateTime.UtcNow lệch 7h.
        // 20:45 (Unspecified, raw VN local) - 13:45 (Utc, raw UTC) = 7h00
        // → ProcessMilestoneAsync sẽ trigger sớm hơn 7 tiếng.
        var utcNow = new DateTime(2026, 10, 3, 13, 45, 0, DateTimeKind.Utc);
        var diff = (oldResult - utcNow).TotalHours;
        Assert.Equal(7.0, diff, precision: 1);
    }

    /// <summary>
    /// FIX TZ-LOBBY-NOTIF-01 (2026-10-03): End-to-end scenario thực tế.
    /// Lobby có <c>ScheduledStartTime = 13:45 UTC</c> (= 20:45 VN sau khi Reservation service convert).
    /// Bug cũ: <c>playDate.ToDateTime(preferredStart)</c> cho ra 20:45 (Unspecified, raw VN local).
    /// So với <c>DateTime.UtcNow</c> không convert Kind → trigger lệch 7 tiếng.
    /// </summary>
    [Fact]
    public void MilestoneCheck_WithUtcConversion_Should_NotTrigger7hLate()
    {
        // Arrange: lobby scheduled 20:45 VN ngày 03/10/2026 = 13:45 UTC.
        var lobby = new Lobby
        {
            Id = Guid.NewGuid(),
            HostUserId = Guid.NewGuid(),
            GameTemplateId = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            PlayDate = new DateOnly(2026, 10, 3),
            PreferredStartTime = new TimeOnly(20, 45), // user nhập giờ VN
            PreferredEndTime = new TimeOnly(23, 45),
            ScheduledStartTime = new DateTime(2026, 10, 3, 13, 45, 0, DateTimeKind.Utc), // 20:45 VN → 13:45 UTC
            Status = LobbyStatus.Open,
            MaxMembers = 4,
            MinPlayers = 2,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act (FIX): resolve scheduledTime về Kind=Utc.
        DateTime scheduledTime;
        if (lobby.ScheduledStartTime.HasValue)
        {
            scheduledTime = BoardVerse.Core.Constants.CafeSchedule.ToUtcAssumingVietnamLocal(
                lobby.ScheduledStartTime.Value);
        }
        else
        {
            var (start, _) = BoardVerse.Core.Constants.CafeSchedule.BuildScheduledStartEndFromPreferred(
                lobby.PlayDate!.Value, lobby.PreferredStartTime!.Value, lobby.PreferredEndTime!.Value);
            scheduledTime = start;
        }
        Assert.Equal(DateTimeKind.Utc, scheduledTime.Kind);
        Assert.Equal(new DateTime(2026, 10, 3, 13, 45, 0, DateTimeKind.Utc), scheduledTime);

        // Scenario: hiện tại 18:45 UTC (= 01:45 VN ngày 04/10).
        // Real targetTime (2h trước scheduledStart) = 11:45 UTC → diff = -7h → KHÔNG trigger (đã quá muộn).
        var now = new DateTime(2026, 10, 3, 18, 45, 0, DateTimeKind.Utc);
        var realTargetTime = scheduledTime - TimeSpan.FromHours(2);
        var realDiff = (realTargetTime - now).TotalMinutes; // -420 phút (= -7h)
        var realShouldTrigger = Math.Abs(realDiff) <= 5.0;
        Assert.False(realShouldTrigger,
            "Real schedule đã qua 7 tiếng → milestone 2h trước KHÔNG trigger.");

        // Demonstrate BUG: dùng logic cũ (Unspecified raw VN), targetTime = 18:45 (Unspecified raw).
        var oldLogic = lobby.PlayDate!.Value.ToDateTime(lobby.PreferredStartTime!.Value);
        var oldTargetTime = oldLogic - TimeSpan.FromHours(2); // 18:45 Unspecified raw
        // 18:45 Unspecified raw - 18:45 UTC raw = 0 phút (lệch 7h so với real target 11:45 UTC).
        // C# DateTime subtraction KHÔNG convert Kind → so sánh raw ticks.
        var oldDiff = (oldTargetTime - now).TotalMinutes;
        var oldShouldTrigger = Math.Abs(oldDiff) <= 5.0;

        // Bug: old logic triggers 7 tiếng trễ so với real trigger window.
        Assert.True(oldShouldTrigger,
            "BUG: logic cũ trigger milestone sai lệch 7 tiếng do Kind=Unspecified raw subtraction.");
        Assert.True(Math.Abs(oldDiff - realDiff) >= 7 * 60 - 1,
            $"BUG: |oldDiff - realDiff| = {Math.Abs(oldDiff - realDiff)} phải >= 7 tiếng.");
    }

    #endregion

    #region Helpers

    private static Reservation CreateReservation(ReservationStatus status, DateTime scheduledStart)
    {
        return new Reservation
        {
            Id = Guid.NewGuid(),
            HostId = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            GameId = Guid.NewGuid(),
            PlayDate = DateOnly.FromDateTime(scheduledStart),
            TimeSlot = TimeSlot.Morning,
            ScheduledStartTime = scheduledStart,
            ScheduledEndTime = scheduledStart.AddHours(4),
            Status = status,
            MinPlayers = 2,
            MaxPlayers = 4,
            DepositAmount = 100,
            MinDepositApplied = 0,
            RiskMultiplier = 1.0m,
            CurrentPlayers = 4,
            RecruitmentDeadline = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    #endregion
}
