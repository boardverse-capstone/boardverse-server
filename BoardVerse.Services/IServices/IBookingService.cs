using BoardVerse.Core.DTOs.Booking;
using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices;

public interface IBookingService
{
    /// <summary>
    /// Tạo Booking từ Lobby đã lock.
    /// Flow: Lobby (Full) -> Host tạo Booking (PendingDeposit).
    /// Đồng thời update Lobby.BookingId = bookingId.
    /// </summary>
    Task<BookingResponseDto> CreateBookingAsync(Guid hostUserId, CreateBookingRequestDto request);

    /// <summary>
    /// Lấy chi tiết booking theo ID.
    /// </summary>
    Task<BookingResponseDto?> GetByIdAsync(Guid bookingId);

    /// <summary>
    /// Lấy chi tiết booking theo ID và kiểm tra quyền truy cập.
    /// Player: chỉ chủ booking (host lobby) hoặc member lobby.
    /// Manager: chỉ booking thuộc cafe của mình.
    /// Admin: xem tất cả.
    /// Trả về null nếu booking không tồn tại hoặc caller không có quyền.
    /// </summary>
    Task<BookingResponseDto?> GetByIdForCallerAsync(Guid bookingId, Guid callerUserId, string callerRole);

    /// <summary>
    /// Lấy booking theo lobby ID.
    /// </summary>
    Task<BookingResponseDto?> GetByLobbyIdAsync(Guid lobbyId);

    /// <summary>
    /// Lấy booking theo lobby ID, kiểm tra quyền caller là member của lobby hoặc Manager cafe / Admin.
    /// </summary>
    Task<BookingResponseDto?> GetByLobbyIdForCallerAsync(Guid lobbyId, Guid callerUserId, string callerRole);

    /// <summary>
    /// Lấy danh sách booking của cafe.
    /// </summary>
    Task<IReadOnlyList<BookingResponseDto>> GetByCafeIdAsync(Guid cafeId, Guid? requestingUserId = null);

    /// <summary>
    /// Cập nhật booking (chỉ một số trường được phép).
    /// Chỉ Owner (host) mới được sửa, và chỉ khi status cho phép.
    /// </summary>
    Task<BookingResponseDto> UpdateBookingAsync(Guid bookingId, Guid requestingUserId, UpdateBookingRequestDto request);

    /// <summary>
    /// Hủy booking bởi user.
    /// </summary>
    Task<BookingResponseDto> CancelBookingAsync(Guid bookingId, Guid requestingUserId, string? reason = null);

    /// <summary>
    /// Check-in tại quán (Manager/Staff).
    /// DEPRECATED — BR mới dùng Reservation (BVC). POS scan QR giờ dùng `ReservationCode`
    /// qua `CafePosService.StartSessionFromBookingAsync` (BVC flow).
    /// </summary>
    [Obsolete("Deprecated — BR mới dùng Reservation BVC. POS scan QR qua CafePosService.StartSessionFromBookingAsync.")]
    Task<BookingResponseDto> CheckInAsync(Guid bookingId, Guid staffUserId);

    /// <summary>
    /// Check-out tại quán (Manager/Staff).
    /// DEPRECATED — BR mới dùng `ReservationService.CompleteAndCaptureAsync`
    /// (BR-REVENUE-01: capture BVC deposit về doanh thu quán).
    /// </summary>
    [Obsolete("Deprecated — đã thay bằng ReservationService.CompleteAndCaptureAsync (BR-REVENUE-01).")]
    Task<BookingResponseDto> CheckOutAsync(Guid bookingId, Guid staffUserId);

    /// <summary>
    /// Xác nhận booking khi đã thanh toán cọc (internal, gọi từ PaymentService).
    /// </summary>
    Task<Booking> ConfirmBookingAsync(Guid bookingId);

    /// <summary>
    /// Đánh dấu NoShow khi khách không đến sau buffer time.
    /// </summary>
    Task<Booking> MarkAsNoShowAsync(Guid bookingId);

    /// <summary>
    /// Mobile task #8: GET /api/bookings/{id}/session-status
    /// Trả về ActiveSession realtime status (ActiveSession + members) cho member lobby xem.
    /// BR-12: Trả về bill về sớm của member nào đã partial-checkout.
    /// </summary>
    Task<BookingSessionStatusResponseDto> GetSessionStatusAsync(Guid bookingId, Guid requestingUserId);
}
