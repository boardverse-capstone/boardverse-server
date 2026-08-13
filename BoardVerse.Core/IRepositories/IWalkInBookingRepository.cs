using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Repository cho WalkInBooking entity (§9.4).
/// </summary>
public interface IWalkInBookingRepository
{
    /// <summary>
    /// Lấy WalkInBooking theo Id.
    /// </summary>
    Task<WalkInBooking?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Lấy WalkInBooking theo IdempotencyKey (để skip duplicate).
    /// </summary>
    Task<WalkInBooking?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Tạo WalkInBooking mới.
    /// </summary>
    Task<WalkInBooking> AddAsync(WalkInBooking booking, CancellationToken ct = default);

    /// <summary>
    /// Cập nhật WalkInBooking (sau khi check-in, thanh toán, cancel).
    /// </summary>
    Task UpdateAsync(WalkInBooking booking, CancellationToken ct = default);

    /// <summary>
    /// Lấy danh sách WalkInBooking của 1 cafe.
    /// </summary>
    Task<IReadOnlyList<WalkInBooking>> GetByCafeAsync(Guid cafeId, CancellationToken ct = default);
}
