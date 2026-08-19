using BoardVerse.Core.DTOs.TimeSlotOverride;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Service quản lý TimeSlot cho manager.
/// </summary>
/// <remarks>
/// Theo BR-NEW-15 §7.1, <c>TimeSlot</c> enum là cố định (4 slot: Morning, Afternoon, Evening, LateNight).
/// Manager không thể thêm slot mới — chỉ override StartTime/EndTime/IsClosed theo từng cafe qua <c>CafeScheduleOverride</c>.
/// </remarks>
public interface ITimeSlotService
{
    /// <summary>
    /// Lấy 4 TimeSlot mặc định của hệ thống (read-only metadata cho UI manager).
    /// </summary>
    Task<IReadOnlyList<DefaultTimeSlotDto>> GetDefaultTimeSlotsAsync();

    /// <summary>
    /// Lấy toàn bộ TimeSlot của cafe (4 slot — gộp default + override nếu có).
    /// Validate ownership: chỉ cafe manager mới được xem.
    /// </summary>
    Task<IReadOnlyList<ManagerTimeSlotResponseDto>> GetCafeTimeSlotsAsync(
        Guid cafeId, Guid managerUserId);

    /// <summary>
    /// Lấy 1 TimeSlot của cafe (default + override nếu có).
    /// </summary>
    /// <returns>
    /// Trả <c>ManagerTimeSlotResponseDto</c> với <c>HasOverride = false</c> nếu cafe chưa override.
    /// Trả <c>null</c> chỉ khi cafe không tồn tại.
    /// </returns>
    Task<ManagerTimeSlotResponseDto> GetCafeTimeSlotAsync(
        Guid cafeId, Guid managerUserId, string slotName);

    /// <summary>
    /// Tạo override cho 1 TimeSlot. Validate ownership + unique (cafe, slot).
    /// Nếu đã tồn tại override → throw ConflictException (dùng PUT để update).
    /// </summary>
    Task<ManagerTimeSlotResponseDto> CreateOverrideAsync(
        Guid cafeId, Guid managerUserId, CreateTimeSlotOverrideRequestDto request);

    /// <summary>
    /// Cập nhật override cho 1 TimeSlot (partial update — chỉ field có giá trị).
    /// Nếu chưa có override → throw NotFoundException (dùng POST để tạo).
    /// </summary>
    Task<ManagerTimeSlotResponseDto> UpdateOverrideAsync(
        Guid cafeId, Guid managerUserId, string slotName, UpdateTimeSlotOverrideRequestDto request);

    /// <summary>
    /// Xóa override cho 1 TimeSlot → cafe quay về dùng default.
    /// Idempotent: nếu chưa có override thì vẫn thành công.
    /// </summary>
    Task DeleteOverrideAsync(
        Guid cafeId, Guid managerUserId, string slotName);
}
