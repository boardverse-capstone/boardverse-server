using BoardVerse.Core.DTOs.Booking;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Services.Services;

public class BookingService : IBookingService
{
    private readonly IBookingRepository _bookingRepository;
    private readonly ILobbyRepository _lobbyRepository;
    private readonly ICafeRepository _cafeRepository;

    public BookingService(
        IBookingRepository bookingRepository,
        ILobbyRepository lobbyRepository,
        ICafeRepository cafeRepository)
    {
        _bookingRepository = bookingRepository;
        _lobbyRepository = lobbyRepository;
        _cafeRepository = cafeRepository;
    }

    public async Task<BookingResponseDto> CreateBookingAsync(Guid hostUserId, CreateBookingRequestDto request)
    {
        // 1. Validate lobby tồn tại và đã lock (Full)
        var lobby = await _lobbyRepository.GetByIdWithMembersAsync(request.LobbyId)
            ?? throw new NotFoundException($"Không tìm thấy phòng chờ '{request.LobbyId}'.");

        var host = lobby.Members.FirstOrDefault(m => m.UserId == hostUserId && m.IsHost && m.IsActive);
        if (host == null)
        {
            throw new ForbiddenException("Chỉ Host của phòng chờ mới có thể tạo booking.");
        }

        if (lobby.Status != LobbyStatus.Full)
        {
            throw new ConflictException("Phòng chờ phải ở trạng thái Full (đã khóa) mới có thể tạo booking.");
        }

        // 2. Kiểm tra chưa có booking cho lobby này
        var existingBooking = await _bookingRepository.GetByLobbyIdAsync(request.LobbyId);
        if (existingBooking != null)
        {
            throw new ConflictException("Phòng chờ này đã có booking được tạo trước đó.");
        }

        // 3. Validate cafe tồn tại
        var cafe = await _cafeRepository.GetByIdAsync(request.CafeId)
            ?? throw new NotFoundException($"Không tìm thấy quán cafe '{request.CafeId}'.");

        // 4. Validate thời gian hợp lệ
        if (request.EndTime <= request.StartTime)
        {
            throw new BadRequestException("Giờ kết thúc phải sau giờ bắt đầu.");
        }

        if (request.BookingDate < DateTime.UtcNow.Date)
        {
            throw new BadRequestException("Ngày đặt chỗ không được là ngày trong quá khứ.");
        }

        // 5. Tạo Booking
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            LobbyId = request.LobbyId,
            CafeId = request.CafeId,
            UserId = hostUserId,
            BookingDate = request.BookingDate.Date,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            TotalSlot = request.TotalSlot ?? lobby.Members.Count(m => m.IsActive),
            TableNumber = request.TableNumber,
            TableCode = request.TableCode,
            SpecialRequest = request.SpecialRequest,
            Status = BookingStatus.PendingDeposit,
            CreatedAt = DateTime.UtcNow
        };

        // 6. Update Lobby.BookingId
        lobby.BookingId = booking.Id;
        lobby.UpdatedAt = DateTime.UtcNow;

        await _bookingRepository.AddAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        // Load relations for response
        booking.Cafe = cafe;
        booking.User = lobby.HostUser;
        booking.Lobby = lobby;

