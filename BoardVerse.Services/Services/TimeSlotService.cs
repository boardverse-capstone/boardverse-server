using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.TimeSlotOverride;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

/// <summary>
/// Triá»ƒn khai <see cref="ITimeSlotService"/> â€” quáº£n lÃ½ 4 TimeSlot máº·c Ä‘á»‹nh vÃ  override theo cafe.
/// </summary>
public class TimeSlotService : ITimeSlotService
{
    private readonly ICafeScheduleOverrideRepository _overrideRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly ILogger<TimeSlotService> _logger;

    public TimeSlotService(
        ICafeScheduleOverrideRepository overrideRepository,
        ICafeRepository cafeRepository,
        ILogger<TimeSlotService> logger)
    {
        _overrideRepository = overrideRepository;
        _cafeRepository = cafeRepository;
        _logger = logger;
    }

    public Task<IReadOnlyList<DefaultTimeSlotDto>> GetDefaultTimeSlotsAsync()
    {
        var slots = new List<DefaultTimeSlotDto>
        {
            new()
            {
                Slot = nameof(TimeSlot.Morning),
                DisplayName = "SÃ¡ng",
                DefaultStartTime = CafeSchedule.GetStartTime(TimeSlot.Morning),
                DefaultEndTime = CafeSchedule.GetEndTime(TimeSlot.Morning),
                DurationMinutes = CafeSchedule.GetDurationMinutes(TimeSlot.Morning),
                Description = "PhiÃªn sÃ¡ng (06:00 â€“ 12:00)"
            },
            new()
            {
                Slot = nameof(TimeSlot.Afternoon),
                DisplayName = "Chiá»u",
                DefaultStartTime = CafeSchedule.GetStartTime(TimeSlot.Afternoon),
                DefaultEndTime = CafeSchedule.GetEndTime(TimeSlot.Afternoon),
                DurationMinutes = CafeSchedule.GetDurationMinutes(TimeSlot.Afternoon),
                Description = "PhiÃªn chiá»u (12:00 â€“ 17:00)"
            },
            new()
            {
                Slot = nameof(TimeSlot.Evening),
                DisplayName = "Tá»‘i",
                DefaultStartTime = CafeSchedule.GetStartTime(TimeSlot.Evening),
                DefaultEndTime = CafeSchedule.GetEndTime(TimeSlot.Evening),
                DurationMinutes = CafeSchedule.GetDurationMinutes(TimeSlot.Evening),
                Description = "PhiÃªn tá»‘i (17:00 â€“ 23:00)"
            },
            new()
            {
                Slot = nameof(TimeSlot.LateNight),
                DisplayName = "Khuya",
                DefaultStartTime = CafeSchedule.GetStartTime(TimeSlot.LateNight),
                DefaultEndTime = CafeSchedule.GetEndTime(TimeSlot.LateNight),
                DurationMinutes = CafeSchedule.GetDurationMinutes(TimeSlot.LateNight),
                Description = "PhiÃªn khuya qua Ä‘Ãªm (23:00 â€“ 06:00 hÃ´m sau)"
            }
        };

        return Task.FromResult<IReadOnlyList<DefaultTimeSlotDto>>(slots);
    }

    public async Task<IReadOnlyList<ManagerTimeSlotResponseDto>> GetCafeTimeSlotsAsync(
        Guid cafeId, Guid managerUserId)
    {
        await EnsureCafeManagerAsync(cafeId, managerUserId);

        var overrides = await _overrideRepository.ListByCafeAsync(cafeId);
        var overrideBySlot = overrides.ToDictionary(o => o.TimeSlot, o => o);

        var slots = Enum.GetValues<TimeSlot>()
            .Select(slot => BuildResponse(slot, cafeId, overrideBySlot))
            .ToList();

        return slots;
    }

    public async Task<ManagerTimeSlotResponseDto> GetCafeTimeSlotAsync(
        Guid cafeId, Guid managerUserId, string slotName)
    {
        await EnsureCafeManagerAsync(cafeId, managerUserId);

        var slot = ParseTimeSlot(slotName);
        var overrideEntry = await _overrideRepository.GetByCafeAndSlotAsync(cafeId, slot);

        return BuildResponse(slot, cafeId,
            overrideEntry is null
                ? new Dictionary<TimeSlot, CafeScheduleOverride>()
                : new Dictionary<TimeSlot, CafeScheduleOverride> { [slot] = overrideEntry });
    }

