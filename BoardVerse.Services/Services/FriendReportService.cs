using BoardVerse.Core.DTOs.Friend;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services;

public class FriendReportService : IFriendReportService
{
    private readonly IFriendReportRepository _reportRepository;
    private readonly IFriendshipRepository _friendshipRepository;
    private readonly IUserManagementRepository _userRepository;

    public FriendReportService(
        IFriendReportRepository reportRepository,
        IFriendshipRepository friendshipRepository,
        IUserManagementRepository userRepository)
    {
        _reportRepository = reportRepository;
        _friendshipRepository = friendshipRepository;
        _userRepository = userRepository;
    }

    public async Task<FriendReportDto> SubmitReportAsync(Guid reporterId, CreateFriendReportDto dto)
    {
        if (dto.TargetUserId == reporterId)
            throw new BadRequestException(ApiErrorMessages.Friend.CannotReportSelf);

        var target = await _userRepository.GetByIdAsync(dto.TargetUserId)
            ?? throw new NotFoundException(ApiErrorMessages.Friend.UserNotFound(dto.TargetUserId));

        if (target.Role == UserRole.Admin)
            throw new ForbiddenException(ApiErrorMessages.Friend.CannotReportAdmin);

        // BR-FRIEND-REPORT-01: Reporter phải từng có quan hệ với Target (Accepted).
        var pair = await _friendshipRepository.GetByPairAsync(reporterId, dto.TargetUserId);
        if (pair == null || pair.Status != FriendshipStatus.Accepted)
        {
            throw new BadRequestException(ApiErrorMessages.Friend.CannotReportNotFriend);
        }

        // BR-FRIEND-REPORT-02: Tránh duplicate.
        var existing = await _reportRepository.GetPendingByReporterAndTargetAsync(reporterId, dto.TargetUserId);
        if (existing != null)
        {
            throw new ConflictException(ApiErrorMessages.Friend.ReportAlreadyExists(dto.TargetUserId));
        }

        if (!Enum.TryParse<FriendReportCategory>(dto.Category, ignoreCase: true, out var category))
        {
            category = FriendReportCategory.Other;
        }

        var report = new FriendReport
        {
            Id = Guid.NewGuid(),
            ReporterId = reporterId,
            TargetUserId = dto.TargetUserId,
            Category = category,
            Reason = dto.Reason.Trim(),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        await _reportRepository.AddAsync(report);
        await _reportRepository.SaveChangesAsync();

        return new FriendReportDto
        {
            ReportId = report.Id,
            TargetUserId = target.Id,
            TargetUsername = target.Username,
            Category = report.Category.ToString(),
            Reason = report.Reason,
            Status = report.Status,
            CreatedAt = report.CreatedAt
        };
    }

    public async Task<IReadOnlyList<FriendReportDto>> GetMyReportsAsync(Guid reporterId)
    {
        var reports = await _reportRepository.GetByReporterAsync(reporterId);
        if (reports.Count == 0) return Array.Empty<FriendReportDto>();

        var targetIds = reports.Select(r => r.TargetUserId).Distinct().ToHashSet();
        var users = await _userRepository.GetByIdsAsync(targetIds);
        var userDict = users.ToDictionary(u => u.Id);

        return reports.Select(r =>
        {
            userDict.TryGetValue(r.TargetUserId, out var u);
            return new FriendReportDto
            {
                ReportId = r.Id,
                TargetUserId = r.TargetUserId,
                TargetUsername = u?.Username ?? "(unknown)",
                Category = r.Category.ToString(),
                Reason = r.Reason,
                Status = r.Status,
                CreatedAt = r.CreatedAt,
                ReviewedAt = r.ReviewedAt,
                AdminNote = r.AdminNote
            };
        }).ToList();
    }
}
