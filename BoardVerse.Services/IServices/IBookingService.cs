using BoardVerse.Core.DTOs.Booking;
using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices;

public interface IBookingService
{
    /// <summary>
    /// Tạo Booking từ Lobby đã lock.
    /// Flow: Lobby (Full) -> Host tạo Booking -> Booking (PendingDeposit).
    /// Đồng thời update Lobby.BookingId = bookingId.
    /// </summary>
    Task<BookingResponseDto> CreateBookingAsync(Guid hostUserId, CreateBookingRequestDto request);

    /// <summary>
    /// Lấy chi tiết booking theo ID.
    /// </summary>
    Task<BookingResponseDto?> GetByIdAsync(Guid bookingId);

    /// <summary>
    /// Lấy booking theo lobby ID.
    /// </summary>
    Task<BookingResponseDto?> GetByLobbyIdAsync(Guid lobbyId);

    /// <summary>
    /// Lấy danh sách booking của user.
    /// </summary>
    Task<IReadOnlyList<BookingResponseDto>> GetByUserIdAsync(Guid userId, Guid? requestingUserId = null);

    /// <summary>
    /// Lấy booking sắp tới của user (upcoming, confirmed/pending).
    /// </summary>
    Task<IReadOnlyList<BookingResponseDto>> GetUpcomingByUserIdAsync(Guid userId, int limit = 10);

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
    /// </summary>
    Task<BookingResponseDto> CheckInAsync(Guid bookingId, Guid staffUserId);

    /// <summary>
    /// Check-out tại quán (Manager/Staff).
    /// </summary>
    Task<BookingResponseDto> CheckOutAsync(Guid bookingId, Guid staffUserId);

    /// <summary>
    /// Xác nhận booking khi đã thanh toán cọc (internal, gọi từ PaymentService).
    /// </summary>
    Task<Booking> ConfirmBookingAsync(Guid bookingId);

    /// <summary>
    /// Cập nhật status booking.
    /// </summary>
    Task<Booking> UpdateStatusAsync(Guid bookingId, Core.Enum.BookingStatus newStatus);
}
