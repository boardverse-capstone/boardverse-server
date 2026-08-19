using BoardVerse.Core.DTOs.Reservation;

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
        Guid reservationId, int extensionMinutes, CancellationToken ct = default);

    /// <summary>
    /// Extend thời gian reservation.
    /// </summary>
    Task<ExtendReservationResponseDto> ExtendAsync(
        ExtendReservationRequestDto request, Guid userId, CancellationToken ct = default);
}
