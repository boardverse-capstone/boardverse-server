using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.WalkIn;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

/// <summary>
/// Service cho Walk-in flow (Phase 2).
///
/// Phục vụ:
/// - POS: xem WalkInWindow trống, tạo WalkInBooking
/// - Background job: tạo WalkInWindow khi early checkout / no-show
/// - Background job: cleanup expired windows
///
/// BR-WALKIN-01: Chỉ tạo walk-in khi WalkInWindow.Status ∈ {Available, Partial}.
/// BR-WALKIN-04: Walk-in KHÔNG cọc — thanh toán 100% tại POS.
/// BR-WALKIN-05: OCC trên WalkInWindow.Version (EC-06).
/// </summary>
public class WalkInService : IWalkInService
{
    private readonly IWalkInWindowRepository _windowRepository;
    private readonly IWalkInBookingRepository _bookingRepository;
    private readonly IActiveSessionRepository _sessionRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly ICafeInventoryRepository _cafeInventoryRepository;
    private readonly ILogger<WalkInService> _logger;

    public WalkInService(
        IWalkInWindowRepository windowRepository,
        IWalkInBookingRepository bookingRepository,
        IActiveSessionRepository sessionRepository,
        ICafeRepository cafeRepository,
        ICafeInventoryRepository cafeInventoryRepository,
        ILogger<WalkInService> logger)
    {
        _windowRepository = windowRepository;
        _bookingRepository = bookingRepository;
        _sessionRepository = sessionRepository;
        _cafeRepository = cafeRepository;
        _cafeInventoryRepository = cafeInventoryRepository;
        _logger = logger;
    }

    /// <summary>
    /// Lấy danh sách WalkInWindow đang Available/Partial của 1 cafe + date.
    /// </summary>
    public async Task<WalkInWindowsResponseDto> GetWalkInWindowsAsync(
        Guid cafeId, DateOnly date, CancellationToken ct = default)
    {
        var windows = await _windowRepository.GetActiveByCafeAsync(cafeId, date, ct);

        var dtos = windows.Select(w => new WalkInWindowDto
        {
            Id = w.Id,
            SourceReservationId = w.SourceReservationId,
            WindowStart = w.WindowStart,
            WindowEnd = w.WindowEnd,
            TotalSeats = w.TotalSeats,
            AvailableSeats = w.AvailableSeats,
            HeldSeats = w.HeldSeats,
            InUseSeats = w.InUseSeats,
            Status = w.Status.ToString(),
            ExpiresAt = w.ExpiresAt,
            CreatedAt = w.CreatedAt
        }).ToList();

        return new WalkInWindowsResponseDto { Items = dtos };
    }

    /// <summary>
    /// Tạo WalkInBooking từ POS.
    ///
    /// BR-WALKIN-01: Validate window còn Available/Partial.
    /// BR-WALKIN-05: OCC trên WalkInWindow.Version.
    /// BR-WALKIN-04: Walk-in KHÔNG cọc — tạo ActiveSession sau check-in.
    ///
    /// Nếu idempotencyKey trùng → return existing booking.
    /// </summary>
    public async Task<WalkInBookingResponseDto> CreateWalkInBookingAsync(
        CreateWalkInBookingRequestDto request, Guid posStaffId, CancellationToken ct = default)
    {
        // 1. Idempotency check
        if (!string.IsNullOrEmpty(request.IdempotencyKey))
        {
            var existing = await _bookingRepository.GetByIdempotencyKeyAsync(request.IdempotencyKey, ct);
            if (existing != null)
            {
                _logger.LogInformation("Walk-in booking idempotent hit: {Key}", request.IdempotencyKey);
                return MapToResponse(existing);
            }
        }

        // 2. Validate WalkInWindow
        var window = await _windowRepository.GetByIdAsync(request.WalkInWindowId, ct);
        if (window == null)
        {
            throw new NotFoundException(ApiErrorMessages.WalkIn.WalkInWindowNotFound(request.WalkInWindowId));
        }

        if (window.Status != WalkInWindowStatus.Available
            && window.Status != WalkInWindowStatus.Partial)
        {
            throw new ConflictException(
                ApiErrorMessages.WalkIn.WalkInWindowNotAvailable(window.Id, window.Status.ToString()));
        }

        if (window.AvailableSeats < request.Seats)
        {
            throw new ConflictException(
                ApiErrorMessages.WalkIn.NotEnoughSeats(request.Seats, window.AvailableSeats));
        }

        // 3. Validate Cafe
        var cafe = await _cafeRepository.GetActiveByIdAsync(window.CafeId);
        if (cafe == null)
        {
            throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(window.CafeId));
        }

