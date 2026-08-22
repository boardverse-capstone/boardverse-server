using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.CafeSchedule;
using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Services.IServices;

/// <summary>
/// Service quản lý CafeScheduleOverride.
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot - dùng ApplyDate/OpenTime/CloseTime.
/// </summary>
public interface ICafeScheduleService
{
    /// <summary>
    /// Lấy toàn bộ schedule override của cafe.
    /// </summary>
    Task<CafeScheduleResponseDto> GetScheduleAsync(Guid cafeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tạo hoặc cập nhật override cho (cafeId, applyDate).
    /// </summary>
    Task<CafeScheduleOverrideResponseDto> UpsertOverrideAsync(
        Guid cafeId, Guid managerUserId, UpsertCafeScheduleOverrideRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xóa override cho ngày cụ thể.
    /// </summary>
    Task DeleteOverrideAsync(Guid cafeId, Guid managerUserId, DateOnly applyDate);
}
