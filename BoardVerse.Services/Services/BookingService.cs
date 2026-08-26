using BoardVerse.Core.DTOs.Booking;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

public class BookingService : IBookingService
{
    private readonly IBookingRepository _bookingRepository;
    private readonly ILobbyRepository _lobbyRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly ICafeTableRepository _cafeTableRepository;
    private readonly IActiveSessionRepository _activeSessionRepository;
    private readonly BoardVerseDbContext _db;
    private readonly ILobbyHubService _hubService;
    private readonly IBookingRatingService _bookingRatingService;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        IBookingRepository bookingRepository,
        ILobbyRepository lobbyRepository,
        ICafeRepository cafeRepository,
        ICafeTableRepository cafeTableRepository,
        IActiveSessionRepository activeSessionRepository,
        BoardVerseDbContext db,
        ILobbyHubService hubService,
        IBookingRatingService bookingRatingService,
        ILogger<BookingService> logger)
    {
        _bookingRepository = bookingRepository;
        _lobbyRepository = lobbyRepository;
        _cafeRepository = cafeRepository;
        _cafeTableRepository = cafeTableRepository;
        _activeSessionRepository = activeSessionRepository;
        _db = db;
        _hubService = hubService;
        _bookingRatingService = bookingRatingService;
        _logger = logger;
    }

    public async Task<BookingResponseDto> CreateBookingAsync(Guid hostUserId, CreateBookingRequestDto request, CancellationToken cancellationToken = default)
    {
        Lobby? lobby = null;

        // 1. Validate lobby nếu có lobbyId (không bắt buộc - mobile gap #3 walk-in).
        if (request.LobbyId.HasValue && request.LobbyId.Value != Guid.Empty)
        {
            lobby = await _lobbyRepository.GetByIdWithMembersAsync(request.LobbyId.Value)
                ?? throw new NotFoundException(ApiErrorMessages.Booking.LobbyNotFoundForBooking(request.LobbyId.Value));

            var host = lobby.Members.FirstOrDefault(m => m.UserId == hostUserId && m.IsHost && m.IsActive);
            if (host == null)
            {
                throw new ForbiddenException(ApiErrorMessages.Booking.OnlyLobbyHostCanCreateBooking);
            }

            if (lobby.Status != LobbyStatus.Full)
            {
                throw new ConflictException(ApiErrorMessages.Booking.LobbyMustBeFullToCreateBooking);
            }
        }

        // 2. Chưa có booking cho lobby này (chỉ check khi có lobbyId).
        if (lobby != null)
        {
            var existingBooking = await _bookingRepository.GetByLobbyIdAsync(lobby.Id);
            if (existingBooking != null)
            {
                throw new ConflictException(ApiErrorMessages.Booking.LobbyAlreadyHasBooking);
            }
        }

        // 3. Validate cafe tồn tại
        var cafe = await _cafeRepository.GetByIdAsync(request.CafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Booking.CafeNotFound(request.CafeId));

        // 4. Validate cafeTable thuộc cafe
        var cafeTable = await _cafeTableRepository.GetByIdAsync(request.CafeTableId)
            ?? throw new NotFoundException(ApiErrorMessages.Booking.TableNotFound(request.CafeTableId));

        if (cafeTable.CafeId != request.CafeId)
        {
            throw new BadRequestException(ApiErrorMessages.Booking.TableNotInCafe);
        }

        // 5. Validate thời gian
        if (request.ScheduleEndTime <= request.ScheduledStartTime)
        {
            throw new BadRequestException(ApiErrorMessages.Booking.InvalidTimeRange);
        }

        if (request.ScheduledStartTime < DateTime.UtcNow)
        {
            throw new BadRequestException(ApiErrorMessages.Booking.StartTimeInPast);
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
                throw new ConflictException(ApiErrorMessages.Booking.TableAlreadyBookedInTimeRange);
            }

            // 7. Tạo Booking (walk-in cho phép LobbyId = null)
            var defaultPlayerQty = lobby?.Members.Count(m => m.IsActive) ?? 1;
            // GAP-11 fix: BR-07 — PlayerQuantity phải <= cafeTable.SeatCount.
            // Áp dụng cả walk-in (defaultPlayerQty) lẫn PlayerQuantity do client gửi.
            var requestedQty = request.PlayerQuantity ?? defaultPlayerQty;
            if (requestedQty > cafeTable.SeatCount)
            {
                throw new BadRequestException(
                    ApiErrorMessages.Booking.PlayerQuantityExceedsTableSeats(
                        requestedQty, cafeTable.Name ?? "", cafeTable.SeatCount));
            }

            var booking = new Booking
            {
                Id = Guid.NewGuid(),
                LobbyId = lobby?.Id, // null cho walk-in (mobile gap #3)
                CafeId = request.CafeId,
                CafeTableId = request.CafeTableId,
                ScheduledStartTime = request.ScheduledStartTime,
                ScheduleEndTime = request.ScheduleEndTime,
                PlayerQuantity = requestedQty,
                Status = BookingStatus.PendingDeposit,
                VerificationQRCode = $"BV-{Guid.NewGuid():N}".Substring(0, 20)
            };

            // 8. Update Lobby.BookingId (chỉ khi có lobby)
            // TD-01 FIX: Gán thành booking.Id — Lobby.BookingId là FK đến Booking (bảng đặt chỗ),
            // không phải BookingDeposit (bảng cọc). Navigation Lobby.Booking → Booking là đúng.
            if (lobby != null)
            {
                lobby.BookingId = booking.Id;
                lobby.UpdatedAt = DateTime.UtcNow;
            }

            await _bookingRepository.AddAsync(booking);
            await _bookingRepository.SaveChangesAsync();

            await transaction.CommitAsync();

            booking.Cafe = cafe;
            booking.CafeTable = cafeTable;
            booking.Lobby = lobby; // navigation null-safe cho walk-in

            return BookingResponseDto.FromEntity(booking);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<BookingResponseDto?> GetByIdAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRelations: true);
        return booking == null ? null : BookingResponseDto.FromEntity(booking);
    }

    public async Task<BookingResponseDto?> GetByIdForCallerAsync(Guid bookingId, Guid callerUserId, string callerRole, CancellationToken cancellationToken = default)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRelations: true);
        if (booking == null)
        {
            return null;
        }

        if (!await IsCallerAuthorizedForBookingAsync(booking, callerUserId, callerRole))
        {
            throw new ForbiddenException(ApiErrorMessages.Booking.NotBookingOwner);
        }

        return BookingResponseDto.FromEntity(booking);
    }

    public async Task<BookingResponseDto?> GetByLobbyIdAsync(Guid lobbyId, CancellationToken cancellationToken = default)
    {
        var booking = await _bookingRepository.GetByLobbyIdAsync(lobbyId);
        return booking == null ? null : BookingResponseDto.FromEntity(booking);
    }

    public async Task<BookingResponseDto?> GetByLobbyIdForCallerAsync(Guid lobbyId, Guid callerUserId, string callerRole, CancellationToken cancellationToken = default)
    {
        var booking = await _bookingRepository.GetByLobbyIdAsync(lobbyId);
        if (booking == null)
        {
            return null;
        }

        if (!await IsCallerAuthorizedForBookingAsync(booking, callerUserId, callerRole))
        {
            throw new ForbiddenException(ApiErrorMessages.Booking.NotBookingOwner);
        }

        return BookingResponseDto.FromEntity(booking);
    }

    /// <summary>
    /// C6/C7: Booking access policy.
    /// - Admin: xem tất cả.
    /// - Manager: chỉ booking thuộc cafe của mình.
    /// - Player: chỉ khi là host hoặc member của lobby liên kết.
    /// Walk-in booking không có lobby → Manager của cafe vẫn xem được.
    /// </summary>
    private async Task<bool> IsCallerAuthorizedForBookingAsync(Booking booking, Guid callerUserId, string callerRole)
    {
        if (callerRole == "Admin")
        {
            return true;
        }

        if (callerRole == "Manager")
        {
            var cafe = await _cafeRepository.GetByIdAsync(booking.CafeId);
            return cafe != null && cafe.ManagerId == callerUserId;
        }

        if (callerRole == "CafeStaff")
        {
            return await _cafeRepository.IsStaffMemberExistsAsync(booking.CafeId, callerUserId);
        }

        // Player: phải là host hoặc member của lobby.
        if (booking.LobbyId.HasValue)
        {
            var lobby = await _lobbyRepository.GetByIdWithMembersAsync(booking.LobbyId.Value);
            if (lobby == null)
            {
                return false;
            }
            if (lobby.HostUserId == callerUserId)
            {
                return true;
            }
            return lobby.Members.Any(m => m.UserId == callerUserId && m.IsActive);
        }

        // Walk-in booking không có lobby — chỉ Manager/CafeStaff/Admin xem.
        return false;
    }

    public async Task<IReadOnlyList<BookingResponseDto>> GetByCafeIdAsync(Guid cafeId, Guid? requestingUserId = null, bool isStaffOrManager = false, CancellationToken cancellationToken = default)
    {
        // GAP-C1: IDOR guard. Non-staff users (Player) get a sanitized list rendered by the controller;
        // here we still require the cafe to exist so we can return a clean 404 instead of an empty array
        // for invalid cafeIds.
        var cafe = await _cafeRepository.GetByIdAsync(cafeId)
            ?? throw new NotFoundException($"Không tìm thấy quán '{cafeId}'.");

        if (requestingUserId.HasValue && !isStaffOrManager)
        {
            var ownsAccess = await _cafeRepository.IsManagerOrStaffAsync(cafeId, requestingUserId.Value);
            if (!ownsAccess)
            {
                // Player view: only return bookings the caller actually participates in.
                var allBookings = await _bookingRepository.GetByCafeIdAsync(cafeId);
                return allBookings
                    .Where(b => b.Lobby != null
                                && (b.Lobby.HostUserId == requestingUserId.Value
                                    || b.Lobby.Members.Any(m => m.UserId == requestingUserId.Value && m.IsActive)))
                    .Select(BookingResponseDto.FromEntity)
                    .ToList();
            }
        }

        var bookings = await _bookingRepository.GetByCafeIdAsync(cafeId);
        return bookings.Select(BookingResponseDto.FromEntity).ToList();
    }

    public async Task<BookingResponseDto> UpdateBookingAsync(
        Guid bookingId,
        Guid requestingUserId,
        UpdateBookingRequestDto request, CancellationToken cancellationToken = default)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRelations: true)
            ?? throw new NotFoundException(ApiErrorMessages.Booking.NotFound(bookingId));

        // Chỉ owner (Host lobby) mới được sửa. Walk-in (LobbyId = null) chỉ owner mới được sửa.
        if (booking.LobbyId.HasValue)
        {
            if (booking.Lobby == null)
            {
                var lobby = await _lobbyRepository.GetByIdAsync(booking.LobbyId.Value);
                if (lobby?.HostUserId != requestingUserId)
                {
                    throw new ForbiddenException(ApiErrorMessages.Booking.NotBookingOwner);
                }
            }
            else if (booking.Lobby.HostUserId != requestingUserId)
            {
                throw new ForbiddenException(ApiErrorMessages.Booking.NotBookingOwner);
            }
        }
        else
        {
            // Walk-in booking: chỉ cho phép chính user tạo booking (deposit.UserId == userId) cập nhật.
            if (booking.BookingDeposit == null || booking.BookingDeposit.UserId != requestingUserId)
            {
                throw new ForbiddenException(ApiErrorMessages.Booking.NotBookingOwner);
            }
        }

        // Chỉ được sửa khi chưa check-in
        if (booking.Status == BookingStatus.CheckedIn || booking.Status == BookingStatus.Cancelled)
        {
            throw new ConflictException(ApiErrorMessages.Booking.CannotUpdateBookingInCurrentState);
        }

        // BUG-5 fix: Nếu đổi bàn, validate conflict trên bàn mới + release bàn cũ.
        var oldTableId = booking.CafeTableId;
        if (request.CafeTableId.HasValue && request.CafeTableId.Value != oldTableId)
        {
            var newTable = await _cafeTableRepository.GetByIdAsync(request.CafeTableId.Value)
                ?? throw new NotFoundException(ApiErrorMessages.Booking.TableNotFound(request.CafeTableId.Value));
            if (newTable.CafeId != booking.CafeId)
            {
                throw new BadRequestException(ApiErrorMessages.Booking.TableNotInBookingCafe);
            }

            // Defensive: check không conflict bàn khác (lọc trừ chính mình).
            // Tính time range dự kiến (ưu tiên request mới, fallback current).
            var newStart = request.ScheduledStartTime ?? booking.ScheduledStartTime;
            var newEnd = request.ScheduleEndTime ?? booking.ScheduleEndTime;
            var conflicts = await _bookingRepository.GetConflictingBookingsWithLockAsync(
                request.CafeTableId.Value, newStart, newEnd);
            var realConflicts = conflicts.Where(c => c.Id != booking.Id).ToList();
            if (realConflicts.Count > 0)
            {
                throw new ConflictException(ApiErrorMessages.Booking.TableAlreadyBookedInTimeRange);
            }

            // Apply new table + release old table (nếu không có ActiveSession nào giữ).
            booking.CafeTableId = request.CafeTableId.Value;
            await TryReleaseTableIfIdleAsync(oldTableId, booking.CafeId, DateTime.UtcNow);
        }

        if (request.ScheduledStartTime.HasValue)
            booking.ScheduledStartTime = request.ScheduledStartTime.Value;

        if (request.ScheduleEndTime.HasValue)
            booking.ScheduleEndTime = request.ScheduleEndTime.Value;

        if (booking.ScheduleEndTime <= booking.ScheduledStartTime)
        {
            throw new BadRequestException(ApiErrorMessages.Booking.InvalidTimeRange);
        }

        if (request.PlayerQuantity.HasValue)
        {
            // GAP-11 fix: BR-07 — playerQuantity <= cafeTable.SeatCount (áp dụng cả walk-in).
            var liveTable = booking.CafeTable != null && booking.CafeTable.Id == booking.CafeTableId
                ? booking.CafeTable
                : await _cafeTableRepository.GetByIdAsync(booking.CafeTableId);
            if (liveTable != null && request.PlayerQuantity.Value > liveTable.SeatCount)
            {
                throw new ConflictException(
                    ApiErrorMessages.Booking.PlayerQuantityExceedsTableSeats(
                        request.PlayerQuantity.Value, liveTable.Name ?? "", liveTable.SeatCount));
            }

            // BR-22 + mobile gap #15: playerQuantity <= số members active trong lobby
            // (walk-in booking LobbyId = null → bỏ qua check này).
            if (booking.LobbyId.HasValue)
            {
                var lobbyForQty = booking.Lobby != null && booking.Lobby.Members != null
                    ? booking.Lobby
                    : await _lobbyRepository.GetByIdWithMembersAsync(booking.LobbyId.Value);
                if (lobbyForQty != null)
                {
                    var currentMembers = lobbyForQty.Members?.Count(m => m.IsActive) ?? 0;
                    if (request.PlayerQuantity.Value > currentMembers)
                    {
                        throw new ConflictException(
                            $"Số lượng người chơi ({request.PlayerQuantity.Value}) vượt quá số thành viên hiện tại trong lobby ({currentMembers}).");
                    }
                }
            }
            booking.PlayerQuantity = request.PlayerQuantity.Value;
        }

        await _bookingRepository.UpdateAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        return BookingResponseDto.FromEntity(booking);
    }

    /// <summary>
    /// BUG-5 helper: Release bàn về Available nếu bàn đang Reserved/InUse nhưng
    /// không có ActiveSession nào đang giữ. Dùng khi move booking sang bàn khác
    /// hoặc khi cancel booking Confirmed.
    /// </summary>
    private async Task TryReleaseTableIfIdleAsync(Guid cafeTableId, Guid cafeId, DateTime nowUtc)
    {
        var table = await _cafeTableRepository.GetByIdAsync(cafeTableId);
        if (table == null)
        {
            return;
        }
        if (table.Status != CafeTableStatus.Reserved && table.Status != CafeTableStatus.InUse)
        {
            return;
        }

        var hasActiveSession = await _db.ActiveSessions
            .AsNoTracking()
            .AnyAsync(s => s.CafeTableId == cafeTableId
                           && s.CafeId == cafeId
                           && (s.Status == GroupSessionStatus.Active
                               || s.Status == GroupSessionStatus.Checking
                               || s.Status == GroupSessionStatus.Unpaid));

        if (!hasActiveSession)
        {
            table.Status = CafeTableStatus.Available;
            table.UpdatedAt = nowUtc;
            await _cafeTableRepository.UpdateAsync(table);
        }
    }

    public async Task<BookingResponseDto> CancelBookingAsync(
        Guid bookingId,
        Guid requestingUserId,
        string? reason = null, CancellationToken cancellationToken = default)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRelations: true)
            ?? throw new NotFoundException(ApiErrorMessages.Booking.NotFound(bookingId));

        // Chỉ owner (Host lobby) hoặc Manager/Staff mới được hủy.
        // Walk-in booking: cho phép chính user đặt cọc (deposit.UserId) hủy.
        if (booking.Lobby != null)
        {
            if (booking.Lobby.HostUserId != requestingUserId)
            {
                throw new ForbiddenException(ApiErrorMessages.Booking.NotBookingOwner);
            }
        }
        else if (booking.BookingDeposit == null || booking.BookingDeposit.UserId != requestingUserId)
        {
            throw new ForbiddenException(ApiErrorMessages.Booking.NotBookingOwner);
        }

        if (booking.Status == BookingStatus.CheckedIn)
        {
            throw new ConflictException(ApiErrorMessages.Booking.CannotCancelCheckedInBooking);
        }

        // P2 Fix #13: Release table when cancelling CONFIRMED booking (defensive: release regardless of current status)
        if (booking.Status == BookingStatus.Confirmed)
        {
            var table = await _cafeTableRepository.GetByIdAsync(booking.CafeTableId);
            // Gap-Fix 2026-08-15: defensive check session active trên bàn trước khi release.
            // Nếu đã có ActiveSession chưa thanh toán trên bàn này (vd session khác đang chơi,
            // hoặc session đã start từ trước khi booking bị cancel) → KHÔNG set Available.
            // Self-healing fix ở GetTablesAsync sẽ trả InUse nhưng vẫn giữ logic ở đây nhất quán.
            if (table != null)
            {
                var hasActiveSession = await _db.ActiveSessions
                    .AsNoTracking()
                    .AnyAsync(s => s.CafeTableId == table.Id
                                   && s.CafeId == table.CafeId
                                   && (s.Status == GroupSessionStatus.Active
                                       || s.Status == GroupSessionStatus.Checking
                                       || s.Status == GroupSessionStatus.Unpaid));

                if (!hasActiveSession &&
                    (table.Status == CafeTableStatus.Reserved || table.Status == CafeTableStatus.InUse))
                {
                    table.Status = CafeTableStatus.Available;
                    await _cafeTableRepository.UpdateAsync(table);
                }
            }
        }

        booking.Status = BookingStatus.Cancelled;

        await _bookingRepository.UpdateAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        // SignalR broadcast — task #7: BookingCancelled
        // refundStatus = "None" cho player-initiated cancel; full refund semantics đã được xử lý riêng bởi DepositRefundPolicy
        await _hubService.NotifyBookingCancelled(bookingId, requestingUserId, reason ?? "PlayerCancelled", refundStatus: "PendingPolicy");

        return BookingResponseDto.FromEntity(booking);
    }

    [Obsolete("Deprecated — BR mới dùng Reservation BVC flow. POS check-in qua CafePosService.StartSessionFromBookingAsync (ReservationCode).")]
    public Task<BookingResponseDto> CheckInAsync(Guid bookingId, Guid staffUserId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "BookingService.CheckInAsync đã deprecated. POS scan QR giờ dùng ReservationCode (BVC flow) qua CafePosService.StartSessionFromBookingAsync.");

    [Obsolete("Deprecated — đã thay bằng ReservationService.CompleteAndCaptureAsync (BR-REVENUE-01).")]
    public Task<BookingResponseDto> CheckOutAsync(Guid bookingId, Guid staffUserId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "BookingService.CheckOutAsync đã deprecated. Capture BVC deposit giờ do ReservationService xử lý (BR-REVENUE-01).");

    public async Task<Booking> ConfirmBookingAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId)
            ?? throw new NotFoundException(ApiErrorMessages.Booking.NotFound(bookingId));

        if (booking.Status != BookingStatus.PendingDeposit)
        {
            throw new ConflictException(ApiErrorMessages.Booking.OnlyPendingDepositCanConfirm(booking.Status));
        }

        booking.Status = BookingStatus.Confirmed;

        await _bookingRepository.UpdateAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        return booking;
    }

    public async Task<Booking> MarkAsNoShowAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId)
            ?? throw new NotFoundException(ApiErrorMessages.Booking.NotFound(bookingId));

        if (booking.Status != BookingStatus.Confirmed && booking.Status != BookingStatus.PendingDeposit)
        {
            throw new ConflictException(ApiErrorMessages.Booking.OnlyConfirmedOrPendingDepositCanNoShow);
        }

        booking.Status = BookingStatus.NoShow;

        await _bookingRepository.UpdateAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        return booking;
    }

    public async Task<BookingSessionStatusResponseDto> GetSessionStatusAsync(Guid bookingId, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRelations: true)
            ?? throw new NotFoundException(ApiErrorMessages.Booking.NotFound(bookingId));

        // AuthZ: chỉ member lobby hoặc deposit owner mới xem được
        bool isMember = false;
        if (booking.LobbyId.HasValue)
        {
            var lobby = booking.Lobby ?? await _lobbyRepository.GetByIdWithMembersAsync(booking.LobbyId.Value);
            isMember = lobby?.Members.Any(m => m.UserId == requestingUserId && m.IsActive) ?? false;
        }
        else
        {
            // Walk-in booking
            isMember = booking.BookingDeposit?.UserId == requestingUserId;
        }

        if (!isMember)
        {
            throw new ForbiddenException(ApiErrorMessages.Booking.NotMemberOfBooking);
        }

        var response = new BookingSessionStatusResponseDto
        {
            BookingId = bookingId,
            SessionStatus = "NotStarted",
            StartedAt = null,
            CurrentDurationMinutes = 0,
            TableNumber = booking.TableNumber,
            Members = new List<BookingSessionMemberStatusDto>(),
            EstimatedFinalBill = null
        };

        // Walk-in booking hoặc lobby chưa check-in → không có ActiveSession
        if (!booking.LobbyId.HasValue)
        {
            return response;
        }

        var session = await _activeSessionRepository.GetByLobbyIdWithMembersAsync(booking.LobbyId.Value);
        if (session == null)
        {
            return response;
        }

        response.ActiveSessionId = session.Id;
        response.SessionStatus = session.Status.ToString();
        response.StartedAt = session.StartedAt;
        response.CurrentDurationMinutes = (int)(DateTime.UtcNow - session.StartedAt).TotalMinutes;

        // Members — bao gồm cả Guest_Slot
        foreach (var m in session.Members)
        {
            response.Members.Add(new BookingSessionMemberStatusDto
            {
                UserId = m.UserId ?? Guid.Empty,
                Username = m.IsGuestSlot
                    ? (m.GuestDisplayName ?? "Khách vô danh")
                    : (m.User?.Username ?? "Unknown"),
                Status = m.IsGuestSlot ? "GuestSlot" : m.Status.ToString(),
                LeftAt = m.LeftAt,
                PartialBillAmount = m.PenaltyAmount,
                PartialBillPaid = m.IsPenaltyPaid && m.IsCheckedOut,
                MergedIntoSessionId = m.OriginalSessionId
            });
        }

        response.EstimatedFinalBill = new BookingSessionEstimatedBillDto
        {
            Subtotal = session.Subtotal,
            Penalty = session.Members.Sum(m => m.PenaltyAmount),
            DepositApplied = session.DepositAppliedAmount,
            Total = session.TotalAmount
        };

        return response;
    }
}
