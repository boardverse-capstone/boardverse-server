using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

public class TournamentWaitlistService : ITournamentWaitlistService
{
    private readonly ITournamentWaitlistRepository _waitlistRepository;
    private readonly ITournamentRepository _tournamentRepository;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly ILogger<TournamentWaitlistService> _logger;

    public TournamentWaitlistService(
        ITournamentWaitlistRepository waitlistRepository,
        ITournamentRepository tournamentRepository,
        IUserProfileRepository userProfileRepository,
        ILogger<TournamentWaitlistService> logger)
    {
        _waitlistRepository = waitlistRepository;
        _tournamentRepository = tournamentRepository;
        _userProfileRepository = userProfileRepository;
        _logger = logger;
    }

    public async Task<TournamentWaitlistEntryDto> JoinWaitlistAsync(Guid userId, Guid tournamentId)
    {
        var tournament = await _tournamentRepository.GetByIdAsync(tournamentId)
            ?? throw new NotFoundException(ApiErrorMessages.Tournament.NotFound(tournamentId));

        if (tournament.Status != TournamentStatus.RegistrationOpen
            && tournament.Status != TournamentStatus.RegistrationClosed)
        {
            throw new ConflictException("Chỉ có thể tham gia waitlist khi tournament đang mở đăng ký.");
        }

        if (tournament.RegistrationDeadline < DateTime.UtcNow)
        {
            throw new ConflictException("Đã hết hạn đăng ký tournament.");
        }

        // Check if user is already a participant
        var existingParticipant = await _tournamentRepository.GetParticipantAsync(tournamentId, userId);
        if (existingParticipant != null)
        {
            throw new ConflictException("Bạn đã đăng ký tham gia tournament này.");
        }

        // Check if already in waitlist
        var existingEntry = await _waitlistRepository.GetPendingByUserAsync(tournamentId, userId);
        if (existingEntry != null)
        {
            return MapToDto(existingEntry);
        }

        var position = await _waitlistRepository.GetNextPositionAsync(tournamentId);

        var entry = new TournamentWaitlist
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            UserId = userId,
            Position = position,
            Status = TournamentWaitlistStatus.Pending,
            JoinedAt = DateTime.UtcNow
        };

        await _waitlistRepository.AddAsync(entry);
        await _waitlistRepository.SaveChangesAsync();

        _logger.LogInformation(
            "User {UserId} joined waitlist for tournament {TournamentId} at position {Position}",
            userId, tournamentId, position);

        return MapToDto(entry);
    }

    public async Task<IReadOnlyList<TournamentWaitlistEntryDto>> GetWaitlistAsync(Guid tournamentId)
    {
        var entries = await _waitlistRepository.GetByTournamentAsync(tournamentId);
        return entries.Select(MapToDto).ToList();
    }

    public async Task<TournamentWaitlistEntryDto?> GetMyWaitlistEntryAsync(Guid userId, Guid tournamentId)
    {
        var entry = await _waitlistRepository.GetPendingByUserAsync(tournamentId, userId);
        return entry != null ? MapToDto(entry) : null;
    }

    public async Task CancelWaitlistAsync(Guid userId, Guid tournamentId)
    {
        var entry = await _waitlistRepository.GetPendingByUserAsync(tournamentId, userId)
            ?? throw new NotFoundException("Bạn không có trong waitlist của tournament này.");

        entry.Status = TournamentWaitlistStatus.Cancelled;
        await _waitlistRepository.UpdateAsync(entry);
        await _waitlistRepository.SaveChangesAsync();

        _logger.LogInformation(
            "User {UserId} cancelled waitlist for tournament {TournamentId}",
            userId, tournamentId);
    }

    public async Task<TournamentWaitlistEntryDto> ConfirmFromWaitlistAsync(Guid userId, Guid tournamentId)
    {
        var entry = await _waitlistRepository.GetPendingByUserAsync(tournamentId, userId)
            ?? throw new NotFoundException("Bạn không có trong waitlist của tournament này.");

        if (entry.Status != TournamentWaitlistStatus.Offered)
        {
            throw new ConflictException("Bạn chưa nhận được offer từ waitlist.");
        }

        if (entry.OfferExpiresAt.HasValue && entry.OfferExpiresAt.Value < DateTime.UtcNow)
        {
            entry.Status = TournamentWaitlistStatus.Expired;
            await _waitlistRepository.UpdateAsync(entry);
            await _waitlistRepository.SaveChangesAsync();
            throw new ConflictException("Offer đã hết hạn.");
        }

        // Register user as participant (simplified - in real impl would call TournamentService)
        entry.Status = TournamentWaitlistStatus.Joined;
        entry.ConfirmedAt = DateTime.UtcNow;
        await _waitlistRepository.UpdateAsync(entry);
        await _waitlistRepository.SaveChangesAsync();

        _logger.LogInformation(
            "User {UserId} confirmed waitlist and joined tournament {TournamentId}",
            userId, tournamentId);

        return MapToDto(entry);
    }

    public async Task<TournamentWaitlistEntryDto> DeclineOfferAsync(Guid userId, Guid tournamentId)
    {
        var entry = await _waitlistRepository.GetPendingByUserAsync(tournamentId, userId)
            ?? throw new NotFoundException("Bạn không có trong waitlist của tournament này.");

        if (entry.Status != TournamentWaitlistStatus.Offered)
        {
            throw new ConflictException("Bạn không có offer nào để từ chối.");
        }

        entry.Status = TournamentWaitlistStatus.Cancelled;
        await _waitlistRepository.UpdateAsync(entry);
        await _waitlistRepository.SaveChangesAsync();

        _logger.LogInformation(
            "User {UserId} declined waitlist offer for tournament {TournamentId}",
            userId, tournamentId);

        return MapToDto(entry);
    }

    private static TournamentWaitlistEntryDto MapToDto(TournamentWaitlist entry)
    {
        return new TournamentWaitlistEntryDto
        {
            Id = entry.Id,
            TournamentId = entry.TournamentId,
            TournamentTitle = entry.Tournament?.Title ?? string.Empty,
            UserId = entry.UserId,
            UserName = entry.User?.Username ?? string.Empty,
            Position = entry.Position,
            Status = entry.Status,
            JoinedAt = entry.JoinedAt,
            OfferedAt = entry.OfferedAt,
            OfferExpiresAt = entry.OfferExpiresAt,
            ConfirmedAt = entry.ConfirmedAt
        };
    }
}