        return BookingResponseDto.FromEntity(booking, includeDeposit: false);
    }

    public async Task<BookingResponseDto?> GetByIdAsync(Guid bookingId)
    {
        var booking = await _bookingRepository.GetByIdWithDepositAsync(bookingId);
        return booking == null ? null : BookingResponseDto.FromEntity(booking, includeDeposit: true);
    }

    public async Task<BookingResponseDto?> GetByLobbyIdAsync(Guid lobbyId)
    {
        var booking = await _bookingRepository.GetByLobbyIdAsync(lobbyId);
        return booking == null ? null : BookingResponseDto.FromEntity(booking, includeDeposit: true);
    }

    public async Task<IReadOnlyList<BookingResponseDto>> GetByUserIdAsync(Guid userId, Guid? requestingUserId = null)
    {
        var bookings = await _bookingRepository.GetByUserIdAsync(userId);
        return bookings
            .Select(b => BookingResponseDto.FromEntity(b, includeDeposit: true))
            .ToList();
    }

    public async Task<IReadOnlyList<BookingResponseDto>> GetUpcomingByUserIdAsync(Guid userId, int limit = 10)
    {
        var bookings = await _bookingRepository.GetUpcomingByUserIdAsync(userId, limit);
        return bookings
            .Select(b => BookingResponseDto.FromEntity(b, includeDeposit: true))
            .ToList();
    }

    public async Task<BookingResponseDto> UpdateBookingAsync(
        Guid bookingId,
        Guid requestingUserId,
        UpdateBookingRequestDto request)
    {
        var booking = await _bookingRepository.GetByIdWithDepositAsync(bookingId)
            ?? throw new NotFoundException($"Không tìm thấy booking '{bookingId}'.");

        // Chỉ owner hoặc manager mới được sửa
        if (booking.UserId != requestingUserId)
        {
            throw new ForbiddenException("Bạn không có quyền cập nhật booking này.");
        }

        // Chỉ được sửa khi chưa check-in
        if (booking.Status == BookingStatus.CheckedIn ||
            booking.Status == BookingStatus.Completed ||
            booking.Status == BookingStatus.Cancelled)
        {
            throw new ConflictException("Không thể cập nhật booking ở trạng thái này.");
        }

        // Áp dụng các trường được phép
        if (request.BookingDate.HasValue)
            booking.BookingDate = request.BookingDate.Value.Date;

        if (request.StartTime.HasValue)
            booking.StartTime = request.StartTime.Value;

        if (request.EndTime.HasValue)
            booking.EndTime = request.EndTime.Value;

        if (request.TotalSlot.HasValue)
            booking.TotalSlot = request.TotalSlot.Value;

        if (request.TableNumber.HasValue)
            booking.TableNumber = request.TableNumber.Value;

        if (request.TableCode != null)
            booking.TableCode = request.TableCode;

        if (request.SpecialRequest != null)
            booking.SpecialRequest = request.SpecialRequest;

        booking.UpdatedAt = DateTime.UtcNow;

        await _bookingRepository.UpdateAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        return BookingResponseDto.FromEntity(booking, includeDeposit: true);
    }

    public async Task<BookingResponseDto> CancelBookingAsync(
        Guid bookingId,
        Guid requestingUserId,
        string? reason = null)
    {
        var booking = await _bookingRepository.GetByIdWithDepositAsync(bookingId)
            ?? throw new NotFoundException($"Không tìm thấy booking '{bookingId}'.");

        // Chỉ owner hoặc manager mới được hủy
        if (booking.UserId != requestingUserId)
        {
            throw new ForbiddenException("Bạn không có quyền hủy booking này.");
        }

        // Không cho hủy khi đã check-in hoặc completed
        if (booking.Status == BookingStatus.CheckedIn || booking.Status == BookingStatus.Completed)
        {
            throw new ConflictException("Không thể hủy booking đã check-in hoặc hoàn tất.");
        }

        booking.Status = BookingStatus.Cancelled;
        booking.CancellationReason = reason;
        booking.UpdatedAt = DateTime.UtcNow;

        await _bookingRepository.UpdateAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        return BookingResponseDto.FromEntity(booking, includeDeposit: true);
    }

    public async Task<BookingResponseDto> CheckInAsync(Guid bookingId, Guid staffUserId)
    {
        var booking = await _bookingRepository.GetByIdWithDepositAsync(bookingId)
            ?? throw new NotFoundException($"Không tìm thấy booking '{bookingId}'.");

        if (booking.Status != BookingStatus.Confirmed)
        {
            throw new ConflictException("Chỉ booking đã xác nhận (Confirmed) mới có thể check-in.");
        }

        booking.Status = BookingStatus.CheckedIn;
        booking.ActualStartTime = DateTime.UtcNow;
        booking.UpdatedAt = DateTime.UtcNow;

        await _bookingRepository.UpdateAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        return BookingResponseDto.FromEntity(booking, includeDeposit: true);
    }

    public async Task<BookingResponseDto> CheckOutAsync(Guid bookingId, Guid staffUserId)
    {
        var booking = await _bookingRepository.GetByIdWithDepositAsync(bookingId)
            ?? throw new NotFoundException($"Không tìm thấy booking '{bookingId}'.");

        if (booking.Status != BookingStatus.CheckedIn)
        {
            throw new ConflictException("Chỉ booking đã check-in mới có thể check-out.");
        }

        booking.Status = BookingStatus.Completed;
        booking.ActualEndTime = DateTime.UtcNow;
        booking.UpdatedAt = DateTime.UtcNow;

        await _bookingRepository.UpdateAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        return BookingResponseDto.FromEntity(booking, includeDeposit: true);
    }

    public async Task<Booking> ConfirmBookingAsync(Guid bookingId)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRelations: true)
            ?? throw new NotFoundException($"Không tìm thấy booking '{bookingId}'.");

        if (booking.Status != BookingStatus.PendingPayment)
        {
            throw new ConflictException($"Chỉ booking ở trạng thái PendingPayment mới có thể xác nhận (hiện tại: {booking.Status}).");
        }

        booking.Status = BookingStatus.Confirmed;
        booking.UpdatedAt = DateTime.UtcNow;

        await _bookingRepository.UpdateAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        return booking;
    }

    public async Task<Booking> UpdateStatusAsync(Guid bookingId, BookingStatus newStatus)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId)
            ?? throw new NotFoundException($"Không tìm thấy booking '{bookingId}'.");

        booking.Status = newStatus;
        booking.UpdatedAt = DateTime.UtcNow;

        await _bookingRepository.UpdateAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        return booking;
    }
}
