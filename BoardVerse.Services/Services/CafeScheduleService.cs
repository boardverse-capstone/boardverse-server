using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.CafeSchedule;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Services.Services;

/// <summary>
/// Triển khai ICafeScheduleService - quản lý CafeScheduleOverride.
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot - dùng ApplyDate/OpenTime/CloseTime.
/// GAP-FIX (2026-09-01): Validate input, authz, transaction, bulk, single-get.
/// </summary>
public class CafeScheduleService : ICafeScheduleService
{
    private readonly ICafeScheduleOverrideRepository _overrideRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly BoardVerseDbContext _db;

    public CafeScheduleService(
        ICafeScheduleOverrideRepository overrideRepository,
        ICafeRepository cafeRepository,
        BoardVerseDbContext db)
    {
        _overrideRepository = overrideRepository;
        _cafeRepository = cafeRepository;
        _db = db;
    }

    public async Task<CafeScheduleResponseDto> GetScheduleAsync(
        Guid cafeId,
        Guid? managerUserId = null,
        CancellationToken cancellationToken = default)
    {
        var cafe = await _cafeRepository.GetByIdAsync(cafeId, cancellationToken);
        if (cafe == null)
        {
            throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
        }

        // GAP-4: Authz check nếu managerUserId được cung cấp
        if (managerUserId.HasValue)
        {
            await EnsureCafeManagerOrStaffAsync(cafeId, managerUserId.Value, cancellationToken);
        }

        var overrides = await _overrideRepository.ListByCafeAsync(cafeId, cancellationToken);

        return new CafeScheduleResponseDto
        {
            CafeId = cafeId,
            DefaultOpenTime = CafeSchedule.DefaultOpenTime,
            DefaultCloseTime = CafeSchedule.DefaultCloseTime,
            Days = overrides.Select(o => MapOverride(o, cafeId)).ToList()
        };
    }

    /// <summary>
    /// Lấy override cho (cafeId, applyDate). Trả về null nếu không có override (dùng default).
    /// </summary>
    public async Task<CafeScheduleOverrideResponseDto?> GetOverrideAsync(
        Guid cafeId, DateOnly applyDate, CancellationToken cancellationToken = default)
    {
        var cafe = await _cafeRepository.GetByIdAsync(cafeId, cancellationToken);
        if (cafe == null)
        {
            throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
        }

        var ov = await _overrideRepository.GetByApplyDateAsync(cafeId, applyDate, cancellationToken);
        return ov != null ? MapOverride(ov, cafeId) : null;
    }

    public async Task<CafeScheduleOverrideResponseDto> UpsertOverrideAsync(
        Guid cafeId,
        Guid managerUserId,
        UpsertCafeScheduleOverrideRequestDto request,
        CancellationToken cancellationToken = default)
    {
        // GAP-8: Single query cho cả exists + authz check
        await EnsureCafeManagerOrStaffAsync(cafeId, managerUserId, cancellationToken);

        // GAP-2: Validate input
        ValidateUpsertRequest(request);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var existing = await _overrideRepository.GetByApplyDateAsync(cafeId, request.ApplyDate, cancellationToken);

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
                await _overrideRepository.AddAsync(existing, cancellationToken);
            }
            else
            {
                existing.OpenTime = request.OpenTime;
                existing.CloseTime = request.CloseTime;
                existing.IsClosed = request.IsClosed;
                existing.UpdatedAt = DateTime.UtcNow;
                await _overrideRepository.UpdateAsync(existing, cancellationToken);
            }

