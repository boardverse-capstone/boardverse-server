using System.Text.Json;
using BoardVerse.Core.DTOs.Booking;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho <see cref="BookingRatingService.AggregateBookingOutcomesAsync"/>.
///
/// Tập trung vào:
/// - Cross-rating aggregate: delta = (avg - 3.0) * 10, ghi KarmaLog + update profile.
/// - No-show vote aggregate: threshold > totalMembers/2.
/// - Forfeit deposit chỉ khi RefundPolicy = None.
/// - Idempotency: rating rows có IsAggregated = true sẽ bị skip.
/// - Conflict: booking ở PendingDeposit / Cancelled không được aggregate.
/// </summary>
public class BookingRatingServiceAggregationTests
{
    private readonly Mock<IBookingRepository> _mockBookingRepo;
    private readonly Mock<ILobbyRepository> _mockLobbyRepo;
    private readonly Mock<IBookingNoShowVoteRepository> _mockNoShowRepo;
    private readonly Mock<IBookingRatingRepository> _mockRatingRepo;
    private readonly Mock<IBookingDepositRepository> _mockDepositRepo;
    private readonly Mock<IKarmaRatingRepository> _mockKarmaRepo;
    private readonly Mock<IUserProfileRepository> _mockUserProfileRepo;
    private readonly Mock<ILogger<BookingRatingService>> _mockLogger;
    private readonly BookingRatingService _service;

    public BookingRatingServiceAggregationTests()
    {
        _mockBookingRepo = new Mock<IBookingRepository>();
        _mockLobbyRepo = new Mock<ILobbyRepository>();
        _mockNoShowRepo = new Mock<IBookingNoShowVoteRepository>();
        _mockRatingRepo = new Mock<IBookingRatingRepository>();
        _mockDepositRepo = new Mock<IBookingDepositRepository>();
        _mockKarmaRepo = new Mock<IKarmaRatingRepository>();
        _mockUserProfileRepo = new Mock<IUserProfileRepository>();
        _mockLogger = new Mock<ILogger<BookingRatingService>>();

        // M5: GetProfilesByUserIdsAsync returns empty by default — tests that need profiles override.
        _mockUserProfileRepo
            .Setup(r => r.GetProfilesByUserIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .ReturnsAsync(new Dictionary<Guid, UserProfile>());

        _service = new BookingRatingService(
            _mockBookingRepo.Object,
            _mockLobbyRepo.Object,
            _mockNoShowRepo.Object,
            _mockRatingRepo.Object,
            _mockDepositRepo.Object,
            _mockKarmaRepo.Object,
            _mockUserProfileRepo.Object,
            _mockLogger.Object);
    }

    // ===========================================================================
    // 1. Conflict
    // ===========================================================================

    [Fact]
    public async Task Aggregate_BookingPendingDeposit_ThrowsConflict()
    {
        var bookingId = Guid.NewGuid();
        _mockBookingRepo.Setup(r => r.GetByIdAsync(bookingId, true))
            .ReturnsAsync(BuildBooking(bookingId, BookingStatus.PendingDeposit));

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.AggregateBookingOutcomesAsync(bookingId));
    }

