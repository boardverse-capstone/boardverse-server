using BoardVerse.Core.DTOs.Booking;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Service cho các API booking dành cho Player mobile (mobile gap #1, #2).
/// Tách khỏi <see cref="IBookingService"/> vì nhóm endpoint này read-only,
/// phục vụ UI trước khi user tạo booking, không mutate state.
/// </summary>
public interface ICafeBookingService
{
    /// <summary>
    /// Lấy danh sách bàn còn trống trong khung giờ [start, end] có <c>SeatCount >= seatCount</c>.
    /// Bị loại nếu trùng giờ với Booking khác (status != Cancelled) hoặc ActiveSession đang mở.
    /// </summary>
    Task<IReadOnlyList<AvailableCafeTableDto>> GetAvailableTablesAsync(
        Guid cafeId,
        DateTime scheduledStartTime,
        DateTime scheduleEndTime,
        int seatCount);

    /// <summary>
    /// Khảo sát capacity quán trong 1 khung giờ và đề xuất các slot thay thế.
    /// </summary>
    Task<CafeAvailabilityDto> GetAvailabilityAsync(
        Guid cafeId,
        DateTime startTime,
        DateTime endTime,
        int seatCount,
        Guid? gameTemplateId);
}