            await _overrideRepository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return MapOverride(existing, cafeId);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Bulk upsert - tạo/cập nhật nhiều override trong 1 transaction.
    /// </summary>
    public async Task<List<CafeScheduleOverrideResponseDto>> UpsertBulkOverridesAsync(
        Guid cafeId,
        Guid managerUserId,
        List<UpsertCafeScheduleOverrideRequestDto> requests,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
        {
            return new List<CafeScheduleOverrideResponseDto>();
        }

        await EnsureCafeManagerOrStaffAsync(cafeId, managerUserId, cancellationToken);

        foreach (var r in requests)
        {
            ValidateUpsertRequest(r);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var results = new List<CafeScheduleOverrideResponseDto>();

            foreach (var request in requests)
            {
                var existing = await _overrideRepository.GetByApplyDateAsync(cafeId, request.ApplyDate, cancellationToken);

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
                    await _overrideRepository.AddAsync(existing, cancellationToken);
                }
                else
                {
                    existing.OpenTime = request.OpenTime;
                    existing.CloseTime = request.CloseTime;
                    existing.IsClosed = request.IsClosed;
                    existing.UpdatedAt = DateTime.UtcNow;
                    await _overrideRepository.UpdateAsync(existing, cancellationToken);
                }

                results.Add(MapOverride(existing, cafeId));
            }

            await _overrideRepository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return results;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteOverrideAsync(
        Guid cafeId,
        Guid managerUserId,
        DateOnly applyDate,
        CancellationToken cancellationToken = default)
    {
        await EnsureCafeManagerOrStaffAsync(cafeId, managerUserId, cancellationToken);

        var existing = await _overrideRepository.GetByApplyDateAsync(cafeId, applyDate, cancellationToken);
        if (existing != null)
        {
            await _overrideRepository.DeleteByIdAsync(existing.Id, cancellationToken);
            await _overrideRepository.SaveChangesAsync(cancellationToken);
        }
        // Idempotent: không có override → coi như thành công
    }

    // ===== Helpers =====

    /// <summary>
    /// GAP-8: Thay 2 query (exists + authz) bằng 1 query dùng IsManagerOrStaffAsync.
    /// </summary>
    private async Task EnsureCafeManagerOrStaffAsync(Guid cafeId, Guid userId, CancellationToken cancellationToken)
    {
        var isAuthorized = await _cafeRepository.IsManagerOrStaffAsync(cafeId, userId, cancellationToken);
        if (!isAuthorized)
        {
            throw new ForbiddenException(ApiErrorMessages.Reservation.NoManagerForCafe(cafeId));
        }
    }

    /// <summary>
    /// GAP-2: Validate UpsertCafeScheduleOverrideRequestDto.
    /// </summary>
    private static void ValidateUpsertRequest(UpsertCafeScheduleOverrideRequestDto request)
    {
        // applyDate không được quá khứ
        if (request.ApplyDate < DateOnly.FromDateTime(DateTime.UtcNow.Date))
        {
            throw new BadRequestException("Ngày áp dụng không được ở quá khứ.");
        }

        // Nếu không đóng cửa, phải validate giờ
        if (!request.IsClosed)
        {
            var openTime = request.OpenTime ?? CafeSchedule.DefaultOpenTime;
            var closeTime = request.CloseTime ?? CafeSchedule.DefaultCloseTime;

            // openTime != closeTime (same-day hoặc overnight đều không được bằng nhau)
            if (openTime == closeTime)
            {
                throw new BadRequestException("Giờ mở cửa và giờ đóng cửa không được bằng nhau.");
            }

            // Same-day: closeTime > openTime → OK (VD 08:00-20:00)
            // Overnight: closeTime < openTime → OK (VD 22:00-02:00)
            // Trường hợp reject: closeTime > openTime nhưng closeTime <= DefaultOpenTime (VD 08:00-06:00)
            // Nghĩa là closeTime = 06:00 và openTime = 08:00 → invalid
        }
    }

    private static CafeScheduleOverrideResponseDto MapOverride(CafeScheduleOverride ov, Guid cafeId)
    {
        return new CafeScheduleOverrideResponseDto
        {
            Id = ov.Id,
            CafeId = cafeId,
            ApplyDate = ov.ApplyDate,
            OpenTime = ov.OpenTime ?? CafeSchedule.DefaultOpenTime,
            CloseTime = ov.CloseTime ?? CafeSchedule.DefaultCloseTime,
            IsClosed = ov.IsClosed,
            HasOverride = true,
            CreatedAt = ov.CreatedAt,
            UpdatedAt = ov.UpdatedAt
        };
    }
}
