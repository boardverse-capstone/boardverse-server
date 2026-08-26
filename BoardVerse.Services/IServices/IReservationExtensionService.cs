using BoardVerse.Core.DTOs.Reservation;

using System.Threading;
namespace BoardVerse.Services.IServices;

/// <summary>
/// Service interface cho extension flow (Phase 3).
/// </summary>
public interface IReservationExtensionService
{
    /// <summary>
    /// Check availability trước khi extend (không thay đổi DB).
    /// </summary>
    Task<ExtendAvailabilityDto> CheckAvailabilityAsync(
        Guid reservationId, int extensionMinutes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extend thời gian reservation.
    /// </summary>
    Task<ExtendReservationResponseDto> ExtendAsync(
        ExtendReservationRequestDto request, Guid userId, CancellationToken cancellationToken = default);
}