        // 4. OCC: Thử giữ ghế
        var held = await _windowRepository.TryHoldSeatsAsync(
            window.Id, request.Seats, window.Version, ct);

        if (!held)
        {
            throw new ConflictException(
                ApiErrorMessages.WalkIn.ConcurrentBooking);
        }

        // 5. Tạo WalkInBooking
        // EndTime ≤ WindowEnd (có thể ngắn hơn nếu khách không chơi full window)
        var endTime = window.WindowEnd; // default: full window

        var booking = new WalkInBooking
        {
            Id = Guid.NewGuid(),
            WalkInWindowId = window.Id,
            CafeId = window.CafeId,
            GuestName = request.GuestName,
            GuestPhone = request.GuestPhone,
            StartTime = window.WindowStart,
            EndTime = endTime,
            Seats = request.Seats,
            HourlyRate = 0, // sẽ update khi check-in + gán game
            TotalAmount = 0, // sẽ update khi tính tiền
            PaymentStatus = WalkInPaymentStatus.Unpaid,
            PosStaffId = posStaffId,
            Status = WalkInBookingStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _bookingRepository.AddAsync(booking, ct);
        }
        catch (Exception ex)
        {
            // Rollback seat hold nếu insert fail
            _logger.LogError(ex, "Failed to create WalkInBooking, releasing seats");
            await _windowRepository.TryReleaseSeatsAsync(
                window.Id, request.Seats, window.Version + 1, ct);
            throw;
        }

        _logger.LogInformation(
            "WalkInBooking created: {Id} for window {WindowId}, {Seats} seats",
            booking.Id, window.Id, request.Seats);

