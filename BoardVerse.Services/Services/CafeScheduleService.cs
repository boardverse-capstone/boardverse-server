using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.CafeSchedule;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

/// <summary>
/// Triển khai <see cref="ICafeScheduleService"/> — quản lý <c>CafeScheduleOverride</c>.
/// </summary>
public class CafeScheduleService : ICafeScheduleService
{
    private readonly ICafeScheduleOverrideRepository _overrideRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly ILogger<CafeScheduleService> _logger;

    public CafeScheduleService(
        ICafeScheduleOverrideRepository overrideRepository,
        ICafeRepository cafeRepository,
        ILogger<CafeScheduleService> logger)
    {
        _overrideRepository = overrideRepository;
        _cafeRepository = cafeRepository;
        _logger = logger;
    }

    public async Task<CafeScheduleResponseDto> GetScheduleAsync(Guid cafeId)
    {
        var cafe = await _cafeRepository.GetByIdAsync(cafeId);
        if (cafe == null)
        {
            throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
        }

        var overrides = await _overrideRepository.ListByCafeAsync(cafeId);
        var overrideBySlot = overrides.ToDictionary(o => o.TimeSlot, o => o);

        var slots = Enum.GetValues<TimeSlot>()
            .Select(slot =>
            {
                if (overrideBySlot.TryGetValue(slot, out var ov))
                {
                    return MapOverride(ov, cafeId);
                }

                var defaultStart = CafeSchedule.GetStartTime(slot);
                var defaultEnd = CafeSchedule.GetEndTime(slot);
                var cafeUpdatedAt = cafe.UpdatedAt ?? cafe.CreatedAt;
                return new CafeScheduleOverrideResponseDto
                {
                    Id = Guid.Empty,
                    CafeId = cafeId,
                    TimeSlot = slot,
                    StartTime = defaultStart,
                    EndTime = defaultEnd,
                    IsClosed = false,
                    HasOverride = false,
                    EffectiveFrom = null,
                    EffectiveTo = null,
                    CreatedAt = cafe.CreatedAt,
                    UpdatedAt = cafeUpdatedAt
                };
            })
            .ToList();

        return new CafeScheduleResponseDto
        {
            CafeId = cafeId,
            Slots = slots
        };
    }

    public async Task<CafeScheduleOverrideResponseDto> UpsertOverrideAsync(
        Guid cafeId, Guid managerUserId, UpsertCafeScheduleOverrideRequestDto request)
    {
        await EnsureCafeExistsAsync(cafeId);
        await EnsureCafeManagerAsync(cafeId, managerUserId);

        ValidateRequest(request);

        var existing = await _overrideRepository.GetActiveAsync(
            cafeId, request.TimeSlot, DateOnly.FromDateTime(DateTime.UtcNow));

        if (existing == null)
        {
            existing = new CafeScheduleOverride
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                TimeSlot = request.TimeSlot,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                IsClosed = request.IsClosed,
                EffectiveFrom = request.EffectiveFrom,
                EffectiveTo = request.EffectiveTo
            };
            await _overrideRepository.AddAsync(existing);
        }
        else
        {
            existing.StartTime = request.StartTime;
            existing.EndTime = request.EndTime;
            existing.IsClosed = request.IsClosed;
            existing.EffectiveFrom = request.EffectiveFrom;
            existing.EffectiveTo = request.EffectiveTo;
            await _overrideRepository.UpdateAsync(existing);
        }

        await _overrideRepository.SaveChangesAsync();

        _logger.LogInformation(
            "CafeScheduleOverride upsert: Cafe={CafeId}, Slot={Slot}, IsClosed={IsClosed}",
            cafeId, request.TimeSlot, request.IsClosed);

        return MapOverride(existing, cafeId);
    }

    public async Task DeleteOverrideAsync(Guid cafeId, Guid managerUserId, TimeSlot slot)
    {
        await EnsureCafeExistsAsync(cafeId);
        await EnsureCafeManagerAsync(cafeId, managerUserId);

        await _overrideRepository.DeleteAsync(cafeId, slot);
        await _overrideRepository.SaveChangesAsync();

        _logger.LogInformation("CafeScheduleOverride deleted: Cafe={CafeId}, Slot={Slot}", cafeId, slot);
    }

    // ===== Helpers =====

    private async Task EnsureCafeExistsAsync(Guid cafeId)
    {
        var cafe = await _cafeRepository.GetByIdAsync(cafeId);
        if (cafe == null)
        {
            throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
        }
    }

    private async Task EnsureCafeManagerAsync(Guid cafeId, Guid managerUserId)
    {
        var managedCafes = await _cafeRepository.GetCafesByManagerIdAsync(managerUserId);
        if (!managedCafes.Any(c => c.Id == cafeId))
        {
            throw new ForbiddenException(ApiErrorMessages.Reservation.NoManagerForCafe(cafeId));
        }
    }

    private static void ValidateRequest(UpsertCafeScheduleOverrideRequestDto request)
    {
        if (request.IsClosed)
        {
            // Khi đóng slot, không validate range.
            return;
        }

        // Khi mở slot, nếu có StartTime + EndTime thì phải khác nhau.
        if (request.StartTime.HasValue && request.EndTime.HasValue && request.StartTime == request.EndTime)
        {
            throw new BadRequestException(ApiErrorMessages.Reservation.CafeScheduleOverlapInvalid);
        }

        if (request.EffectiveFrom.HasValue && request.EffectiveTo.HasValue
            && request.EffectiveFrom > request.EffectiveTo)
        {
            throw new BadRequestException(
                ApiErrorMessages.System.CafeScheduleEffectiveRangeInvalid(
                    request.EffectiveFrom.Value, request.EffectiveTo.Value));
        }
    }

    private static CafeScheduleOverrideResponseDto MapOverride(CafeScheduleOverride ov, Guid cafeId)
    {
        var start = ov.StartTime ?? CafeSchedule.GetStartTime(ov.TimeSlot);
        var end = ov.EndTime ?? CafeSchedule.GetEndTime(ov.TimeSlot);

        return new CafeScheduleOverrideResponseDto
        {
            Id = ov.Id,
            CafeId = cafeId,
            TimeSlot = ov.TimeSlot,
            StartTime = start,
            EndTime = end,
            IsClosed = ov.IsClosed,
            HasOverride = true,
            EffectiveFrom = ov.EffectiveFrom,
            EffectiveTo = ov.EffectiveTo,
            CreatedAt = ov.CreatedAt,
            UpdatedAt = ov.UpdatedAt
        };
    }
}
