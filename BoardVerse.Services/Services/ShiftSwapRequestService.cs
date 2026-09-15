using BoardVerse.Core.DTOs.StaffSchedule;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

/// <summary>
/// ShiftSwapRequestService — quản lý yêu cầu đổi ca giữa 2 staff.
/// Khi Manager duyệt, 2 lịch StaffSchedule được hoán đổi StaffUserId.
/// </summary>
public class ShiftSwapRequestService : IShiftSwapRequestService
{
    private readonly IShiftSwapRequestRepository _swapRepository;
    private readonly IStaffScheduleRepository _scheduleRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly IPushNotificationService _pushNotificationService;
    private readonly ILogger<ShiftSwapRequestService> _logger;

    public ShiftSwapRequestService(
        IShiftSwapRequestRepository swapRepository,
        IStaffScheduleRepository scheduleRepository,
        ICafeRepository cafeRepository,
        IPushNotificationService pushNotificationService,
        ILogger<ShiftSwapRequestService> logger)
    {
        _swapRepository = swapRepository;
        _scheduleRepository = scheduleRepository;
        _cafeRepository = cafeRepository;
        _pushNotificationService = pushNotificationService;
        _logger = logger;
    }

    public async Task<ShiftSwapRequestResponseDto> CreateAsync(Guid cafeId, Guid requesterId, CreateShiftSwapRequestDto dto, CancellationToken cancellationToken = default)
    {
        // GAP-VALIDATION-02 fix: validate cafe tồn tại TRƯỚC khi xử lý các logic khác.
        var cafe = await _cafeRepository.GetByIdAsync(cafeId, cancellationToken);
        if (cafe == null)
            throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        if (dto.RequesterScheduleId == dto.TargetScheduleId)
            throw new BadRequestException(ApiErrorMessages.StaffSchedule.SwapSameSchedule);

        if (requesterId == dto.TargetStaffId)
            throw new BadRequestException(ApiErrorMessages.StaffSchedule.SwapSameStaff);

        var requesterSchedule = await _scheduleRepository.GetByIdAsync(dto.RequesterScheduleId, cancellationToken);
        if (requesterSchedule == null)
            throw new NotFoundException(ApiErrorMessages.StaffSchedule.ScheduleNotFoundById(dto.RequesterScheduleId));

        // GAP-VALIDATION-02 fix: schedule phải thuộc cafeId trong URL — staff A không thể đổi ca với schedule ở cafe khác.
        if (requesterSchedule.CafeId != cafeId)
            throw new NotFoundException(ApiErrorMessages.StaffSchedule.ScheduleNotFoundById(dto.RequesterScheduleId));

        if (requesterSchedule.StaffUserId != requesterId)
            throw new BadRequestException(ApiErrorMessages.StaffSchedule.SwapSchedulesBelongDifferentStaff);

        var targetSchedule = await _scheduleRepository.GetByIdAsync(dto.TargetScheduleId, cancellationToken);
        if (targetSchedule == null)
            throw new NotFoundException(ApiErrorMessages.StaffSchedule.ScheduleNotFoundById(dto.TargetScheduleId));

        // GAP-VALIDATION-02 fix: target schedule cũng phải thuộc cùng cafe.
        if (targetSchedule.CafeId != cafeId)
            throw new NotFoundException(ApiErrorMessages.StaffSchedule.ScheduleNotFoundById(dto.TargetScheduleId));

        if (targetSchedule.StaffUserId != dto.TargetStaffId)
            throw new BadRequestException(ApiErrorMessages.StaffSchedule.SwapSchedulesBelongDifferentStaff);

        // GAP-VALIDATION-03 fix: target staff phải là nhân viên thực sự của cafe (không phải GUID random).
        var isTargetStaff = await _cafeRepository.IsStaffMemberExistsAsync(cafeId, dto.TargetStaffId, cancellationToken);
        if (!isTargetStaff && cafe.ManagerId != dto.TargetStaffId)
            throw new BadRequestException(ApiErrorMessages.StaffSchedule.NotCafeStaff);

        var request = new ShiftSwapRequest
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            RequesterId = requesterId,
            TargetStaffId = dto.TargetStaffId,
            RequesterScheduleId = dto.RequesterScheduleId,
            TargetScheduleId = dto.TargetScheduleId,
            Status = ShiftSwapStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await _swapRepository.AddAsync(request, cancellationToken);
        await _swapRepository.SaveChangesAsync(cancellationToken);

        await NotifyTargetStaffAsync(requesterSchedule.User?.Username ?? "Staff", dto.TargetStaffId, cancellationToken);

        var created = await _swapRepository.GetByIdAsync(request.Id, cancellationToken);
        return await MapToDtoAsync(created!, cancellationToken);
    }