        return MapToResponse(booking);
    }

    /// <summary>
    /// Tạo WalkInWindow từ early checkout hoặc no-show.
    /// Gọi bởi ActiveSessionService khi session kết thúc sớm,
    /// hoặc bởi NoShowDetectionJob.
    ///
    /// `releasedSeats` = số ghế được giải phóng (không phải tất cả totalSeats).
    /// `windowStart` = thời điểm early checkout / no-show xảy ra.
    /// `windowEnd` = Reservation.ScheduledEndTime (BR-RESV-02).
    /// </summary>
    public async Task<WalkInWindow> CreateWindowFromReservationAsync(
        Reservation reservation,
        int releasedSeats,
        DateTime windowStart,
        CancellationToken ct = default)
    {
        if (releasedSeats <= 0)
        {
            _logger.LogDebug("No seats to release for reservation {Id}", reservation.Id);
            return null!;
        }

        // Default: window tồn tại trong 30 phút kể từ windowStart
        var expiresAt = windowStart.AddMinutes(30);
        var windowEnd = reservation.ScheduledEndTime;
        if (windowEnd == default)
            throw new InternalServerErrorException(
                ApiErrorMessages.WalkIn.ReservationMissingScheduledEndTime);

        var window = new WalkInWindow
        {
            Id = Guid.NewGuid(),
            SourceReservationId = reservation.Id,
            CafeId = reservation.CafeId,
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            TotalSeats = releasedSeats,
            AvailableSeats = releasedSeats,
            HeldSeats = 0,
            InUseSeats = 0,
            Version = 1,
            Status = WalkInWindowStatus.Available,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        };

        await _windowRepository.AddAsync(window, ct);

        _logger.LogInformation(
            "WalkInWindow created from reservation {Id}: {Seats} seats, {Start} - {End}",
            reservation.Id, releasedSeats, windowStart, reservation.ScheduledEndTime);

        return window;
    }

    /// <summary>
    /// Cleanup expired WalkInWindows (gọi bởi WalkInWindowCleanupJob).
    /// Đóng windows đã hết hạn (WindowEnd &lt; now và chưa closed).
    /// </summary>
    public async Task CleanupExpiredWindowsAsync(CancellationToken ct = default)
    {
        var expired = await _windowRepository.GetExpiredAsync(ct);

        foreach (var window in expired)
        {
            await _windowRepository.CloseAsync(window.Id, ct);
            _logger.LogInformation("Closed expired WalkInWindow: {Id}", window.Id);
        }
    }

    /// <summary>
    /// Đóng WalkInWindow thủ công bởi POS staff.
    /// </summary>
    public async Task CloseWindowAsync(Guid windowId, string? reason = null, CancellationToken ct = default)
    {
        var window = await _windowRepository.GetByIdAsync(windowId, ct);
        if (window == null)
        {
            throw new NotFoundException(ApiErrorMessages.WalkIn.WalkInWindowNotFound(windowId));
        }

        await _windowRepository.CloseAsync(windowId, ct);
        _logger.LogInformation(
            "WalkInWindow {Id} closed by POS. Reason: {Reason}", windowId, reason ?? "N/A");
    }

    /// <summary>
    /// Hủy WalkInBooking (chỉ khi chưa check-in / Status = Active).
    /// Trả ghế về WalkInWindow để có thể bán lại.
    /// §10.3: POST /api/v1/reservations/walkin/{id}/cancel
    /// </summary>
    public async Task CancelWalkInBookingAsync(Guid walkInBookingId, CancellationToken ct = default)
    {
        var booking = await _bookingRepository.GetByIdAsync(walkInBookingId, ct);
        if (booking == null)
        {
            throw new NotFoundException(ApiErrorMessages.WalkIn.WalkInBookingNotFound(walkInBookingId));
        }

        // Chỉ cho phép hủy khi Status = Active (chưa check-in)
        if (booking.Status != WalkInBookingStatus.Active)
        {
            throw new ConflictException(
                $"Chỉ có thể hủy WalkInBooking ở trạng thái Active. Trạng thái hiện tại: {booking.Status}");
        }

        // Lấy WalkInWindow để trả ghế
        var window = await _windowRepository.GetByIdAsync(booking.WalkInWindowId, ct);
        if (window == null)
        {
            _logger.LogWarning(
                "CancelWalkInBookingAsync: Window {WindowId} not found, cannot release seats",
                booking.WalkInWindowId);
        }
        else
        {
            // Trả ghế về WalkInWindow (tăng AvailableSeats) với OCC check
            var released = await _windowRepository.TryReleaseSeatsAsync(
                booking.WalkInWindowId, booking.Seats, window.Version, ct);
            if (released)
            {
                _logger.LogInformation(
                    "CancelWalkInBookingAsync: Released {Seats} seats back to Window {WindowId}",
                    booking.Seats, booking.WalkInWindowId);
            }
            else
            {
                _logger.LogWarning(
                    "CancelWalkInBookingAsync: Failed to release seats (version conflict) for Window {WindowId}",
                    booking.WalkInWindowId);
            }
        }

        // Cập nhật status WalkInBooking -> Cancelled
        booking.Status = WalkInBookingStatus.Cancelled;
        await _bookingRepository.UpdateAsync(booking, ct);

        _logger.LogInformation(
            "WalkInBooking {Id} cancelled. Seats {Seats} returned to window.",
            walkInBookingId, booking.Seats);
    }

    private static WalkInBookingResponseDto MapToResponse(WalkInBooking booking)
    {
        return new WalkInBookingResponseDto
        {
            Id = booking.Id,
            WalkInWindowId = booking.WalkInWindowId,
            GuestName = booking.GuestName,
            GuestPhone = booking.GuestPhone,
            StartTime = booking.StartTime,
            EndTime = booking.EndTime,
            Seats = booking.Seats,
            HourlyRate = booking.HourlyRate,
            TotalAmount = booking.TotalAmount,
            PaymentStatus = booking.PaymentStatus.ToString(),
            Status = booking.Status.ToString(),
            CreatedAt = booking.CreatedAt
        };
    }
}
