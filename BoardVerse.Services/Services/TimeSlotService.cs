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
/// Triển khai <see cref="ITimeSlotService"/> — quản lý 4 TimeSlot mặc định và override theo cafe.
///
/// BR-NEW-15 (2026-08-18): TimeSlot enum đang trong quá trình loại bỏ.
/// - Phần quản lý override cụ thể (Create/Update/Delete theo slot) sẽ được chuyển sang
///   <c>CafeScheduleService</c> dùng ApplyDate/OpenTime/CloseTime.
/// - Các method quản lý theo slot (TimeSlotController endpoints) tạm thời trả về stub.
/// </summary>
[Obsolete("BR-NEW-15: TimeSlot-based management sẽ được thay bằng ApplyDate/OpenTime/CloseTime trong CafeScheduleService. Xem docs/time-slot-fixed-end-design.md v3.0.")]
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
                DisplayName = "Sáng",
                DefaultStartTime = TimeSlot.Morning.GetStartTime(),
                DefaultEndTime = TimeSlot.Morning.GetEndTime(),
                DurationMinutes = TimeSlot.Morning.GetDurationMinutes(),
                Description = "Phiên sáng (06:00 – 12:00)"
            },
            new()
            {
                Slot = nameof(TimeSlot.Afternoon),
                DisplayName = "Chiều",
                DefaultStartTime = TimeSlot.Afternoon.GetStartTime(),
                DefaultEndTime = TimeSlot.Afternoon.GetEndTime(),
                DurationMinutes = TimeSlot.Afternoon.GetDurationMinutes(),
                Description = "Phiên chiều (12:00 – 17:00)"
            },
            new()
            {
                Slot = nameof(TimeSlot.Evening),
                DisplayName = "Tối",
                DefaultStartTime = TimeSlot.Evening.GetStartTime(),
                DefaultEndTime = TimeSlot.Evening.GetEndTime(),
                DurationMinutes = TimeSlot.Evening.GetDurationMinutes(),
                Description = "Phiên tối (17:00 – 23:00)"
            },
            new()
            {
                Slot = nameof(TimeSlot.LateNight),
                DisplayName = "Khuya",
                DefaultStartTime = TimeSlot.LateNight.GetStartTime(),
                DefaultEndTime = TimeSlot.LateNight.GetEndTime(),
                DurationMinutes = TimeSlot.LateNight.GetDurationMinutes(),
                Description = "Phiên khuya qua đêm (23:00 – 06:00 hôm sau)"
            }
        };

        return Task.FromResult<IReadOnlyList<DefaultTimeSlotDto>>(slots);
    }

    public async Task<IReadOnlyList<ManagerTimeSlotResponseDto>> GetCafeTimeSlotsAsync(
        Guid cafeId, Guid managerUserId)
    {
        await EnsureCafeManagerAsync(cafeId, managerUserId);

        // BR-NEW-15: Override theo TimeSlot slot đang bị loại bỏ.
        // Trả về 4 slot default — override cụ thể xem qua CafeScheduleService.
        var slots = Enum.GetValues<TimeSlot>()
            .Select(slot => BuildDefaultResponse(slot, cafeId))
            .ToList();

        return slots;
    }

    public async Task<ManagerTimeSlotResponseDto> GetCafeTimeSlotAsync(
        Guid cafeId, Guid managerUserId, string slotName)
    {
        await EnsureCafeManagerAsync(cafeId, managerUserId);
        var slot = ParseTimeSlot(slotName);
        return BuildDefaultResponse(slot, cafeId);
    }

    public Task<ManagerTimeSlotResponseDto> CreateOverrideAsync(
        Guid cafeId, Guid managerUserId, CreateTimeSlotOverrideRequestDto request)
    {
        throw new NotImplementedException(
            "BR-NEW-15: CreateOverrideAsync (theo TimeSlot slot) đang bị loại bỏ. " +
            "Dùng CafeScheduleService.CreateOrUpdateOverrideAsync(Guid cafeId, DateOnly applyDate, ...) thay thế.");
    }

    public Task<ManagerTimeSlotResponseDto> UpdateOverrideAsync(
        Guid cafeId, Guid managerUserId, string slotName, UpdateTimeSlotOverrideRequestDto request)
    {
        throw new NotImplementedException(
            "BR-NEW-15: UpdateOverrideAsync (theo TimeSlot slot) đang bị loại bỏ. " +
            "Dùng CafeScheduleService.CreateOrUpdateOverrideAsync(Guid cafeId, DateOnly applyDate, ...) thay thế.");
    }

    public Task DeleteOverrideAsync(Guid cafeId, Guid managerUserId, string slotName)
    {
        throw new NotImplementedException(
            "BR-NEW-15: DeleteOverrideAsync (theo TimeSlot slot) đang bị loại bỏ. " +
            "Dùng CafeScheduleService.DeleteOverrideAsync(Guid cafeId, DateOnly applyDate) thay thế.");
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

    private ManagerTimeSlotResponseDto BuildDefaultResponse(TimeSlot slot, Guid cafeId)
    {
        var defaultStart = slot.GetStartTime();
        var defaultEnd = slot.GetEndTime();

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
}
