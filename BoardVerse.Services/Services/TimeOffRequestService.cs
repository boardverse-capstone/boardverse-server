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
/// TimeOffRequestService — quản lý yêu cầu nghỉ phép của staff.
/// Khi Manager duyệt, hệ thống tự động tạo các bản ghi StaffUnavailableDate trong khoảng StartDate..EndDate.
/// </summary>
public class TimeOffRequestService : ITimeOffRequestService
{
    private readonly ITimeOffRequestRepository _requestRepository;
    private readonly IStaffUnavailableDateRepository _unavailableRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly IPushNotificationService _pushNotificationService;
    private readonly ILogger<TimeOffRequestService> _logger;

    public TimeOffRequestService(
        ITimeOffRequestRepository requestRepository,
        IStaffUnavailableDateRepository unavailableRepository,
        ICafeRepository cafeRepository,
        IPushNotificationService pushNotificationService,
        ILogger<TimeOffRequestService> logger)
    {
        _requestRepository = requestRepository;
        _unavailableRepository = unavailableRepository;
        _cafeRepository = cafeRepository;
        _pushNotificationService = pushNotificationService;
        _logger = logger;
    }

    public async Task<TimeOffRequestResponseDto> CreateAsync(Guid cafeId, Guid staffUserId, CreateTimeOffRequestDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.EndDate < dto.StartDate)
            throw new BadRequestException(ApiErrorMessages.StaffSchedule.TimeOffEndBeforeStart);

        // GAP-VALIDATION-04 fix: validate cafe tồn tại TRƯỚC khi xử lý các logic khác.
        // Trước đây: nếu staff pass cafeId random GUID không tồn tại → IsStaffMemberExistsAsync trả false → 400 "không phải nhân viên".
        // → không rõ ràng là do cafeId sai. Giờ trả 404 đúng nguyên nhân.
        var cafe = await _cafeRepository.GetByIdAsync(cafeId, cancellationToken);
        if (cafe == null)
            throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        var isStaff = await _cafeRepository.IsStaffMemberExistsAsync(cafeId, staffUserId, cancellationToken);
        if (!isStaff && cafe.ManagerId != staffUserId)
            throw new BadRequestException(ApiErrorMessages.StaffSchedule.NotCafeStaff);

        var request = new TimeOffRequest
        {
            Id = Guid.NewGuid(),
            CafeId = cafeId,
            StaffUserId = staffUserId,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            Reason = dto.Reason,
            Status = TimeOffStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await _requestRepository.AddAsync(request, cancellationToken);
        await _requestRepository.SaveChangesAsync(cancellationToken);

        var created = await _requestRepository.GetByIdAsync(request.Id, cancellationToken);
        return MapToDto(created!);
    }

    public async Task<TimeOffRequestResponseDto> ReviewAsync(Guid requestId, Guid managerUserId, ReviewTimeOffRequestDto dto, CancellationToken cancellationToken = default)
    {
        var request = await _requestRepository.GetByIdAsync(requestId, cancellationToken);
        if (request == null)
            throw new NotFoundException(ApiErrorMessages.StaffSchedule.TimeOffRequestNotFound(requestId));

        if (request.Status != TimeOffStatus.Pending)
            throw new ConflictException(ApiErrorMessages.StaffSchedule.TimeOffAlreadyReviewed);

        request.Status = dto.Status;
        request.ReviewedByUserId = managerUserId;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewNote = dto.ReviewNote;

        await _requestRepository.UpdateAsync(request, cancellationToken);
        await _requestRepository.SaveChangesAsync(cancellationToken);

        // Khi Approved → tạo StaffUnavailableDate cho từng ngày
        if (dto.Status == TimeOffStatus.Approved)
        {
            var unavailableList = new List<StaffUnavailableDate>();
            for (var date = request.StartDate; date <= request.EndDate; date = date.AddDays(1))
            {
                // Tránh duplicate nếu staff đã tự thêm StaffUnavailableDate ngày đó
                var exists = await _unavailableRepository.ExistsAsync(request.StaffUserId, date, cancellationToken);
                if (exists) continue;

                unavailableList.Add(new StaffUnavailableDate
                {
                    Id = Guid.NewGuid(),
                    CafeId = request.CafeId,
                    StaffUserId = request.StaffUserId,
                    Date = date,
                    Reason = $"Nghỉ phép: {request.Reason ?? "(không có lý do)"}",
                    CreatedAt = DateTime.UtcNow
                });
            }
            if (unavailableList.Count > 0)
            {
                await _unavailableRepository.AddRangeAsync(unavailableList, cancellationToken);
                await _unavailableRepository.SaveChangesAsync(cancellationToken);
            }
        }

        await NotifyReviewAsync(request, dto.Status, cancellationToken);

        var updated = await _requestRepository.GetByIdAsync(requestId, cancellationToken);
        return MapToDto(updated!);
    }

    public async Task<IReadOnlyList<TimeOffRequestResponseDto>> GetByCafeAsync(Guid cafeId, TimeOffStatus? status = null, CancellationToken cancellationToken = default)
    {
        var list = await _requestRepository.GetByCafeAsync(cafeId, status, cancellationToken);
        return list.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<TimeOffRequestResponseDto>> GetMyRequestsAsync(Guid staffUserId, CancellationToken cancellationToken = default)
    {
        var list = await _requestRepository.GetByStaffAsync(staffUserId, cancellationToken);
        return list.Select(MapToDto).ToList();
    }

    #region Private helpers

    private static TimeOffRequestResponseDto MapToDto(TimeOffRequest r) => new()
    {
        Id = r.Id,
        CafeId = r.CafeId,
        StaffUserId = r.StaffUserId,
        StaffName = r.Staff?.Username ?? string.Empty,
        StartDate = r.StartDate,
        EndDate = r.EndDate,
        Reason = r.Reason,
        Status = r.Status,
        ReviewedByUserId = r.ReviewedByUserId,
        ReviewerName = r.ReviewedByUser?.Username,
        ReviewedAt = r.ReviewedAt,
        ReviewNote = r.ReviewNote,
        CreatedAt = r.CreatedAt
    };

    private async Task NotifyReviewAsync(TimeOffRequest request, TimeOffStatus status, CancellationToken cancellationToken)
    {
        try
        {
            var message = status switch
            {
                TimeOffStatus.Approved => $"Yêu cầu nghỉ phép {request.StartDate:dd/MM/yyyy} - {request.EndDate:dd/MM/yyyy} đã được duyệt.",
                TimeOffStatus.Rejected => $"Yêu cầu nghỉ phép {request.StartDate:dd/MM/yyyy} - {request.EndDate:dd/MM/yyyy} đã bị từ chối.",
                TimeOffStatus.Cancelled => $"Yêu cầu nghỉ phép {request.StartDate:dd/MM/yyyy} - {request.EndDate:dd/MM/yyyy} đã bị hủy.",
                _ => $"Yêu cầu nghỉ phép của bạn đã được cập nhật."
            };

            await _pushNotificationService.SendAsync(
                request.StaffUserId,
                "Yêu cầu nghỉ phép",
                message,
                new Dictionary<string, string>
                {
                    { "type", $"time_off_reviewed" },
                    { "requestId", request.Id.ToString() },
                    { "status", status.ToString() }
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TimeOff] Failed to send notification for request {RequestId}", request.Id);
        }
    }

    #endregion
}