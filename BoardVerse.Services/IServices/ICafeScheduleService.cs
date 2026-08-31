using BoardVerse.Core.DTOs.CafeSchedule;
using BoardVerse.Core.Entities;
using System.Threading;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Service quản lý CafeScheduleOverride.
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot - dùng ApplyDate/OpenTime/CloseTime.
/// GAP-FIX (2026-09-01): Bulk upsert, single-get.
/// </summary>
public interface ICafeScheduleService
{
    /// <summary>
    /// Lấy toàn bộ schedule override của cafe.
    /// </summary>
    /// <param name="managerUserId">Nếu provided, verify authz (manager/staff) trước khi trả data.</param>
    Task<CafeScheduleResponseDto> GetScheduleAsync(
        Guid cafeId,
        Guid? managerUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy override cho (cafeId, applyDate). Trả về null nếu không có override.
    /// </summary>
    Task<CafeScheduleOverrideResponseDto?> GetOverrideAsync(
        Guid cafeId, DateOnly applyDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tạo hoặc cập nhật override cho (cafeId, applyDate).
    /// </summary>
    Task<CafeScheduleOverrideResponseDto> UpsertOverrideAsync(
        Guid cafeId, Guid managerUserId, UpsertCafeScheduleOverrideRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk upsert nhiều override trong 1 transaction.
    /// </summary>
    Task<List<CafeScheduleOverrideResponseDto>> UpsertBulkOverridesAsync(
        Guid cafeId, Guid managerUserId, List<UpsertCafeScheduleOverrideRequestDto> requests,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xóa override cho ngày cụ thể (idempotent).
    /// </summary>
    Task DeleteOverrideAsync(Guid cafeId, Guid managerUserId, DateOnly applyDate,
        CancellationToken cancellationToken = default);
}
