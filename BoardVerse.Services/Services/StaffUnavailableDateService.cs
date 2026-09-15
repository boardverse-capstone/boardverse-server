using BoardVerse.Core.DTOs.StaffSchedule;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services;

/// <summary>
/// StaffUnavailableDateService — quản lý ngày nghỉ cố định của staff.
/// </summary>
public class StaffUnavailableDateService : IStaffUnavailableDateService
{
    private readonly IStaffUnavailableDateRepository _unavailableRepository;
    private readonly ICafeRepository _cafeRepository;

    public StaffUnavailableDateService(
        IStaffUnavailableDateRepository unavailableRepository,
        ICafeRepository cafeRepository)
    {
        _unavailableRepository = unavailableRepository;
        _cafeRepository = cafeRepository;
    }

    public async Task<UnavailableDateResponseDto> CreateAsync(Guid cafeId, Guid staffUserId, CreateUnavailableDateDto dto, CancellationToken cancellationToken = default)
    {
        // GAP-VALIDATION-05 fix: validate cafe tồn tại TRƯỚC khi xử lý.
        var cafe = await _cafeRepository.GetByIdAsync(cafeId, cancellationToken);
        if (cafe == null)
            throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        var isStaff = await _cafeRepository.IsStaffMemberExistsAsync(cafeId, staffUserId, cancellationToken);
        if (!isStaff && cafe.ManagerId != staffUserId)
            throw new BadRequestException(ApiErrorMessages.StaffSchedule.NotCafeStaff);

        var exists = await _unavailableRepository.ExistsAsync(staffUserId, dto.Date, cancellationToken);
        if (exists)
            throw new ConflictException(ApiErrorMessages.StaffSchedule.UnavailableDateExists);

        var item = new StaffUnavailableDate
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            StaffUserId = staffUserId,
            Date = dto.Date,
            Reason = dto.Reason,
            CreatedAt = DateTime.UtcNow
        };

        await _unavailableRepository.AddAsync(item, cancellationToken);
        await _unavailableRepository.SaveChangesAsync(cancellationToken);

        return MapToDto(item);
    }

    public async Task DeleteAsync(Guid id, Guid staffUserId, CancellationToken cancellationToken = default)
    {
        var item = await _unavailableRepository.GetByIdAsync(id, cancellationToken);
        if (item == null)
            throw new NotFoundException(ApiErrorMessages.StaffSchedule.UnavailableDateNotFound(id));

        if (item.StaffUserId != staffUserId)
            throw new ForbiddenException(ApiErrorMessages.StaffSchedule.UnavailableDateNotOwnedByCaller);

        await _unavailableRepository.DeleteAsync(id, cancellationToken);
        await _unavailableRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UnavailableDateResponseDto>> GetMyUnavailableDatesAsync(Guid staffUserId, Guid cafeId, DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default)
    {
        var list = await _unavailableRepository.GetByStaffAsync(staffUserId, cafeId, from, to, cancellationToken);
        return list.Select(MapToDto).ToList();
    }

    public Task<bool> IsUnavailableAsync(Guid staffUserId, DateOnly date, CancellationToken cancellationToken = default)
    {
        return _unavailableRepository.IsUnavailableAsync(staffUserId, date, cancellationToken);
    }

    private static UnavailableDateResponseDto MapToDto(StaffUnavailableDate u) => new()
    {
        Id = u.Id,
        StaffUserId = u.StaffUserId,
        CafeId = u.CafeId,
        Date = u.Date,
        Reason = u.Reason,
        CreatedAt = u.CreatedAt
    };
}