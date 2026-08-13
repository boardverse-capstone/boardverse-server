using BoardVerse.Core.DTOs.WalkIn;
using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Service interface cho Walk-in flow (Phase 2).
/// </summary>
public interface IWalkInService
{
    /// <summary>
    /// Lấy danh sách WalkInWindow đang Available/Partial của 1 cafe + date.
    /// </summary>
    Task<WalkInWindowsResponseDto> GetWalkInWindowsAsync(
        Guid cafeId, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Tạo WalkInBooking từ POS.
    /// </summary>
    Task<WalkInBookingResponseDto> CreateWalkInBookingAsync(
        CreateWalkInBookingRequestDto request, Guid posStaffId, CancellationToken ct = default);

    /// <summary>
    /// Tạo WalkInWindow từ early checkout / no-show.
    /// </summary>
    Task<WalkInWindow> CreateWindowFromReservationAsync(
        Reservation reservation, int releasedSeats, DateTime windowStart, CancellationToken ct = default);

    /// <summary>
    /// Cleanup expired WalkInWindows (gọi bởi background job).
    /// </summary>
    Task CleanupExpiredWindowsAsync(CancellationToken ct = default);

    /// <summary>
    /// Đóng WalkInWindow thủ công bởi POS staff.
    /// </summary>
    Task CloseWindowAsync(Guid windowId, string? reason = null, CancellationToken ct = default);

    /// <summary>
    /// Hủy WalkInBooking (chỉ khi chưa check-in).
    /// Trả ghế về WalkInWindow.
    /// </summary>
    Task CancelWalkInBookingAsync(Guid walkInBookingId, CancellationToken ct = default);
}
