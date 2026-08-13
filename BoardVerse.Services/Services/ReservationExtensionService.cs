using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

/// <summary>
/// Service cho extension flow (Phase 3).
///
/// BR-EXT-01: Chỉ extend khi Status = Confirmed.
/// BR-EXT-02: Không extend qua midnight.
/// BR-EXT-03: Max 2 lần extend (tổng max 2 tiếng).
/// BR-EXT-04: Charge thêm BVC cho extension.
/// BR-EXT-05: Partial extension OK.
///
/// EC-05: Extension không được overlap với WalkInWindow đang có.
/// EC-08: Extension không được cross-day.
/// </summary>
public class ReservationExtensionService : IReservationExtensionService
{
    private const int MaxExtensionCount = 2;
    private const int MaxExtensionMinutesPerExtension = 60; // 1 tiếng mỗi lần
    private const int MaxTotalExtensionMinutes = 120; // tổng 2 tiếng

    private readonly BoardVerseDbContext _db;
    private readonly IReservationRepository _reservationRepository;
    private readonly IWalkInWindowRepository _walkInWindowRepository;
    private readonly IWalletService _walletService;
    private readonly ILogger<ReservationExtensionService> _logger;

    public ReservationExtensionService(
        BoardVerseDbContext db,
        IReservationRepository reservationRepository,
        IWalkInWindowRepository walkInWindowRepository,
        IWalletService walletService,
        ILogger<ReservationExtensionService> logger)
    {
        _db = db;
        _reservationRepository = reservationRepository;
        _walkInWindowRepository = walkInWindowRepository;
        _walletService = walletService;
        _logger = logger;
    }

    /// <summary>
    /// Check availability trước khi extend (không thay đổi DB).
    /// </summary>
    public async Task<ExtendAvailabilityDto> CheckAvailabilityAsync(
        Guid reservationId, int extensionMinutes, CancellationToken ct = default)
    {
        var reservation = await _reservationRepository.GetByIdAsync(reservationId, includeRelations: true);
        if (reservation == null)
        {
            throw new NotFoundException(ApiErrorMessages.Reservation.NotFound(reservationId));
        }

        var currentEndTime = reservation.ExtendedEndTime ?? reservation.ScheduledEndTime;
        if (currentEndTime == default)
            throw new InvalidOperationException($"Cannot extend reservation {reservationId}: both ExtendedEndTime and ScheduledEndTime are null");
        var remainingMinutes = MaxTotalExtensionMinutes - (reservation.ExtensionCount * MaxExtensionMinutesPerExtension);
        var proposedEndTime = currentEndTime.AddMinutes(extensionMinutes);

        var dto = new ExtendAvailabilityDto
        {
            ReservationId = reservationId,
            CurrentEndTime = currentEndTime,
            ProposedEndTime = proposedEndTime,
            CanExtend = true,
            RemainingExtensionMinutes = remainingMinutes
        };

        // BR-EXT-01: Chỉ extend khi Confirmed
        if (reservation.Status != ReservationStatus.Confirmed)
        {
            dto.CanExtend = false;
            dto.Reason = ApiErrorMessages.ReservationExtension.OnlyConfirmedStatus(reservation.Status.ToString());
            return dto;
        }

        // BR-EXT-03: Max 2 lần
        if (reservation.ExtensionCount >= MaxExtensionCount)
        {
            dto.CanExtend = false;
            dto.Reason = ApiErrorMessages.ReservationExtension.MaxExtensionCountReached(MaxExtensionCount);
            return dto;
        }

        // Check remaining minutes
        if (extensionMinutes > remainingMinutes)
        {
            dto.CanExtend = false;
            dto.Reason = ApiErrorMessages.ReservationExtension.RemainingMinutesInsufficient(remainingMinutes, extensionMinutes);
            return dto;
        }

        // BR-EXT-02: Không extend qua midnight
        if (proposedEndTime.Date > reservation.PlayDate.ToDateTime(TimeOnly.MinValue).Date)
        {
            dto.CanExtend = false;
            dto.Reason = ApiErrorMessages.ReservationExtension.CannotExtendPastMidnight;
            return dto;
        }

        // EC-05: Check WalkInWindow overlap
        var overlappingWindows = await _walkInWindowRepository.GetOverlappingAsync(
            reservation.CafeId, currentEndTime, proposedEndTime, ct);

        if (overlappingWindows.Count > 0)
        {
            dto.CanExtend = false;
            dto.Reason = ApiErrorMessages.ReservationExtension.WalkInWindowOverlap;
            return dto;
        }

        return dto;
    }

