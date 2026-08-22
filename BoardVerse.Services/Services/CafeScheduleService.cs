using BoardVerse.Core.DTOs.CafeSchedule;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services;

/// <summary>
/// Triển khai ICafeScheduleService - quản lý CafeScheduleOverride.
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot - dùng ApplyDate/OpenTime/CloseTime.
/// </summary>
public class CafeScheduleService : ICafeScheduleService
{
    private readonly ICafeScheduleOverrideRepository _overrideRepository;
    private readonly ICafeRepository _cafeRepository;

    public CafeScheduleService(
        ICafeScheduleOverrideRepository overrideRepository,
        ICafeRepository cafeRepository)
    {
        _overrideRepository = overrideRepository;
        _cafeRepository = cafeRepository;
    }

    public async Task<CafeScheduleResponseDto> GetScheduleAsync(Guid cafeId, CancellationToken cancellationToken = default)
    {
        var cafe = await _cafeRepository.GetByIdAsync(cafeId);
        if (cafe == null)
        {
            throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
        }

        var overrides = await _overrideRepository.ListByCafeAsync(cafeId);

        return new CafeScheduleResponseDto
        {
            CafeId = cafeId,
            Days = overrides.Select(o => MapOverride(o, cafeId)).ToList()
        };
    }

    public async Task<CafeScheduleOverrideResponseDto> UpsertOverrideAsync(
        Guid cafeId, Guid managerUserId, UpsertCafeScheduleOverrideRequestDto request, CancellationToken cancellationToken = default)
    {
        await EnsureCafeExistsAsync(cafeId);
        await EnsureCafeManagerAsync(cafeId, managerUserId);

        var existing = await _overrideRepository.GetByApplyDateAsync(cafeId, request.ApplyDate);

        if (existing == null)
        {
            existing = new CafeScheduleOverride
            {
                Id = Guid.NewGuid(),
                CafeId = cafeId,
                ApplyDate = request.ApplyDate,
                OpenTime = request.OpenTime,
                CloseTime = request.CloseTime,
                IsClosed = request.IsClosed
            };
            await _overrideRepository.AddAsync(existing);
        }
        else
        {
            existing.OpenTime = request.OpenTime;
            existing.CloseTime = request.CloseTime;
            existing.IsClosed = request.IsClosed;
            existing.UpdatedAt = DateTime.UtcNow;
            await _overrideRepository.UpdateAsync(existing);
        }

        await _overrideRepository.SaveChangesAsync();

        return MapOverride(existing, cafeId);
    }

    public async Task DeleteOverrideAsync(Guid cafeId, Guid managerUserId, DateOnly applyDate)
    {
        await EnsureCafeExistsAsync(cafeId);
        await EnsureCafeManagerAsync(cafeId, managerUserId);

        var existing = await _overrideRepository.GetByApplyDateAsync(cafeId, applyDate);
        if (existing != null)
        {
            await _overrideRepository.DeleteByIdAsync(existing.Id);
            await _overrideRepository.SaveChangesAsync();
        }
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

    private static CafeScheduleOverrideResponseDto MapOverride(CafeScheduleOverride ov, Guid cafeId)
    {
        return new CafeScheduleOverrideResponseDto
        {
            Id = ov.Id,
            CafeId = cafeId,
            ApplyDate = ov.ApplyDate,
            OpenTime = ov.OpenTime ?? TimeOnly.MinValue,
            CloseTime = ov.CloseTime ?? TimeOnly.MaxValue,
            IsClosed = ov.IsClosed,
            HasOverride = true,
            CreatedAt = ov.CreatedAt,
            UpdatedAt = ov.UpdatedAt
        };
    }
}
