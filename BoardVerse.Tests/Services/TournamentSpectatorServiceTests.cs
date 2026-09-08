using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit tests cho TournamentSpectatorService — quản lý spectator của tournament.
/// </summary>
public class TournamentSpectatorServiceTests
{
    private readonly Mock<ITournamentSpectatorRepository> _spectatorRepo = new();
    private readonly Mock<ITournamentRepository> _tournamentRepo = new();

    private TournamentSpectatorService CreateService() => new(
        _spectatorRepo.Object,
        _tournamentRepo.Object,
        NullLogger<TournamentSpectatorService>.Instance);

    private static Tournament BuildTournament(Guid id, TournamentStatus status = TournamentStatus.RegistrationOpen) => new()
    {
        Id = id,
        Title = "Test Tournament",
        Status = status
    };

    private static TournamentParticipant BuildParticipant(Guid tournamentId, Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        TournamentId = tournamentId,
        UserId = userId
    };

    #region SpectateAsync

    [Fact]
    public async Task SpectateAsync_TournamentNotFound_ThrowsNotFound()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _tournamentRepo.Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tournament?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.SpectateAsync(userId, tournamentId));
    }

    [Theory]
    [InlineData(TournamentStatus.Draft)]
    public async Task SpectateAsync_DraftStatus_ThrowsConflict(TournamentStatus status)
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _tournamentRepo.Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTournament(tournamentId, status));

        var sut = CreateService();
        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.SpectateAsync(userId, tournamentId));
    }

    [Fact]
    public async Task SpectateAsync_AlreadyParticipant_ThrowsConflict()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _tournamentRepo.Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTournament(tournamentId, TournamentStatus.RegistrationOpen));
        _tournamentRepo.Setup(r => r.GetParticipantAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildParticipant(tournamentId, userId));

        var sut = CreateService();
        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.SpectateAsync(userId, tournamentId));
    }

    [Fact]
    public async Task SpectateAsync_AlreadySpectating_ReturnsExistingDto()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var existing = new TournamentSpectator
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            UserId = userId,
            JoinedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        _tournamentRepo.Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTournament(tournamentId, TournamentStatus.OnGoing));
        _tournamentRepo.Setup(r => r.GetParticipantAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TournamentParticipant?)null);
        _spectatorRepo.Setup(r => r.GetByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = CreateService();
        var dto = await sut.SpectateAsync(userId, tournamentId);

        Assert.Equal(existing.Id, dto.Id);
        _spectatorRepo.Verify(r => r.AddAsync(It.IsAny<TournamentSpectator>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SpectateAsync_NewSpectator_PersistsAndReturns()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _tournamentRepo.Setup(r => r.GetByIdAsync(tournamentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTournament(tournamentId, TournamentStatus.RegistrationOpen));
        _tournamentRepo.Setup(r => r.GetParticipantAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TournamentParticipant?)null);
        _spectatorRepo.Setup(r => r.GetByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TournamentSpectator?)null);

        var sut = CreateService();
        var dto = await sut.SpectateAsync(userId, tournamentId);

        Assert.Equal(tournamentId, dto.TournamentId);
        Assert.Equal(userId, dto.UserId);
        Assert.Null(dto.LeftAt);

        _spectatorRepo.Verify(r => r.AddAsync(It.Is<TournamentSpectator>(s =>
            s.TournamentId == tournamentId && s.UserId == userId), It.IsAny<CancellationToken>()), Times.Once);
        _spectatorRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region LeaveSpectateAsync

    [Fact]
    public async Task LeaveSpectateAsync_NotSpectating_ThrowsNotFound()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _spectatorRepo.Setup(r => r.GetByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TournamentSpectator?)null);

        var sut = CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.LeaveSpectateAsync(userId, tournamentId));
    }

    [Fact]
    public async Task LeaveSpectateAsync_Existing_SetsLeftAtAndPersists()
    {
        var tournamentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var existing = new TournamentSpectator
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            UserId = userId,
            JoinedAt = DateTime.UtcNow.AddMinutes(-10),
            LeftAt = null
        };
        _spectatorRepo.Setup(r => r.GetByUserAsync(tournamentId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = CreateService();
        await sut.LeaveSpectateAsync(userId, tournamentId);

        Assert.NotNull(existing.LeftAt);
        _spectatorRepo.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        _spectatorRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}
