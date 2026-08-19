using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.CafeSchedule;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Service quản lý <see cref="CafeScheduleOverride"/> — cho phép cafe bật/tắt <see cref="TimeSlot"/>
/// hoặc đổi giờ mặc định. Triển khai mặc định: <c>CafeScheduleService</c>.
/// </summary>
public interface ICafeScheduleService
{
    /// <summary>
    /// Lấy toàn bộ schedule của cafe: 4 slot kèm override (hoặc default nếu không có).
    /// </summary>
    Task<CafeScheduleResponseDto> GetScheduleAsync(Guid cafeId);

    /// <summary>
    /// Tạo hoặc cập nhật override cho (cafeId, slot).
    /// Validate ownership: chỉ cafe manager mới được thao tác.
    /// </summary>
    Task<CafeScheduleOverrideResponseDto> UpsertOverrideAsync(
        Guid cafeId, Guid managerUserId, UpsertCafeScheduleOverrideRequestDto request);

    /// <summary>
    /// Xóa override → cafe quay về dùng default <c>CafeSchedule</c>.
    /// </summary>
    Task DeleteOverrideAsync(Guid cafeId, Guid managerUserId, TimeSlot slot);
}