    public async Task<ManagerTimeSlotResponseDto> CreateOverrideAsync(
        Guid cafeId, Guid managerUserId, CreateTimeSlotOverrideRequestDto request)
    {
        await EnsureCafeManagerAsync(cafeId, managerUserId);

        var slot = ParseTimeSlot(request.TimeSlot);
        ValidateOverrideTimeRange(slot, request.StartTime, request.EndTime, request.IsClosed);
        ValidateEffectiveRange(request.EffectiveFrom, request.EffectiveTo);

        var existing = await _overrideRepository.GetByCafeAndSlotAsync(cafeId, slot);
        if (existing != null)
        {
            throw new ConflictException(ApiErrorMessages.System.TimeSlotOverrideOverrideAlreadyExists(cafeId, request.TimeSlot));
        }

        var entity = new CafeScheduleOverride
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            TimeSlot = slot,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            IsClosed = request.IsClosed,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _overrideRepository.AddAsync(entity);
        await _overrideRepository.SaveChangesAsync();

        _logger.LogInformation(
            "TimeSlot override created: Cafe={CafeId}, Slot={Slot}, IsClosed={IsClosed}",
            cafeId, slot, request.IsClosed);

        return MapOverride(entity, cafeId);
    }

    public async Task<ManagerTimeSlotResponseDto> UpdateOverrideAsync(
        Guid cafeId, Guid managerUserId, string slotName, UpdateTimeSlotOverrideRequestDto request)
    {
        await EnsureCafeManagerAsync(cafeId, managerUserId);

        var slot = ParseTimeSlot(slotName);

        var existing = await _overrideRepository.GetByCafeAndSlotAsync(cafeId, slot);
        if (existing == null)
        {
            throw new NotFoundException(ApiErrorMessages.System.TimeSlotOverrideOverrideNotFound(cafeId, slotName));
        }

        // Náº¿u táº¥t cáº£ field request Ä‘á»u null â†’ khÃ´ng cÃ³ gÃ¬ Ä‘á»ƒ update.
        if (request.StartTime == null && request.EndTime == null && request.IsClosed == null
            && request.EffectiveFrom == null && request.EffectiveTo == null)
        {
            throw new BadRequestException(ApiErrorMessages.System.TimeSlotOverrideNoFieldsToUpdate);
        }

        // Snapshot giÃ¡ trá»‹ hiá»‡n táº¡i trÆ°á»›c khi Ã¡p partial update.
        var newStart = request.StartTime ?? existing.StartTime;
        var newEnd = request.EndTime ?? existing.EndTime;
        var newIsClosed = request.IsClosed ?? existing.IsClosed;
        var newEffectiveFrom = request.EffectiveFrom ?? existing.EffectiveFrom;
        var newEffectiveTo = request.EffectiveTo ?? existing.EffectiveTo;

        // Validate giÃ¡ trá»‹ má»›i (sau khi merge partial).
        ValidateOverrideTimeRange(slot, newStart, newEnd, newIsClosed);
        ValidateEffectiveRange(newEffectiveFrom, newEffectiveTo);

        existing.StartTime = newStart;
        existing.EndTime = newEnd;
        existing.IsClosed = newIsClosed;
        existing.EffectiveFrom = newEffectiveFrom;
        existing.EffectiveTo = newEffectiveTo;

        await _overrideRepository.UpdateAsync(existing);
        await _overrideRepository.SaveChangesAsync();

        _logger.LogInformation(
            "TimeSlot override updated: Cafe={CafeId}, Slot={Slot}", cafeId, slot);

        return MapOverride(existing, cafeId);
    }

    public async Task DeleteOverrideAsync(
        Guid cafeId, Guid managerUserId, string slotName)
    {
        await EnsureCafeManagerAsync(cafeId, managerUserId);

        var slot = ParseTimeSlot(slotName);
        await _overrideRepository.DeleteAsync(cafeId, slot);
        await _overrideRepository.SaveChangesAsync();

        _logger.LogInformation(
            "TimeSlot override deleted: Cafe={CafeId}, Slot={Slot}", cafeId, slot);
    }

    // ===== Helpers =====

