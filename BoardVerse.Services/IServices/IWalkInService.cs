using BoardVerse.Core.DTOs.WalkIn;
using BoardVerse.Core.Entities;

using System.Threading;
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
        Guid cafeId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tạo WalkInBooking từ POS.
    /// </summary>
    Task<WalkInBookingResponseDto> CreateWalkInBookingAsync(
        CreateWalkInBookingRequestDto request, Guid posStaffId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tạo WalkInWindow từ early checkout / no-show.
    /// </summary>
    Task<WalkInWindow> CreateWindowFromReservationAsync(
        Reservation reservation, int releasedSeats, DateTime windowStart, CancellationToken cancellationToken = default);

    /// <summary>
    /// GAP-14 Fix: Lấy WalkInWindow đang active cho 1 Reservation (dùng cho idempotency check).
    /// Trả về null nếu không có window active.
    /// </summary>
    Task<WalkInWindow?> GetActiveWindowByReservationIdAsync(
        Guid reservationId, CancellationToken cancellationToken = default);

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
