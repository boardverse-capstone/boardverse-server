using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho WalkInWindow entity (§9.3).
/// Key method: TryHoldSeatsAsync — OCC-based seat reservation for EC-06.
/// </summary>
public interface IWalkInWindowRepository
{
    /// <summary>
    /// Lấy WalkInWindow theo Id.
    /// </summary>
    Task<WalkInWindow?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Lấy WalkInWindow kèm navigation (WalkInBookings).
    /// </summary>
    Task<WalkInWindow?> GetByIdWithBookingsAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Lấy tất cả WalkInWindow đang Available/Partial cho 1 cafe + date.
    /// Dùng cho POS hiển thị danh sách walk-in windows.
    /// </summary>
    Task<IReadOnlyList<WalkInWindow>> GetActiveByCafeAsync(
        Guid cafeId, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Thử giữ seats với OCC (Optimistic Concurrency Control).
    /// BR-WALKIN-05: First-come-first-served — UPDATE với version check.
    /// EC-06: Race condition protection.
    ///
    /// Returns true nếu giữ thành công.
    /// Returns false nếu version conflict (nhiều POS cùng giữ).
    /// </summary>
    Task<bool> TryHoldSeatsAsync(Guid windowId, int seatsToHold, uint expectedVersion, CancellationToken ct = default);

    /// <summary>
    /// Release seats khi WalkInBooking bị hủy.
    /// </summary>
    Task<bool> TryReleaseSeatsAsync(Guid windowId, int seatsToRelease, uint expectedVersion, CancellationToken ct = default);

    /// <summary>
    /// Tạo WalkInWindow mới.
    /// </summary>
    Task<WalkInWindow> AddAsync(WalkInWindow window, CancellationToken ct = default);

    /// <summary>
    /// Đóng WalkInWindow (staff thủ công hoặc background job).
    /// </summary>
    Task CloseAsync(Guid windowId, CancellationToken ct = default);

    /// <summary>
    /// Lấy danh sách WalkInWindow đã hết hạn (WindowEnd &lt; now) và chưa closed.
    /// Dùng cho WalkInWindowCleanupJob (§4.4).
    /// </summary>
    Task<IReadOnlyList<WalkInWindow>> GetExpiredAsync(CancellationToken ct = default);

    /// <summary>
    /// Lấy WalkInWindow overlap với khoảng thời gian đề xuất.
    /// Dùng cho EC-05: extension không được overlap với WalkInWindow.
    /// </summary>
    Task<IReadOnlyList<WalkInWindow>> GetOverlappingAsync(
        Guid cafeId, DateTime windowStart, DateTime windowEnd, CancellationToken ct = default);

    /// <summary>
    /// GAP-14 Fix: Lấy WalkInWindow active (Available/Partial) cho 1 Reservation.
    /// Dùng cho idempotency check trước khi tạo WalkInWindow mới từ early checkout.
    /// </summary>
    Task<WalkInWindow?> GetActiveByReservationIdAsync(Guid reservationId, CancellationToken ct = default);
}