    private async Task EnsureCafeManagerAsync(Guid cafeId, Guid managerUserId)
    {
        var cafe = await _cafeRepository.GetByIdAsync(cafeId);
        if (cafe == null)
        {
            throw new NotFoundException(ApiErrorMessages.System.TimeSlotOverrideNotFoundForCafe(cafeId));
        }

        var managedCafes = await _cafeRepository.GetCafesByManagerIdAsync(managerUserId);
        if (!managedCafes.Any(c => c.Id == cafeId))
        {
            throw new ForbiddenException(ApiErrorMessages.System.TimeSlotOverrideNotManagerForCafe(cafeId));
        }
    }

    private static TimeSlot ParseTimeSlot(string? slotName)
    {
        if (string.IsNullOrWhiteSpace(slotName))
        {
            throw new BadRequestException(ApiErrorMessages.Validation.TimeSlotRequired);
        }

        if (!Enum.TryParse<TimeSlot>(slotName, ignoreCase: true, out var slot)
            || !Enum.IsDefined(typeof(TimeSlot), slot))
        {
            throw new BadRequestException(
                ApiErrorMessages.Validation.TimeSlotInvalid("Morning, Afternoon, Evening, LateNight"));
        }

        return slot;
    }

    private static void ValidateOverrideTimeRange(
        TimeSlot slot,
        TimeOnly? startTime,
        TimeOnly? endTime,
        bool isClosed)
    {
        if (isClosed)
        {
            return;
        }

        if (startTime.HasValue && endTime.HasValue && startTime.Value == endTime.Value)
        {
            throw new BadRequestException(ApiErrorMessages.System.TimeSlotOverrideInvalidTimeRange);
        }
    }

    private static void ValidateEffectiveRange(DateOnly? from, DateOnly? to)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            throw new BadRequestException(ApiErrorMessages.System.TimeSlotOverrideInvalidEffectiveRange);
        }
    }

    private ManagerTimeSlotResponseDto BuildResponse(
        TimeSlot slot,
        Guid cafeId,
        IReadOnlyDictionary<TimeSlot, CafeScheduleOverride> overrideBySlot)
    {
        var defaultStart = CafeSchedule.GetStartTime(slot);
        var defaultEnd = CafeSchedule.GetEndTime(slot);

        if (overrideBySlot.TryGetValue(slot, out var ov))
        {
            var start = ov.StartTime ?? defaultStart;
            var end = ov.EndTime ?? defaultEnd;

            return new ManagerTimeSlotResponseDto
            {
                Id = ov.Id,
                CafeId = cafeId,
                TimeSlot = slot.ToString(),
                StartTime = start,
                EndTime = end,
                DefaultStartTime = defaultStart,
                DefaultEndTime = defaultEnd,
                IsClosed = ov.IsClosed,
                HasOverride = true,
                EffectiveFrom = ov.EffectiveFrom,
                EffectiveTo = ov.EffectiveTo,
                CreatedAt = ov.CreatedAt,
                UpdatedAt = ov.UpdatedAt
            };
        }

        return new ManagerTimeSlotResponseDto
        {
            Id = Guid.Empty,
            CafeId = cafeId,
            TimeSlot = slot.ToString(),
            StartTime = defaultStart,
            EndTime = defaultEnd,
            DefaultStartTime = defaultStart,
            DefaultEndTime = defaultEnd,
            IsClosed = false,
            HasOverride = false,
            EffectiveFrom = null,
            EffectiveTo = null,
            CreatedAt = null,
            UpdatedAt = null
        };
    }

    private static ManagerTimeSlotResponseDto MapOverride(CafeScheduleOverride ov, Guid cafeId)
    {
        var defaultStart = CafeSchedule.GetStartTime(ov.TimeSlot);
        var defaultEnd = CafeSchedule.GetEndTime(ov.TimeSlot);
        var start = ov.StartTime ?? defaultStart;
        var end = ov.EndTime ?? defaultEnd;

        return new ManagerTimeSlotResponseDto
        {
            Id = ov.Id,
            CafeId = cafeId,
            TimeSlot = ov.TimeSlot.ToString(),
            StartTime = start,
            EndTime = end,
            DefaultStartTime = defaultStart,
            DefaultEndTime = defaultEnd,
            IsClosed = ov.IsClosed,
            HasOverride = true,
            EffectiveFrom = ov.EffectiveFrom,
            EffectiveTo = ov.EffectiveTo,
            CreatedAt = ov.CreatedAt,
            UpdatedAt = ov.UpdatedAt
        };
    }
}