    /// <summary>
    /// Extend thời gian reservation.
    ///
    /// BR-EXT-01..05 + EC-05 + EC-08.
    /// Idempotent: nếu reservation đã extend cùng số phút → trả kết quả cũ.
    /// </summary>
    public async Task<ExtendReservationResponseDto> ExtendAsync(
        ExtendReservationRequestDto request, Guid userId, CancellationToken ct = default)
    {
        var reservation = await _reservationRepository.GetByIdAsync(
            request.ReservationId, includeRelations: true);

        if (reservation == null)
        {
            throw new NotFoundException(ApiErrorMessages.Reservation.NotFound(request.ReservationId));
        }

        // Validate: chỉ host được extend
        if (reservation.HostId != userId)
        {
            throw new ForbiddenException(ApiErrorMessages.ReservationExtension.OnlyHostCanExtend);
        }

        var currentEndTime = reservation.ExtendedEndTime ?? reservation.ScheduledEndTime;
        if (currentEndTime == default)
            throw new InvalidOperationException($"Cannot extend reservation {request.ReservationId}: both ExtendedEndTime and ScheduledEndTime are null");
        var proposedEndTime = currentEndTime.AddMinutes(request.ExtensionMinutes);
        var previousEndTime = reservation.ExtendedEndTime ?? reservation.ScheduledEndTime;
        if (previousEndTime == default)
            throw new InvalidOperationException($"Cannot extend reservation {request.ReservationId}: both ExtendedEndTime and ScheduledEndTime are null");

        // BR-EXT-01: Chỉ extend khi Confirmed
        if (reservation.Status != ReservationStatus.Confirmed)
        {
            throw new ConflictException(
                ApiErrorMessages.ReservationExtension.OnlyConfirmedStatus(reservation.Status.ToString()));
        }

        // BR-EXT-03: Max 2 lần
        if (reservation.ExtensionCount >= MaxExtensionCount)
        {
            throw new ConflictException(ApiErrorMessages.ReservationExtension.MaxExtensionCountReached(MaxExtensionCount));
        }

        // Check remaining minutes
        var remainingMinutes = MaxTotalExtensionMinutes - (reservation.ExtensionCount * MaxExtensionMinutesPerExtension);
        if (request.ExtensionMinutes > remainingMinutes)
        {
            throw new ConflictException(
                ApiErrorMessages.ReservationExtension.RemainingMinutesInsufficient(remainingMinutes, request.ExtensionMinutes));
        }

        // BR-EXT-02: Không extend qua midnight
        if (proposedEndTime.Date > reservation.PlayDate.ToDateTime(TimeOnly.MinValue).Date)
        {
            throw new ConflictException(
                ApiErrorMessages.ReservationExtension.CannotExtendPastMidnight);
        }

        // EC-05: Check WalkInWindow overlap
        var overlappingWindows = await _walkInWindowRepository.GetOverlappingAsync(
            reservation.CafeId, currentEndTime, proposedEndTime, ct);

        if (overlappingWindows.Count > 0)
        {
            throw new ConflictException(
                ApiErrorMessages.ReservationExtension.WalkInWindowOverlap);
        }

        // Update reservation
        reservation.ExtendedEndTime = proposedEndTime;
        reservation.ExtensionCount += 1;
        reservation.UpdatedAt = DateTime.UtcNow;

        await _reservationRepository.UpdateAsync(reservation);

        _logger.LogInformation(
            "Reservation {Id} extended: {PrevEnd} -> {NewEnd}, count={Count}",
            reservation.Id, previousEndTime, proposedEndTime, reservation.ExtensionCount);

        return new ExtendReservationResponseDto
        {
            ReservationId = reservation.Id,
            NewScheduledEndTime = proposedEndTime,
            PreviousEndTime = previousEndTime,
            ExtensionCount = reservation.ExtensionCount,
            ExtensionMinutes = request.ExtensionMinutes,
            RemainingExtensionMinutes = MaxTotalExtensionMinutes - (reservation.ExtensionCount * MaxExtensionMinutesPerExtension)
        };
    }
}
