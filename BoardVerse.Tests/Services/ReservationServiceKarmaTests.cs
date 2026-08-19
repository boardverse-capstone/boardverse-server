using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Phase 1 TD-02 tests: Verify entity navigation and FK relationships work correctly
/// for the new ReservationId nullable FKs added to KarmaShortPlayRecord,
/// BookingRating, BookingNoShowVote, and BvcLedgerEntry.
///
/// These tests verify the EF Core model (in-memory) without needing full service construction.
/// </summary>
public class ReservationServiceKarmaTests : IDisposable
{
    private readonly BoardVerseDbContext _db;

    public ReservationServiceKarmaTests()
    {
        var options = new DbContextOptionsBuilder<BoardVerseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new BoardVerseDbContext(options);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task KarmaShortPlayRecord_Should_NavigateToReservation()
    {
        // Arrange: create a Reservation and link a KarmaShortPlayRecord via new ReservationId FK
        var userId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        var reservation = CreateMinimalReservation(reservationId);
        var record = new KarmaShortPlayRecord
        {
            Id = Guid.NewGuid(),
            ReservationId = reservationId,
            UserId = userId,
            PlayedMinutes = 60,
            ScheduledMinutes = 240,
            PlayedRatio = 0.25m,
            KarmaDelta = -5,
            CreatedAt = DateTime.UtcNow
        };

        _db.Reservations.Add(reservation);
        _db.KarmaShortPlayRecords.Add(record);
        await _db.SaveChangesAsync();

        // Act
        var loaded = await _db.KarmaShortPlayRecords
            .Include(r => r.Reservation)
            .FirstAsync(r => r.Id == record.Id);

        // Assert: new ReservationId FK navigation works
        Assert.NotNull(loaded.Reservation);
        Assert.Equal(reservationId, loaded.Reservation.Id);
    }

    [Fact]
    public async Task BookingNoShowVote_Should_SupportNewReservationIdFk()
    {
        // Arrange
        var reservationId = Guid.NewGuid();
        var voterUserId = Guid.NewGuid();
        var reservation = CreateMinimalReservation(reservationId);

        var vote = new BookingNoShowVote
        {
            Id = Guid.NewGuid(),
            BookingId = Guid.NewGuid(), // legacy FK still required
            ReservationId = reservationId, // Phase 1: new nullable FK
            VoterUserId = voterUserId,
            AbsentMemberIdsJson = "[]",
            VotedAt = DateTime.UtcNow
        };

        _db.Reservations.Add(reservation);
        _db.BookingNoShowVotes.Add(vote);
        await _db.SaveChangesAsync();

        // Act: query by new ReservationId
        var loaded = await _db.BookingNoShowVotes
            .Include(v => v.Reservation)
            .FirstAsync(v => v.ReservationId == reservationId);

        // Assert
        Assert.NotNull(loaded);
        Assert.NotNull(loaded.Reservation);
        Assert.Equal(reservationId, loaded.Reservation.Id);
    }

    [Fact]
    public async Task BookingRating_Should_SupportNewReservationIdFk()
    {
        // Arrange
        var reservationId = Guid.NewGuid();
        var raterUserId = Guid.NewGuid();
        var reservation = CreateMinimalReservation(reservationId);

        var rating = new BookingRating
        {
            Id = Guid.NewGuid(),
            BookingId = Guid.NewGuid(),
            ReservationId = reservationId, // Phase 1: new nullable FK
            VoterUserId = raterUserId,
            RatingsJson = "[{\"score\":5}]",
            SubmittedAt = DateTime.UtcNow,
            IsAggregated = false
        };

        _db.Reservations.Add(reservation);
        _db.BookingRatings.Add(rating);
        await _db.SaveChangesAsync();

        // Act
        var loaded = await _db.BookingRatings
            .Include(r => r.Reservation)
            .FirstAsync(r => r.ReservationId == reservationId);

        // Assert
        Assert.NotNull(loaded);
        Assert.NotNull(loaded.Reservation);
        Assert.Equal(reservationId, loaded.Reservation.Id);
        Assert.False(loaded.IsAggregated);
    }

    [Fact]
    public async Task BvcLedgerEntry_Should_SupportNewRelatedReservationId()
    {
        // Arrange
        var reservationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var reservation = CreateMinimalReservation(reservationId);

        var ledger = new BvcLedgerEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = LedgerEntryType.DepositHold, // enum
            Amount = 50_000,
            BalanceSnapshot = 150_000,
            IdempotencyKey = Guid.NewGuid().ToString(),
            RelatedReservationId = reservationId, // Phase 1: new nullable FK
            RelatedBookingId = null, // legacy path NOT used
            CreatedAt = DateTime.UtcNow
        };

        _db.Reservations.Add(reservation);
        _db.BvcLedgerEntries.Add(ledger);
        await _db.SaveChangesAsync();

        // Act: query by new RelatedReservationId
        var loaded = await _db.BvcLedgerEntries
            .FirstAsync(e => e.RelatedReservationId == reservationId);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(50_000, loaded.Amount);
        Assert.Null(loaded.RelatedBookingId); // legacy path not used
        Assert.Equal(LedgerEntryType.DepositHold, loaded.Type);
    }

    /// <summary>
    /// Helper: create a minimal Reservation entity valid for in-memory testing.
    /// Uses only required fields.
    /// </summary>
    private static Reservation CreateMinimalReservation(Guid reservationId)
    {
        return new Reservation
        {
            Id = reservationId,
            HostId = Guid.NewGuid(),
            CafeId = Guid.NewGuid(),
            GameId = Guid.NewGuid(),
            PlayDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            TimeSlot = TimeSlot.Morning,
            RecruitmentDeadline = DateTime.UtcNow.AddHours(1),
            ScheduledStartTime = DateTime.UtcNow.AddDays(1),
            ScheduledEndTime = DateTime.UtcNow.AddDays(1).AddHours(4),
            MinPlayers = 2,
            MaxPlayers = 4,
            DepositAmount = 50_000,
            MinDepositApplied = 0,
            RiskMultiplier = 1.0m,
            Status = ReservationStatus.Holding,
            CurrentPlayers = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
