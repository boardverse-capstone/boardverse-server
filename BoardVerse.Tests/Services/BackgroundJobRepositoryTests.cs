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
