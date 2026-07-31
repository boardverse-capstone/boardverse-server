using BoardVerse.Core.DTOs.Booking;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Services.Services;

public class BookingService : IBookingService
{
    private readonly IBookingRepository _bookingRepository;
    private readonly ILobbyRepository _lobbyRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly ICafeTableRepository _cafeTableRepository;
    private readonly BoardVerseDbContext _db;

    public BookingService(
        IBookingRepository bookingRepository,
        ILobbyRepository lobbyRepository,
        ICafeRepository cafeRepository,
        ICafeTableRepository cafeTableRepository,
        BoardVerseDbContext db)
    {
        _bookingRepository = bookingRepository;
        _lobbyRepository = lobbyRepository;
        _cafeRepository = cafeRepository;
        _cafeTableRepository = cafeTableRepository;
        _db = db;
    }

    public async Task<BookingResponseDto> CreateBookingAsync(Guid hostUserId, CreateBookingRequestDto request)
    {
        // 1. Validate lobby đã lock (Full)
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

        // 2. Chưa có booking cho lobby này
        var existingBooking = await _bookingRepository.GetByLobbyIdAsync(request.LobbyId);
        if (existingBooking != null)
        {
            throw new ConflictException("Phòng chờ này đã có booking được tạo trước đó.");
        }

        // 3. Validate cafe tồn tại
        var cafe = await _cafeRepository.GetByIdAsync(request.CafeId)
            ?? throw new NotFoundException($"Không tìm thấy quán cafe '{request.CafeId}'.");

        // 4. Validate cafeTable thuộc cafe
        var cafeTable = await _cafeTableRepository.GetByIdAsync(request.CafeTableId)
            ?? throw new NotFoundException($"Không tìm thấy bàn '{request.CafeTableId}'.");

        if (cafeTable.CafeId != request.CafeId)
        {
            throw new BadRequestException("Bàn không thuộc quán đã chọn.");
        }

        // 5. Validate thời gian
        if (request.ScheduleEndTime <= request.ScheduledStartTime)
        {
            throw new BadRequestException("Thời gian kết thúc phải sau thời gian bắt đầu.");
        }

        if (request.ScheduledStartTime < DateTime.UtcNow)
        {
            throw new BadRequestException("Thời gian bắt đầu không được là thời điểm trong quá khứ.");
        }

        // P1 Fix #6: Wrap booking creation in transaction with pessimistic locking
        // to prevent race conditions when multiple users book the same table simultaneously
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // 6. Validate bàn không bị trùng giờ (với pessimistic lock)
            var conflicts = await _bookingRepository.GetConflictingBookingsWithLockAsync(
                request.CafeTableId, request.ScheduledStartTime, request.ScheduleEndTime);
            if (conflicts.Count > 0)
            {
                throw new ConflictException("Bàn đã có booking khác trong khoảng thời gian này.");
            }

            // 7. Tạo Booking
            var booking = new Booking
            {
                Id = Guid.NewGuid(),
                LobbyId = request.LobbyId,
                CafeId = request.CafeId,
                CafeTableId = request.CafeTableId,
                ScheduledStartTime = request.ScheduledStartTime,
                ScheduleEndTime = request.ScheduleEndTime,
                PlayerQuantity = request.PlayerQuantity ?? lobby.Members.Count(m => m.IsActive),
                Status = BookingStatus.PendingDeposit,
                VerificationQRCode = $"BV-{Guid.NewGuid():N}".Substring(0, 20)
            };

            // 8. Update Lobby.BookingId
            lobby.BookingId = booking.Id;
            lobby.UpdatedAt = DateTime.UtcNow;

            await _bookingRepository.AddAsync(booking);
            await _bookingRepository.SaveChangesAsync();

            await transaction.CommitAsync();

            booking.Cafe = cafe;
            booking.CafeTable = cafeTable;
            booking.Lobby = lobby;

            return BookingResponseDto.FromEntity(booking);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<BookingResponseDto?> GetByIdAsync(Guid bookingId)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRelations: true);
        return booking == null ? null : BookingResponseDto.FromEntity(booking);
    }

    public async Task<BookingResponseDto?> GetByLobbyIdAsync(Guid lobbyId)
    {
        var booking = await _bookingRepository.GetByLobbyIdAsync(lobbyId);
        return booking == null ? null : BookingResponseDto.FromEntity(booking);
    }

    public async Task<IReadOnlyList<BookingResponseDto>> GetByCafeIdAsync(Guid cafeId, Guid? requestingUserId = null)
    {
        var bookings = await _bookingRepository.GetByCafeIdAsync(cafeId);
        return bookings.Select(BookingResponseDto.FromEntity).ToList();
    }

    public async Task<BookingResponseDto> UpdateBookingAsync(
        Guid bookingId,
        Guid requestingUserId,
        UpdateBookingRequestDto request)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRelations: true)
            ?? throw new NotFoundException($"Không tìm thấy booking '{bookingId}'.");

        // Chỉ owner (Host lobby) mới được sửa
        if (booking.Lobby == null)
        {
            var lobby = await _lobbyRepository.GetByIdAsync(booking.LobbyId);
            if (lobby?.HostUserId != requestingUserId)
            {
                throw new ForbiddenException("Bạn không có quyền cập nhật booking này.");
            }
        }
        else if (booking.Lobby.HostUserId != requestingUserId)
        {
            throw new ForbiddenException("Bạn không có quyền cập nhật booking này.");
        }

        // Chỉ được sửa khi chưa check-in
        if (booking.Status == BookingStatus.CheckedIn || booking.Status == BookingStatus.Cancelled)
        {
            throw new ConflictException("Không thể cập nhật booking ở trạng thái này.");
        }

        if (request.CafeTableId.HasValue)
        {
            var table = await _cafeTableRepository.GetByIdAsync(request.CafeTableId.Value)
                ?? throw new NotFoundException($"Không tìm thấy bàn '{request.CafeTableId}'.");
            if (table.CafeId != booking.CafeId)
            {
                throw new BadRequestException("Bàn không thuộc quán của booking này.");
            }
            booking.CafeTableId = request.CafeTableId.Value;
        }

        if (request.ScheduledStartTime.HasValue)
            booking.ScheduledStartTime = request.ScheduledStartTime.Value;

        if (request.ScheduleEndTime.HasValue)
            booking.ScheduleEndTime = request.ScheduleEndTime.Value;

        if (booking.ScheduleEndTime <= booking.ScheduledStartTime)
        {
            throw new BadRequestException("Thời gian kết thúc phải sau thời gian bắt đầu.");
        }

        if (request.PlayerQuantity.HasValue)
            booking.PlayerQuantity = request.PlayerQuantity.Value;

        await _bookingRepository.UpdateAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        return BookingResponseDto.FromEntity(booking);
    }

    public async Task<BookingResponseDto> CancelBookingAsync(
        Guid bookingId,
        Guid requestingUserId,
        string? reason = null)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRelations: true)
            ?? throw new NotFoundException($"Không tìm thấy booking '{bookingId}'.");

        // Chỉ owner (Host lobby) hoặc Manager/Staff mới được hủy
        if (booking.Lobby != null && booking.Lobby.HostUserId != requestingUserId)
        {
            throw new ForbiddenException("Bạn không có quyền hủy booking này.");
        }

        if (booking.Status == BookingStatus.CheckedIn)
        {
            throw new ConflictException("Không thể hủy booking đã check-in.");
        }

        // P2 Fix #13: Release table when cancelling CONFIRMED booking (defensive: release regardless of current status)
        if (booking.Status == BookingStatus.Confirmed)
        {
            var table = await _cafeTableRepository.GetByIdAsync(booking.CafeTableId);
            if (table != null)
            {
                // Release table back to Available - defensive check to handle race conditions
                if (table.Status == CafeTableStatus.Reserved || table.Status == CafeTableStatus.InUse)
                {
                    table.Status = CafeTableStatus.Available;
                    await _cafeTableRepository.UpdateAsync(table);
                }
            }
        }

        booking.Status = BookingStatus.Cancelled;

        await _bookingRepository.UpdateAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        return BookingResponseDto.FromEntity(booking);
    }

    public async Task<BookingResponseDto> CheckInAsync(Guid bookingId, Guid staffUserId)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRelations: true)
            ?? throw new NotFoundException($"Không tìm thấy booking '{bookingId}'.");

        if (booking.Status != BookingStatus.Confirmed)
        {
            throw new ConflictException("Chỉ booking đã xác nhận (Confirmed) mới có thể check-in.");
        }

        booking.Status = BookingStatus.CheckedIn;

        await _bookingRepository.UpdateAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        return BookingResponseDto.FromEntity(booking);
    }

    public async Task<BookingResponseDto> CheckOutAsync(Guid bookingId, Guid staffUserId)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRelations: true)
            ?? throw new NotFoundException($"Không tìm thấy booking '{bookingId}'.");

        if (booking.Status != BookingStatus.CheckedIn)
        {
            throw new ConflictException("Chỉ booking đã check-in mới có thể check-out.");
        }

        // P0 Fix #2: CheckOut doesn't change booking status - the session handles the terminal state.
        // Booking status stays at CheckedIn (no terminal state in BookingStatus enum).
        // The ActiveSession handles the payment lifecycle independently.
        await _bookingRepository.UpdateAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        return BookingResponseDto.FromEntity(booking);
    }

    public async Task<Booking> ConfirmBookingAsync(Guid bookingId)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId)
            ?? throw new NotFoundException($"Không tìm thấy booking '{bookingId}'.");

        if (booking.Status != BookingStatus.PendingDeposit)
        {
            throw new ConflictException($"Chỉ booking ở trạng thái PendingDeposit mới có thể xác nhận (hiện tại: {booking.Status}).");
        }

        booking.Status = BookingStatus.Confirmed;

        await _bookingRepository.UpdateAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        return booking;
    }

    public async Task<Booking> MarkAsNoShowAsync(Guid bookingId)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId)
            ?? throw new NotFoundException($"Không tìm thấy booking '{bookingId}'.");

        if (booking.Status != BookingStatus.Confirmed && booking.Status != BookingStatus.PendingDeposit)
        {
            throw new ConflictException("Chỉ booking ở trạng thái Confirmed hoặc PendingDeposit mới có thể NoShow.");
        }

        booking.Status = BookingStatus.NoShow;

        await _bookingRepository.UpdateAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        return booking;
    }
}
