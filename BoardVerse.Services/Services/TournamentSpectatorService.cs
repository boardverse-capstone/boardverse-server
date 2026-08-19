using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

public class TournamentSpectatorService : ITournamentSpectatorService
{
    private readonly ITournamentSpectatorRepository _spectatorRepository;
    private readonly ITournamentRepository _tournamentRepository;
    private readonly ILogger<TournamentSpectatorService> _logger;

    public TournamentSpectatorService(
        ITournamentSpectatorRepository spectatorRepository,
        ITournamentRepository tournamentRepository,
        ILogger<TournamentSpectatorService> logger)
    {
        _spectatorRepository = spectatorRepository;
        _tournamentRepository = tournamentRepository;
        _logger = logger;
    }

    public async Task<TournamentSpectatorDto> SpectateAsync(Guid userId, Guid tournamentId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        if (tournament.Status == TournamentStatus.Draft)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.Spectator.CannotSpectateUnpublished);
        }

        // Check if user is already a participant
        var existingParticipant = await _tournamentRepository.GetParticipantAsync(tournamentId, userId);
        if (existingParticipant != null)
        {
            throw new ConflictException(ApiErrorMessages.Tournament.Spectator.CannotSpectateAsParticipant);
        }

        // Check if already spectating
        var existing = await _spectatorRepository.GetByUserAsync(tournamentId, userId);
        if (existing != null)
        {
            return MapToDto(existing);
        }

        var spectator = new TournamentSpectator
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            UserId = userId,
            JoinedAt = DateTime.UtcNow
        };

        await _spectatorRepository.AddAsync(spectator);
        await _spectatorRepository.SaveChangesAsync();

        _logger.LogInformation(
            "User {UserId} started spectating tournament {TournamentId}",
            userId, tournamentId);

        return MapToDto(spectator);
    }

    public async Task LeaveSpectateAsync(Guid userId, Guid tournamentId)
    {
        var spectator = await _spectatorRepository.GetByUserAsync(tournamentId, userId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.Spectator.NotSpectating);

        spectator.LeftAt = DateTime.UtcNow;
        await _spectatorRepository.UpdateAsync(spectator);
        await _spectatorRepository.SaveChangesAsync();

        _logger.LogInformation(
            "User {UserId} stopped spectating tournament {TournamentId}",
            userId, tournamentId);
    }

    public async Task<TournamentSpectatorDto?> GetMySpectatorEntryAsync(Guid userId, Guid tournamentId)
    {
        var spectator = await _spectatorRepository.GetByUserAsync(tournamentId, userId);
        return spectator != null ? MapToDto(spectator) : null;
    }

    public async Task<IReadOnlyList<TournamentSpectatorDto>> GetSpectatorsAsync(Guid tournamentId)
    {
        var spectators = await _spectatorRepository.GetByTournamentAsync(tournamentId);
        return spectators.Select(MapToDto).ToList();
    }

    private static TournamentSpectatorDto MapToDto(TournamentSpectator spectator)
    {
        return new TournamentSpectatorDto
        {
            Id = spectator.Id,
            TournamentId = spectator.TournamentId,
            TournamentTitle = spectator.Tournament?.Title ?? string.Empty,
            UserId = spectator.UserId,
            UserName = spectator.User?.Username ?? string.Empty,
            JoinedAt = spectator.JoinedAt,
            LeftAt = spectator.LeftAt
        };
    }
}
