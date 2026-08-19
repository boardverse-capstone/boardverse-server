using BoardVerse.Core.DTOs.Booking;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services;

/// <summary>
/// Triển khai các API booking cho mobile Player (booking-payment-gaps.md #1, #2).
/// Read-only: không mutate state. Chỉ query Booking + ActiveSession để tính capacity.
/// </summary>
public class CafeBookingService : ICafeBookingService
{
    private readonly ICafeRepository _cafeRepository;
    private readonly ICafeTableRepository _cafeTableRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly IActiveSessionRepository _activeSessionRepository;
    private readonly ICafePosRepository _posRepository;

    public CafeBookingService(
        ICafeRepository cafeRepository,
        ICafeTableRepository cafeTableRepository,
        IBookingRepository bookingRepository,
        IActiveSessionRepository activeSessionRepository,
        ICafePosRepository posRepository)
    {
        _cafeRepository = cafeRepository;
        _cafeTableRepository = cafeTableRepository;
        _bookingRepository = bookingRepository;
        _activeSessionRepository = activeSessionRepository;
        _posRepository = posRepository;
    }

    public async Task<IReadOnlyList<AvailableCafeTableDto>> GetAvailableTablesAsync(
        Guid cafeId,
        DateTime scheduledStartTime,
        DateTime scheduleEndTime,
        int seatCount)
    {
        if (scheduleEndTime <= scheduledStartTime)
        {
            throw new BadRequestException(ApiErrorMessages.Booking.InvalidTimeRange);
        }

        if (seatCount < 1)
        {
            throw new BadRequestException(ApiErrorMessages.Booking.SeatCountMustBePositive);
        }

        var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        var allTables = await _cafeTableRepository.GetByCafeIdAsync(cafeId);
        // Filter ra bàn inactive.
        var activeTables = allTables.Where(t => t.IsActive).ToList();
        var overlappingBookings = await _bookingRepository.GetOverlappingBookingsAsync(
            cafeId, scheduledStartTime, scheduleEndTime);

        // Bàn đã có booking overlap.
        var occupiedTableIds = overlappingBookings
            .Select(b => b.CafeTableId)
            .ToHashSet();

        // Bàn có ActiveSession đang active/checking (chưa Paid).
        var activeSessions = await _activeSessionRepository.GetActiveSessionsAsync(cafeId, null);
        var inUseTableIds = activeSessions
            .Where(s => s.CafeTableId.HasValue)
            .Select(s => s.CafeTableId!.Value)
            .ToHashSet();

        var result = activeTables
            .Where(t => t.SeatCount >= seatCount
                && t.Status == CafeTableStatus.Available
                && !occupiedTableIds.Contains(t.Id)
                && !inUseTableIds.Contains(t.Id))
            .OrderBy(t => t.SeatCount)
            .ThenBy(t => t.SortOrder)
            .Select(t => new AvailableCafeTableDto
            {
                Id = t.Id,
                Name = t.Name,
                SeatCount = t.SeatCount,
                IsAvailable = true,
                PricePerHour = cafe.BasePrice
            })
            .ToList();

        return result;
    }

    public async Task<CafeAvailabilityDto> GetAvailabilityAsync(
        Guid cafeId,
        DateTime startTime,
        DateTime endTime,
        int seatCount,
        Guid? gameTemplateId)
    {
        if (endTime <= startTime)
        {
            throw new BadRequestException(ApiErrorMessages.Booking.InvalidTimeRange);
        }

        if (seatCount < 1)
        {
            seatCount = 1;
        }

        var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId)
            ?? throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        // TotalSeats: tổng SeatCount của các bàn active.
        var tables = (await _cafeTableRepository.GetByCafeIdAsync(cafeId))
            .Where(t => t.IsActive)
            .ToList();
        var totalSeats = tables.Sum(t => t.SeatCount);

        // Overlap từ Booking (Confirmed/CheckedIn chưa cancel) + ActiveSession (Active/Checking).
        var overlappingBookings = await _bookingRepository.GetOverlappingBookingsAsync(
            cafeId, startTime, endTime);
        var activeSessions = await _activeSessionRepository.GetActiveSessionsAsync(cafeId, null);

        var bookedSeats = overlappingBookings.Sum(b => b.PlayerQuantity);
        var sessionSeats = activeSessions
            .Where(s => s.CafeTableId.HasValue)
            .Sum(s => tables.FirstOrDefault(t => t.Id == s.CafeTableId)?.SeatCount ?? 0);

        var availableSeats = Math.Max(0, totalSeats - bookedSeats - sessionSeats);
        var hasCapacity = availableSeats >= seatCount;

        // Game box count nếu có filter gameTemplateId.
        int availableGameBoxCount = 0;
        NearbyCafeGameAvailabilityStatus? gameStatus = null;
        if (gameTemplateId.HasValue)
        {
            var boxes = await _posRepository.GetBoxesAsync(cafeId, gameTemplateId);
            availableGameBoxCount = boxes.Count;
            gameStatus = availableGameBoxCount > 0
                ? NearbyCafeGameAvailabilityStatus.GameAvailable
                : NearbyCafeGameAvailabilityStatus.WaitingForGame;
        }

        // Alternative slots: khảo sát 4 slot kế tiếp (mỗi slot cách 30 phút).
        var altSlots = new List<CafeAvailabilitySlotDto>();
        var duration = endTime - startTime;
        for (int i = 1; i <= 4 && altSlots.Count < 2; i++)
        {
            var altStart = startTime.AddMinutes(30 * i);
            var altEnd = altStart.Add(duration);
            var altBookings = await _bookingRepository.GetOverlappingBookingsAsync(cafeId, altStart, altEnd);
            var altSessions = await _activeSessionRepository.GetActiveSessionsAsync(cafeId, null);
            var altBookedSeats = altBookings.Sum(b => b.PlayerQuantity);
            var altSessionSeats = altSessions
                .Where(s => s.CafeTableId.HasValue)
                .Sum(s => tables.FirstOrDefault(t => t.Id == s.CafeTableId)?.SeatCount ?? 0);
            var altAvailable = Math.Max(0, totalSeats - altBookedSeats - altSessionSeats);
            if (altAvailable >= seatCount)
            {
                altSlots.Add(new CafeAvailabilitySlotDto
                {
                    StartTime = altStart,
                    EndTime = altEnd,
                    AvailableSeats = altAvailable
                });
            }
        }

        return new CafeAvailabilityDto
        {
            CafeId = cafeId,
            CafeName = cafe.Name,
            RequestedStartTime = startTime,
            RequestedEndTime = endTime,
            HasCapacity = hasCapacity,
            AvailableSeats = availableSeats,
            TotalSeats = totalSeats,
            AvailableGameBoxCount = availableGameBoxCount,
            SelectedGameAvailabilityStatus = gameStatus,
            AlternativeSlots = altSlots
        };
    }
}