    [Fact]
    public async Task Aggregate_BookingCancelled_ThrowsConflict()
    {
        var bookingId = Guid.NewGuid();
        _mockBookingRepo.Setup(r => r.GetByIdAsync(bookingId, true))
            .ReturnsAsync(BuildBooking(bookingId, BookingStatus.Cancelled));

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.AggregateBookingOutcomesAsync(bookingId));
    }

    [Fact]
    public async Task Aggregate_BookingNotFound_ThrowsNotFound()
    {
        var bookingId = Guid.NewGuid();
        _mockBookingRepo.Setup(r => r.GetByIdAsync(bookingId, true))
            .ReturnsAsync((Booking?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.AggregateBookingOutcomesAsync(bookingId));
    }

    // ===========================================================================
    // 2. Cross-rating aggregate
    // ===========================================================================

    [Fact]
    public async Task Aggregate_CrossRating_AverageAboveThree_AddsKarmaToTargetUser()
    {
        var bookingId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var host = Guid.NewGuid();
        var member1 = Guid.NewGuid();
        var member2 = Guid.NewGuid();

        var booking = BuildBooking(bookingId, BookingStatus.CheckedIn, lobbyId);
        _mockBookingRepo.Setup(r => r.GetByIdAsync(bookingId, true)).ReturnsAsync(booking);

        _mockLobbyRepo.Setup(r => r.GetByIdWithMembersAsync(lobbyId))
            .ReturnsAsync(BuildLobby(lobbyId, host, member1, member2));

        // Voter host rates member1 với avg=4.0 → delta = (4-3)*10 = +10.
        var ratingRow = new BookingRating
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId,
            VoterUserId = host,
            RatingsJson = JsonSerializer.Serialize(new List<BookingRatingItemDto>
            {
                new() { RatedUserId = member1, Attitude = 4, Sportsmanship = 4, Punctuality = 4 }
            }),
            IsAggregated = false
        };
        _mockRatingRepo.Setup(r => r.GetUnaggregatedByBookingAsync(bookingId))
            .ReturnsAsync(new List<BookingRating> { ratingRow });
        _mockNoShowRepo.Setup(r => r.GetByBookingAsync(bookingId))
            .ReturnsAsync(new List<BookingNoShowVote>());

        var profile = new UserProfile { UserId = member1, KarmaPoints = 100 };
        _mockKarmaRepo.Setup(k => k.GetProfileForUpdateAsync(member1)).ReturnsAsync(profile);

        var result = await _service.AggregateBookingOutcomesAsync(bookingId);

        Assert.Equal(1, result.RatingsProcessed);
        Assert.Equal(10m, result.KarmaDeltaByUser[member1]);
        Assert.Equal(110, profile.KarmaPoints);
        Assert.Empty(result.NoShowConfirmedMembers);

        _mockKarmaRepo.Verify(k => k.AddKarmaLogAsync(It.Is<KarmaLog>(log =>
            log.UserId == member1
            && log.KarmaPointsChange == 10
            && log.KarmaBefore == 100
            && log.KarmaAfter == 110
            && log.Source == KarmaLogSource.PlayerCrossRating
            && log.ViolationCategory == KarmaViolationCategory.CrossRating
        )), Times.Once);

        Assert.True(ratingRow.IsAggregated);
        _mockRatingRepo.Verify(r => r.UpdateAsync(ratingRow), Times.Once);
    }

    [Fact]
    public async Task Aggregate_CrossRating_AverageBelowThree_SubtractsKarma()
    {
        var bookingId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var host = Guid.NewGuid();
        var target = Guid.NewGuid();

        var booking = BuildBooking(bookingId, BookingStatus.CheckedIn, lobbyId);
        _mockBookingRepo.Setup(r => r.GetByIdAsync(bookingId, true)).ReturnsAsync(booking);
        _mockLobbyRepo.Setup(r => r.GetByIdWithMembersAsync(lobbyId))
            .ReturnsAsync(BuildLobby(lobbyId, host, target));

        // avg = (2+2+2)/3 = 2.0 → delta = (2-3)*10 = -10.
        var ratingRow = new BookingRating
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId,
            VoterUserId = host,
            RatingsJson = JsonSerializer.Serialize(new List<BookingRatingItemDto>
            {
                new() { RatedUserId = target, Attitude = 2, Sportsmanship = 2, Punctuality = 2 }
            }),
            IsAggregated = false
        };
        _mockRatingRepo.Setup(r => r.GetUnaggregatedByBookingAsync(bookingId))
            .ReturnsAsync(new List<BookingRating> { ratingRow });
        _mockNoShowRepo.Setup(r => r.GetByBookingAsync(bookingId))
            .ReturnsAsync(new List<BookingNoShowVote>());

        var profile = new UserProfile { UserId = target, KarmaPoints = 100 };
        _mockKarmaRepo.Setup(k => k.GetProfileForUpdateAsync(target)).ReturnsAsync(profile);

        var result = await _service.AggregateBookingOutcomesAsync(bookingId);

        Assert.Equal(-10m, result.KarmaDeltaByUser[target]);
        Assert.Equal(90, profile.KarmaPoints);
        Assert.Equal(-10m, result.TotalKarmaDelta);
    }

    [Fact]
    public async Task Aggregate_CrossRating_MultipleVoters_AveragesScores()
    {
        var bookingId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var target = Guid.NewGuid();

        var booking = BuildBooking(bookingId, BookingStatus.CheckedIn, lobbyId);
        _mockBookingRepo.Setup(r => r.GetByIdAsync(bookingId, true)).ReturnsAsync(booking);
        _mockLobbyRepo.Setup(r => r.GetByIdWithMembersAsync(lobbyId))
            .ReturnsAsync(BuildLobby(lobbyId, v1, v2, target));

        // v1 votes 4.0 (5+4+3)/3, v2 votes 2.0 (2+2+2)/3 → mean = 3.0 → delta = 0.
        var rows = new List<BookingRating>
        {
            new()
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                VoterUserId = v1,
                RatingsJson = JsonSerializer.Serialize(new List<BookingRatingItemDto>
                {
                    new() { RatedUserId = target, Attitude = 5, Sportsmanship = 4, Punctuality = 3 }
                }),
                IsAggregated = false
            },
            new()
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                VoterUserId = v2,
                RatingsJson = JsonSerializer.Serialize(new List<BookingRatingItemDto>
                {
                    new() { RatedUserId = target, Attitude = 2, Sportsmanship = 2, Punctuality = 2 }
                }),
                IsAggregated = false
            }
        };
        _mockRatingRepo.Setup(r => r.GetUnaggregatedByBookingAsync(bookingId)).ReturnsAsync(rows);
        _mockNoShowRepo.Setup(r => r.GetByBookingAsync(bookingId))
            .ReturnsAsync(new List<BookingNoShowVote>());

        var profile = new UserProfile { UserId = target, KarmaPoints = 50 };
        _mockKarmaRepo.Setup(k => k.GetProfileForUpdateAsync(target)).ReturnsAsync(profile);

        var result = await _service.AggregateBookingOutcomesAsync(bookingId);

        // mean = (4.0 + 2.0)/2 = 3.0 → delta = 0.
        Assert.Equal(0m, result.KarmaDeltaByUser[target]);
        Assert.Equal(50, profile.KarmaPoints);
    }

    // ===========================================================================
    // 3. No-show aggregate
    // ===========================================================================

    [Fact]
    public async Task Aggregate_NoShowConfirmed_AbsentVotesOverHalf_SubtractsTenKarma()
    {
        var bookingId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var noShowMember = Guid.NewGuid();

        var booking = BuildBooking(bookingId, BookingStatus.CheckedIn, lobbyId);
        _mockBookingRepo.Setup(r => r.GetByIdAsync(bookingId, true)).ReturnsAsync(booking);
        _mockLobbyRepo.Setup(r => r.GetByIdWithMembersAsync(lobbyId))
            .ReturnsAsync(BuildLobby(lobbyId, v1, v2, noShowMember));

        _mockRatingRepo.Setup(r => r.GetUnaggregatedByBookingAsync(bookingId))
            .ReturnsAsync(new List<BookingRating>());

        // 2/3 voters vote noShowMember absent → threshold = 3/2 = 1 → 2 > 1 → confirmed.
        var votes = new List<BookingNoShowVote>
        {
            new() { BookingId = bookingId, VoterUserId = v1, AbsentMemberIdsJson = JsonSerializer.Serialize(new List<Guid> { noShowMember }) },
            new() { BookingId = bookingId, VoterUserId = v2, AbsentMemberIdsJson = JsonSerializer.Serialize(new List<Guid> { noShowMember }) }
        };
        _mockNoShowRepo.Setup(r => r.GetByBookingAsync(bookingId)).ReturnsAsync(votes);
        _mockDepositRepo.Setup(r => r.GetByBookingIdAsync(bookingId)).ReturnsAsync((BookingDeposit?)null);

        var profile = new UserProfile { UserId = noShowMember, KarmaPoints = 100 };
        _mockKarmaRepo.Setup(k => k.GetProfileForUpdateAsync(noShowMember)).ReturnsAsync(profile);

        var result = await _service.AggregateBookingOutcomesAsync(bookingId);

        Assert.Single(result.NoShowConfirmedMembers);
        Assert.Contains(noShowMember, result.NoShowConfirmedMembers);
        Assert.Equal(-10m, result.KarmaDeltaByUser[noShowMember]);
        Assert.Equal(90, profile.KarmaPoints);

        _mockKarmaRepo.Verify(k => k.AddKarmaLogAsync(It.Is<KarmaLog>(log =>
            log.UserId == noShowMember
            && log.KarmaPointsChange == -10
            && log.Source == KarmaLogSource.SystemAutomatic
            && log.ViolationCategory == KarmaViolationCategory.NoShow
        )), Times.Once);
    }

    [Fact]
    public async Task Aggregate_NoShowNotConfirmed_AbsentVotesAtOrBelowHalf_NoPenalty()
    {
        var bookingId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var v3 = Guid.NewGuid();
        var v4 = Guid.NewGuid();
        var suspect = Guid.NewGuid();

        var booking = BuildBooking(bookingId, BookingStatus.CheckedIn, lobbyId);
        _mockBookingRepo.Setup(r => r.GetByIdAsync(bookingId, true)).ReturnsAsync(booking);
        _mockLobbyRepo.Setup(r => r.GetByIdWithMembersAsync(lobbyId))
            .ReturnsAsync(BuildLobby(lobbyId, v1, v2, v3, v4, suspect));

        _mockRatingRepo.Setup(r => r.GetUnaggregatedByBookingAsync(bookingId))
            .ReturnsAsync(new List<BookingRating>());

        // 2/5 voters vote suspect absent → threshold = 5/2 = 2 → 2 > 2 is false → not confirmed.
        var votes = new List<BookingNoShowVote>
        {
            new() { BookingId = bookingId, VoterUserId = v1, AbsentMemberIdsJson = JsonSerializer.Serialize(new List<Guid> { suspect }) },
            new() { BookingId = bookingId, VoterUserId = v2, AbsentMemberIdsJson = JsonSerializer.Serialize(new List<Guid> { suspect }) }
        };
        _mockNoShowRepo.Setup(r => r.GetByBookingAsync(bookingId)).ReturnsAsync(votes);

        var result = await _service.AggregateBookingOutcomesAsync(bookingId);

        Assert.Empty(result.NoShowConfirmedMembers);
        Assert.False(result.KarmaDeltaByUser.ContainsKey(suspect));
        _mockKarmaRepo.Verify(k => k.AddKarmaLogAsync(It.IsAny<KarmaLog>()), Times.Never);
    }

    [Fact]
    public async Task Aggregate_NoShowConfirmed_WithRefundPolicyNone_ForfeitsDeposit()
    {
        var bookingId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var noShowMember = Guid.NewGuid();

        var booking = BuildBooking(bookingId, BookingStatus.CheckedIn, lobbyId);
        _mockBookingRepo.Setup(r => r.GetByIdAsync(bookingId, true)).ReturnsAsync(booking);
        _mockLobbyRepo.Setup(r => r.GetByIdWithMembersAsync(lobbyId))
            .ReturnsAsync(BuildLobby(lobbyId, v1, v2, noShowMember));

        _mockRatingRepo.Setup(r => r.GetUnaggregatedByBookingAsync(bookingId))
            .ReturnsAsync(new List<BookingRating>());

        // 2/3 voters vote noShowMember absent → threshold = 3/2 = 1 → 2 > 1 → confirmed.
        var votes = new List<BookingNoShowVote>
        {
            new() { BookingId = bookingId, VoterUserId = v1, AbsentMemberIdsJson = JsonSerializer.Serialize(new List<Guid> { noShowMember }) },
            new() { BookingId = bookingId, VoterUserId = v2, AbsentMemberIdsJson = JsonSerializer.Serialize(new List<Guid> { noShowMember }) }
        };
        _mockNoShowRepo.Setup(r => r.GetByBookingAsync(bookingId)).ReturnsAsync(votes);

        var deposit = new BookingDeposit
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId,
            UserId = noShowMember,
            Status = BookingDepositStatus.Paid,
            RefundPolicy = DepositRefundPolicy.None,
            Amount = 50_000m
        };
        _mockDepositRepo.Setup(r => r.GetByBookingIdAsync(bookingId)).ReturnsAsync(deposit);

        var profile = new UserProfile { UserId = noShowMember, KarmaPoints = 80 };
        _mockKarmaRepo.Setup(k => k.GetProfileForUpdateAsync(noShowMember)).ReturnsAsync(profile);

        var result = await _service.AggregateBookingOutcomesAsync(bookingId);

        Assert.Equal(BookingDepositStatus.Forfeited, deposit.Status);
        Assert.Contains(deposit.Id, result.ForfeitedDepositIds);
        _mockDepositRepo.Verify(r => r.UpdateAsync(deposit), Times.Once);

        // 2 KarmaLog rows: no-show penalty + deposit forfeit audit.
        _mockKarmaRepo.Verify(k => k.AddKarmaLogAsync(It.IsAny<KarmaLog>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Aggregate_NoShowConfirmed_WithRefundPolicyFull_DoesNotForfeitDeposit()
    {
        var bookingId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var noShowMember = Guid.NewGuid();

        var booking = BuildBooking(bookingId, BookingStatus.CheckedIn, lobbyId);
        _mockBookingRepo.Setup(r => r.GetByIdAsync(bookingId, true)).ReturnsAsync(booking);
        _mockLobbyRepo.Setup(r => r.GetByIdWithMembersAsync(lobbyId))
            .ReturnsAsync(BuildLobby(lobbyId, v1, v2, noShowMember));

        _mockRatingRepo.Setup(r => r.GetUnaggregatedByBookingAsync(bookingId))
            .ReturnsAsync(new List<BookingRating>());

        var votes = new List<BookingNoShowVote>
        {
            new() { BookingId = bookingId, VoterUserId = v1, AbsentMemberIdsJson = JsonSerializer.Serialize(new List<Guid> { noShowMember }) },
            new() { BookingId = bookingId, VoterUserId = v2, AbsentMemberIdsJson = JsonSerializer.Serialize(new List<Guid> { noShowMember }) }
        };
        _mockNoShowRepo.Setup(r => r.GetByBookingAsync(bookingId)).ReturnsAsync(votes);

        var deposit = new BookingDeposit
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId,
            UserId = noShowMember,
            Status = BookingDepositStatus.Paid,
            RefundPolicy = DepositRefundPolicy.Full,
            Amount = 50_000m
        };
        _mockDepositRepo.Setup(r => r.GetByBookingIdAsync(bookingId)).ReturnsAsync(deposit);

        var profile = new UserProfile { UserId = noShowMember, KarmaPoints = 80 };
        _mockKarmaRepo.Setup(k => k.GetProfileForUpdateAsync(noShowMember)).ReturnsAsync(profile);

        var result = await _service.AggregateBookingOutcomesAsync(bookingId);

        Assert.Equal(BookingDepositStatus.Paid, deposit.Status);
        Assert.Empty(result.ForfeitedDepositIds);
        _mockDepositRepo.Verify(r => r.UpdateAsync(It.IsAny<BookingDeposit>()), Times.Never);
        // Chỉ 1 KarmaLog row (no-show penalty), không có audit log cho forfeit.
        _mockKarmaRepo.Verify(k => k.AddKarmaLogAsync(It.IsAny<KarmaLog>()), Times.Once);
    }

    // ===========================================================================
    // 4. Idempotency
    // ===========================================================================

    [Fact]
    public async Task Aggregate_NoUnaggregatedRatings_NoVotes_ReturnsEmptyResult()
    {
        var bookingId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var booking = BuildBooking(bookingId, BookingStatus.CheckedIn, lobbyId);
        _mockBookingRepo.Setup(r => r.GetByIdAsync(bookingId, true)).ReturnsAsync(booking);
        _mockLobbyRepo.Setup(r => r.GetByIdWithMembersAsync(lobbyId))
            .ReturnsAsync(BuildLobby(lobbyId, Guid.NewGuid()));

        _mockRatingRepo.Setup(r => r.GetUnaggregatedByBookingAsync(bookingId))
            .ReturnsAsync(new List<BookingRating>());
        _mockNoShowRepo.Setup(r => r.GetByBookingAsync(bookingId))
            .ReturnsAsync(new List<BookingNoShowVote>());

        var result = await _service.AggregateBookingOutcomesAsync(bookingId);

        Assert.Equal(0, result.RatingsProcessed);
        Assert.Empty(result.KarmaDeltaByUser);
        Assert.Empty(result.NoShowConfirmedMembers);
        Assert.Equal(0m, result.TotalKarmaDelta);
        _mockKarmaRepo.Verify(k => k.AddKarmaLogAsync(It.IsAny<KarmaLog>()), Times.Never);
    }

    // ===========================================================================
    // 5. Save changes
    // ===========================================================================

    [Fact]
    public async Task Aggregate_WithChanges_CallsAllSaveChangesAsync()
    {
        var bookingId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var v1 = Guid.NewGuid();
        var target = Guid.NewGuid();

        var booking = BuildBooking(bookingId, BookingStatus.CheckedIn, lobbyId);
        _mockBookingRepo.Setup(r => r.GetByIdAsync(bookingId, true)).ReturnsAsync(booking);
        _mockLobbyRepo.Setup(r => r.GetByIdWithMembersAsync(lobbyId))
            .ReturnsAsync(BuildLobby(lobbyId, v1, target));

        var ratingRow = new BookingRating
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId,
            VoterUserId = v1,
            RatingsJson = JsonSerializer.Serialize(new List<BookingRatingItemDto>
            {
                new() { RatedUserId = target, Attitude = 5, Sportsmanship = 5, Punctuality = 5 }
            }),
            IsAggregated = false
        };
        _mockRatingRepo.Setup(r => r.GetUnaggregatedByBookingAsync(bookingId))
            .ReturnsAsync(new List<BookingRating> { ratingRow });
        _mockNoShowRepo.Setup(r => r.GetByBookingAsync(bookingId))
            .ReturnsAsync(new List<BookingNoShowVote>());

        var profile = new UserProfile { UserId = target, KarmaPoints = 100 };
        _mockKarmaRepo.Setup(k => k.GetProfileForUpdateAsync(target)).ReturnsAsync(profile);

        await _service.AggregateBookingOutcomesAsync(bookingId);

        _mockKarmaRepo.Verify(k => k.SaveChangesAsync(), Times.Once);
        _mockRatingRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        _mockDepositRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    // ===========================================================================
    // Helpers
    // ===========================================================================

    private static Booking BuildBooking(Guid id, BookingStatus status, Guid? lobbyId = null)
    {
        return new Booking
        {
            Id = id,
            Status = status,
            LobbyId = lobbyId,
            ScheduledStartTime = DateTime.UtcNow.AddHours(-2),
            ScheduleEndTime = DateTime.UtcNow.AddHours(2),
            Lobby = lobbyId.HasValue
                ? new Lobby { Id = lobbyId.Value, Status = LobbyStatus.InProgress }
                : null
        };
    }

    private static Lobby BuildLobby(Guid lobbyId, params Guid[] userIds)
    {
        var lobby = new Lobby
        {
            Id = lobbyId,
            Status = LobbyStatus.InProgress
        };
        foreach (var uid in userIds)
        {
            lobby.Members.Add(new LobbyMember
            {
                Id = Guid.NewGuid(),
                LobbyId = lobbyId,
                UserId = uid,
                IsActive = true,
                IsHost = false
            });
        }
        return lobby;
    }
}