    public async Task<ShiftSwapRequestResponseDto> ReviewAsync(Guid requestId, Guid managerUserId, ReviewShiftSwapRequestDto dto, CancellationToken cancellationToken = default)
    {
        var request = await _swapRepository.GetByIdAsync(requestId, cancellationToken);
        if (request == null)
            throw new NotFoundException(ApiErrorMessages.StaffSchedule.SwapNotFound);

        if (request.Status != ShiftSwapStatus.Pending)
            throw new ConflictException(ApiErrorMessages.StaffSchedule.SwapAlreadyReviewed);

        request.Status = dto.Status;
        request.ReviewedByUserId = managerUserId;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewNote = dto.ReviewNote;

        if (dto.Status == ShiftSwapStatus.Approved)
        {
            // Hoán đổi StaffUserId giữa 2 lịch
            var requesterSchedule = await _scheduleRepository.GetByIdAsync(request.RequesterScheduleId, cancellationToken);
            var targetSchedule = await _scheduleRepository.GetByIdAsync(request.TargetScheduleId, cancellationToken);

            if (requesterSchedule == null || targetSchedule == null)
                throw new NotFoundException(ApiErrorMessages.StaffSchedule.ScheduleNotFound);

            var tempUserId = requesterSchedule.StaffUserId;
            requesterSchedule.StaffUserId = targetSchedule.StaffUserId;
            targetSchedule.StaffUserId = tempUserId;
            requesterSchedule.UpdatedAt = DateTime.UtcNow;
            targetSchedule.UpdatedAt = DateTime.UtcNow;

            await _scheduleRepository.UpdateAsync(requesterSchedule, cancellationToken);
            await _scheduleRepository.UpdateAsync(targetSchedule, cancellationToken);
        }

        await _swapRepository.UpdateAsync(request, cancellationToken);
        await _swapRepository.SaveChangesAsync(cancellationToken);

        await NotifyReviewAsync(request, dto.Status, cancellationToken);

        var updated = await _swapRepository.GetByIdAsync(requestId, cancellationToken);
        return await MapToDtoAsync(updated!, cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftSwapRequestResponseDto>> GetByCafeAsync(Guid cafeId, ShiftSwapStatus? status = null, CancellationToken cancellationToken = default)
    {
        var list = await _swapRepository.GetByCafeAsync(cafeId, status, cancellationToken);
        var mapped = new List<ShiftSwapRequestResponseDto>();
        foreach (var item in list)
        {
            mapped.Add(await MapToDtoAsync(item, cancellationToken));
        }
        return mapped;
    }

    public async Task<IReadOnlyList<ShiftSwapRequestResponseDto>> GetIncomingForStaffAsync(Guid staffUserId, CancellationToken cancellationToken = default)
    {
        var list = await _swapRepository.GetPendingForStaffAsync(staffUserId, cancellationToken);
        var mapped = new List<ShiftSwapRequestResponseDto>();
        foreach (var item in list)
        {
            mapped.Add(await MapToDtoAsync(item, cancellationToken));
        }
        return mapped;
    }

    public async Task<IReadOnlyList<ShiftSwapRequestResponseDto>> GetByRequesterAsync(Guid requesterId, CancellationToken cancellationToken = default)
    {
        var list = await _swapRepository.GetByRequesterAsync(requesterId, cancellationToken);
        var mapped = new List<ShiftSwapRequestResponseDto>();
        foreach (var item in list)
        {
            mapped.Add(await MapToDtoAsync(item, cancellationToken));
        }
        return mapped;
    }

    #region Private helpers

    private async Task<ShiftSwapRequestResponseDto> MapToDtoAsync(ShiftSwapRequest s, CancellationToken cancellationToken)
    {
        var requesterSchedule = await _scheduleRepository.GetByIdAsync(s.RequesterScheduleId, cancellationToken);
        var targetSchedule = await _scheduleRepository.GetByIdAsync(s.TargetScheduleId, cancellationToken);

        return new ShiftSwapRequestResponseDto
        {
            Id = s.Id,
            CafeId = s.CafeId,
            RequesterId = s.RequesterId,
            RequesterName = s.Requester?.Username ?? string.Empty,
            TargetStaffId = s.TargetStaffId,
            TargetStaffName = s.TargetStaff?.Username ?? string.Empty,
            RequesterScheduleId = s.RequesterScheduleId,
            RequesterSchedule = requesterSchedule == null ? null : await MapScheduleDtoAsync(requesterSchedule, cancellationToken),
            TargetScheduleId = s.TargetScheduleId,
            TargetSchedule = targetSchedule == null ? null : await MapScheduleDtoAsync(targetSchedule, cancellationToken),
            Status = s.Status,
            ReviewedByUserId = s.ReviewedByUserId,
            ReviewerName = string.Empty,
            ReviewedAt = s.ReviewedAt,
            ReviewNote = s.ReviewNote,
            CreatedAt = s.CreatedAt
        };
    }

    private async Task<StaffScheduleResponseDto> MapScheduleDtoAsync(StaffSchedule s, CancellationToken cancellationToken)
    {
        var staffName = s.User?.Username ?? string.Empty;
        if (string.IsNullOrEmpty(staffName))
        {
            var user = await _cafeRepository.GetUserByIdAsync(s.StaffUserId, cancellationToken);
            staffName = user?.Username ?? string.Empty;
        }

        return new StaffScheduleResponseDto
        {
            Id = s.Id,
            CafeId = s.CafeId,
            StaffUserId = s.StaffUserId,
            StaffName = staffName,
            DayOfWeek = s.DayOfWeek,
            DayOfWeekName = GetVietnameseDayOfWeekName(s.DayOfWeek),
            StartTime = s.StartTime,
            EndTime = s.EndTime,
            IsOvernight = s.EndTime < s.StartTime,
            DurationMinutes = CalculateDurationMinutes(s.StartTime, s.EndTime),
            ShiftType = s.ShiftType,
            IsRecurring = s.IsRecurring,
            Note = s.Note,
            Status = s.Status,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt
        };
    }

    private static int CalculateDurationMinutes(TimeOnly start, TimeOnly end)
    {
        if (end > start) return (int)(end - start).TotalMinutes;
        return (int)((TimeOnly.MaxValue - start).TotalMinutes + 1 + (end - TimeOnly.MinValue).TotalMinutes);
    }

    private static string GetVietnameseDayOfWeekName(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday => "Thứ 2",
        DayOfWeek.Tuesday => "Thứ 3",
        DayOfWeek.Wednesday => "Thứ 4",
        DayOfWeek.Thursday => "Thứ 5",
        DayOfWeek.Friday => "Thứ 6",
        DayOfWeek.Saturday => "Thứ 7",
        DayOfWeek.Sunday => "Chủ nhật",
        _ => dow.ToString()
    };

    private async Task NotifyTargetStaffAsync(string requesterName, Guid targetStaffId, CancellationToken cancellationToken)
    {
        try
        {
            await _pushNotificationService.SendAsync(
                targetStaffId,
                "Yêu cầu đổi ca",
                $"{requesterName} muốn đổi ca với bạn. Vui lòng chờ Manager duyệt.",
                new Dictionary<string, string>
                {
                    { "type", "shift_swap_requested" }
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ShiftSwap] Failed to send notification to target {TargetId}", targetStaffId);
        }
    }

    private async Task NotifyReviewAsync(ShiftSwapRequest request, ShiftSwapStatus status, CancellationToken cancellationToken)
    {
        try
        {
            var message = status switch
            {
                ShiftSwapStatus.Approved => "Yêu cầu đổi ca của bạn đã được duyệt. Lịch đã được hoán đổi.",
                ShiftSwapStatus.Rejected => "Yêu cầu đổi ca của bạn đã bị từ chối.",
                ShiftSwapStatus.Cancelled => "Yêu cầu đổi ca đã bị hủy.",
                _ => "Yêu cầu đổi ca đã được cập nhật."
            };

            // Notify cả requester và target
            foreach (var userId in new[] { request.RequesterId, request.TargetStaffId }.Distinct())
            {
                await _pushNotificationService.SendAsync(
                    userId,
                    "Đổi ca",
                    message,
                    new Dictionary<string, string>
                    {
                        { "type", $"shift_swap_reviewed" },
                        { "status", status.ToString() }
                    },
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ShiftSwap] Failed to send review notification for swap {SwapId}", request.Id);
        }
    }

    #endregion
}