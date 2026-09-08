using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho TournamentWaitlistService — waitlist cho tournament khi full participant.
/// BR-TOURNAMENT-WAITLIST: chỉ join khi registration open, không trùng participant, idempotent re-join.
/// </summary>
public class TournamentWaitlistServiceTests
{
    private readonly Mock<ITournamentWaitlistRepository> _waitlistRepo = new();
    private readonly Mock<ITournamentRepository> _tournamentRepo = new();
    private readonly Mock<IUserProfileRepository> _userProfileRepo = new();

    private TournamentWaitlistService CreateService() => new(
        _waitlistRepo.Object,
        _tournamentRepo.Object,
        _userProfileRepo.Object,
        NullLogger<TournamentWaitlistService>.Instance);

    private static Tournament BuildTournament(
        Guid id,
        TournamentStatus status = TournamentStatus.RegistrationOpen,
        DateTime? registrationDeadline = null) => new()
    {
        Id = id,
        Title = "Test Tournament",
        Status = status,
        RegistrationDeadline = registrationDeadline ?? DateTime.UtcNow.AddDays(1)
    };

    #region JoinWaitlistAsync

    [Fact]
    public async Task JoinWaitlistAsync_TournamentNotFound_ThrowsNotFound()
    {
        var tournamentId = Guid.NewGuid();
        _tournamentRepo.Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tournament?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.JoinWaitlistAsync(Guid.NewGuid(), tournamentId));
    }

    [Theory]
    [InlineData(TournamentStatus.Draft)]
    [InlineData(TournamentStatus.OnGoing)]
    public async Task JoinWaitlistAsync_RegistrationNotOpen_ThrowsConflict(TournamentStatus status)
    {
        var tournamentId = Guid.NewGuid();
        _tournamentRepo.Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTournament(tournamentId, status));

        var sut = CreateService();
        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.JoinWaitlistAsync(Guid.NewGuid(), tournamentId));
    }

    [Fact]
    public async Task JoinWaitlistAsync_DeadlinePassed_ThrowsConflict()
    {
        var tournamentId = Guid.NewGuid();
        _tournamentRepo.Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTournament(tournamentId, TournamentStatus.RegistrationOpen, DateTime.UtcNow.AddDays(-1)));

        var sut = CreateService();
        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.JoinWaitlistAsync(Guid.NewGuid(), tournamentId));
    }

    [Fact]
    public async Task JoinWaitlistAsync_AlreadyParticipant_ThrowsConflict()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _tournamentRepo.Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTournament(tournamentId));
        _tournamentRepo.Setup(r => r.GetParticipantAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TournamentParticipant { Id = Guid.NewGuid(), TournamentId = tournamentId, UserId = userId });

        var sut = CreateService();
        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.JoinWaitlistAsync(userId, tournamentId));
    }

    [Fact]
    public async Task JoinWaitlistAsync_AlreadyInWaitlist_ReturnsExistingEntry()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var existing = new TournamentWaitlist
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            UserId = userId,
            Position = 3,
            Status = TournamentWaitlistStatus.Pending,
            JoinedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        _tournamentRepo.Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTournament(tournamentId));
        _tournamentRepo.Setup(r => r.GetParticipantAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TournamentParticipant?)null);
        _waitlistRepo.Setup(r => r.GetPendingByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = CreateService();
        var dto = await sut.JoinWaitlistAsync(userId, tournamentId);

        Assert.Equal(existing.Id, dto.Id);
        Assert.Equal(3, dto.Position);
        _waitlistRepo.Verify(r => r.AddAsync(It.IsAny<TournamentWaitlist>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task JoinWaitlistAsync_NewEntry_AssignsNextPosition()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _tournamentRepo.Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTournament(tournamentId));
        _tournamentRepo.Setup(r => r.GetParticipantAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TournamentParticipant?)null);
        _waitlistRepo.Setup(r => r.GetPendingByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TournamentWaitlist?)null);
        _waitlistRepo.Setup(r => r.GetNextPositionAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        var sut = CreateService();
        var dto = await sut.JoinWaitlistAsync(userId, tournamentId);

        Assert.Equal(userId, dto.UserId);
        Assert.Equal(7, dto.Position);
        Assert.Equal(TournamentWaitlistStatus.Pending, dto.Status);

        _waitlistRepo.Verify(r => r.AddAsync(It.Is<TournamentWaitlist>(e =>
            e.TournamentId == tournamentId &&
            e.UserId == userId &&
            e.Position == 7 &&
            e.Status == TournamentWaitlistStatus.Pending), It.IsAny<CancellationToken>()), Times.Once);
        _waitlistRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JoinWaitlistAsync_RegistrationClosed_AllowsJoin()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _tournamentRepo.Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTournament(tournamentId, TournamentStatus.RegistrationClosed));
        _tournamentRepo.Setup(r => r.GetParticipantAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TournamentParticipant?)null);
        _waitlistRepo.Setup(r => r.GetPendingByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TournamentWaitlist?)null);
        _waitlistRepo.Setup(r => r.GetNextPositionAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = CreateService();
        var dto = await sut.JoinWaitlistAsync(userId, tournamentId);

        Assert.Equal(TournamentWaitlistStatus.Pending, dto.Status);
    }

    #endregion

    #region CancelWaitlistAsync

    [Fact]
    public async Task CancelWaitlistAsync_NotInWaitlist_ThrowsNotFound()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _waitlistRepo.Setup(r => r.GetPendingByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TournamentWaitlist?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.CancelWaitlistAsync(userId, tournamentId));
    }

    [Fact]
    public async Task CancelWaitlistAsync_Existing_MarksAsCancelled()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var entry = new TournamentWaitlist
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            UserId = userId,
            Position = 2,
            Status = TournamentWaitlistStatus.Pending,
            JoinedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        _waitlistRepo.Setup(r => r.GetPendingByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        var sut = CreateService();
        await sut.CancelWaitlistAsync(userId, tournamentId);

        Assert.Equal(TournamentWaitlistStatus.Cancelled, entry.Status);
        _waitlistRepo.Verify(r => r.UpdateAsync(entry, It.IsAny<CancellationToken>()), Times.Once);
        _waitlistRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ConfirmFromWaitlistAsync

    [Fact]
    public async Task ConfirmFromWaitlistAsync_NoEntry_ThrowsNotFound()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _waitlistRepo.Setup(r => r.GetPendingByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TournamentWaitlist?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.ConfirmFromWaitlistAsync(userId, tournamentId));
    }

    [Fact]
    public async Task ConfirmFromWaitlistAsync_NotOffered_ThrowsConflict()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var entry = new TournamentWaitlist
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            UserId = userId,
            Status = TournamentWaitlistStatus.Pending
        };
        _waitlistRepo.Setup(r => r.GetPendingByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        var sut = CreateService();
        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.ConfirmFromWaitlistAsync(userId, tournamentId));
    }

    [Fact]
    public async Task ConfirmFromWaitlistAsync_OfferExpired_MarksExpiredAndThrows()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var entry = new TournamentWaitlist
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            UserId = userId,
            Status = TournamentWaitlistStatus.Offered,
            OfferExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        };
        _waitlistRepo.Setup(r => r.GetPendingByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        var sut = CreateService();
        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.ConfirmFromWaitlistAsync(userId, tournamentId));

        Assert.Equal(TournamentWaitlistStatus.Expired, entry.Status);
    }

    [Fact]
    public async Task ConfirmFromWaitlistAsync_ValidOffer_MarksJoined()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var entry = new TournamentWaitlist
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            UserId = userId,
            Status = TournamentWaitlistStatus.Offered,
            OfferExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        _waitlistRepo.Setup(r => r.GetPendingByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        var sut = CreateService();
        var dto = await sut.ConfirmFromWaitlistAsync(userId, tournamentId);

        Assert.Equal(TournamentWaitlistStatus.Joined, dto.Status);
        Assert.NotNull(dto.ConfirmedAt);
        Assert.Equal(TournamentWaitlistStatus.Joined, entry.Status);
    }

    #endregion

    #region DeclineOfferAsync

    [Fact]
    public async Task DeclineOfferAsync_NoEntry_ThrowsNotFound()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _waitlistRepo.Setup(r => r.GetPendingByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TournamentWaitlist?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.DeclineOfferAsync(userId, tournamentId));
    }

    [Fact]
    public async Task DeclineOfferAsync_NotOffered_ThrowsConflict()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var entry = new TournamentWaitlist
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            UserId = userId,
            Status = TournamentWaitlistStatus.Pending
        };
        _waitlistRepo.Setup(r => r.GetPendingByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        var sut = CreateService();
        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.DeclineOfferAsync(userId, tournamentId));
    }

    [Fact]
    public async Task DeclineOfferAsync_ValidOffer_MarksCancelled()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var entry = new TournamentWaitlist
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            UserId = userId,
            Status = TournamentWaitlistStatus.Offered,
            OfferExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        _waitlistRepo.Setup(r => r.GetPendingByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        var sut = CreateService();
        await sut.DeclineOfferAsync(userId, tournamentId);

        Assert.Equal(TournamentWaitlistStatus.Cancelled, entry.Status);
    }

    #endregion

    #region GetWaitlistAsync

    [Fact]
    public async Task GetWaitlistAsync_NoEntries_ReturnsEmptyList()
    {
        var tournamentId = Guid.NewGuid();
        _waitlistRepo.Setup(r => r.GetByTournamentAsync(tournamentId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TournamentWaitlist>());

        var sut = CreateService();
        var result = await sut.GetWaitlistAsync(tournamentId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetWaitlistAsync_WithEntries_ReturnsMapped()
    {
        var tournamentId = Guid.NewGuid();
        var entries = new List<TournamentWaitlist>
        {
            new() { Id = Guid.NewGuid(), TournamentId = tournamentId, UserId = Guid.NewGuid(), Position = 1, Status = TournamentWaitlistStatus.Pending },
            new() { Id = Guid.NewGuid(), TournamentId = tournamentId, UserId = Guid.NewGuid(), Position = 2, Status = TournamentWaitlistStatus.Pending }
        };
        _waitlistRepo.Setup(r => r.GetByTournamentAsync(tournamentId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        var sut = CreateService();
        var result = await sut.GetWaitlistAsync(tournamentId);

        Assert.Equal(2, result.Count);
    }

    #endregion

    #region GetMyWaitlistEntryAsync

    [Fact]
    public async Task GetMyWaitlistEntryAsync_NoEntry_ReturnsNull()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _waitlistRepo.Setup(r => r.GetPendingByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TournamentWaitlist?)null);

        var sut = CreateService();
        var result = await sut.GetMyWaitlistEntryAsync(userId, tournamentId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetMyWaitlistEntryAsync_HasEntry_ReturnsDto()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var entry = new TournamentWaitlist
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            UserId = userId,
            Position = 4,
            Status = TournamentWaitlistStatus.Pending
        };
        _waitlistRepo.Setup(r => r.GetPendingByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        var sut = CreateService();
        var result = await sut.GetMyWaitlistEntryAsync(userId, tournamentId);

        Assert.NotNull(result);
        Assert.Equal(4, result!.Position);
    }

    #endregion
}
