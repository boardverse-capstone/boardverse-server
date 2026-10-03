using BoardVerse.Core.Common;
using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BoardVerse.Services.Services
{
    public class AdminModerationService : IAdminModerationService
    {
        private readonly IAdminModerationRepository _repository;
        private readonly ICoolingOffService _coolingOffService;
        private readonly BoardVerseDbContext _db;
        private readonly ILogger<AdminModerationService> _logger;

        public AdminModerationService(
            IAdminModerationRepository repository,
            ICoolingOffService coolingOffService,
            BoardVerseDbContext db,
            ILogger<AdminModerationService> logger)
        {
            _repository = repository;
            _coolingOffService = coolingOffService;
            _db = db;
            _logger = logger;
        }

        public Task<PaginatedResponse<KarmaLogDto>> GetKarmaLogsAsync(
            Guid? userId,
            KarmaViolationCategory? violationCategory,
            DateTime? fromUtc,
            DateTime? toUtc,
            PaginationParams pagination, CancellationToken cancellationToken = default) =>
            _repository.GetKarmaLogsAsync(userId, violationCategory, fromUtc, toUtc, pagination, cancellationToken);

        public Task<IReadOnlyList<UserKarmaAlertDto>> GetKarmaAlertsAsync(CancellationToken cancellationToken = default) =>
            _repository.GetKarmaAlertsAsync(SystemConfigKeys.KarmaSafetyThreshold, cancellationToken);

        public Task<PaginatedResponse<PlayerActionHistoryDto>> GetPlayerActionHistoryAsync(PlayerActionHistoryQuery query) =>
            _repository.GetPlayerActionHistoryAsync(query);

        public async Task<AdminPunishUserResponseDto> PunishUserAsync(
            Guid adminUserId,
            Guid targetUserId,
            AdminPunishUserRequestDto request, CancellationToken cancellationToken = default)
        {
            var user = await _repository.GetUserWithProfileForUpdateAsync(targetUserId);
            if (user == null)
            {
                throw new UserNotFoundException(ApiErrorMessages.AdminUsers.UserNotFound(targetUserId));
            }

            if (user.Role == UserRole.Admin)
            {
                throw new ForbiddenException(ApiErrorMessages.AdminModeration.CannotPunishAdmin);
            }

            var utcNow = DateTime.UtcNow;
            var reason = request.Reason.Trim();

            switch (request.ActionType)
            {
                case AdminPunishmentActionType.Warning:
                    await _repository.AddKarmaLogAsync(new KarmaLog
                    {
                        Id = Guid.NewGuid(),
                        UserId = targetUserId,
                        ViolationCategory = KarmaViolationCategory.AdminWarning,
                        Source = KarmaLogSource.AdminManual,
                        KarmaPointsChange = 0,
                        KarmaBefore = user.Profile?.KarmaPoints ?? 100,
                        KarmaAfter = user.Profile?.KarmaPoints ?? 100,
                        Reason = reason,
                        PerformedByUserId = adminUserId,
                        IsAdminAdjustment = false,
                        CreatedAt = utcNow
                    });

                    // BR-RISK-05: audit log ghi nhận Warning.
                    _db.PlayerActionHistories.Add(new PlayerActionHistory
                    {
                        Id = Guid.NewGuid(),
                        UserId = targetUserId,
                        ActionType = AdminActionType.Warning,
                        ActionBy = adminUserId,
                        Reason = reason,
                        Metadata = JsonSerializer.Serialize(new
                        {
                            previousStatus = user.AccountStatus.ToString()
                        }),
                        CreatedAt = utcNow
                    });
                    break;

                case AdminPunishmentActionType.Suspend:
                    if (!request.DurationDays.HasValue || request.DurationDays.Value < 1)
                    {
                        throw new BadRequestException(ApiErrorMessages.AdminModeration.SuspendDurationRequired);
                    }

                    var previousStatusBeforeSuspend = user.AccountStatus;
                    var lockoutEnd = utcNow.AddDays(request.DurationDays.Value);

                    user.AccountStatus = UserAccountStatus.Suspended;
                    user.BlockReason = reason;
                    user.BlockedAt = utcNow;
                    user.LockoutEndDate = lockoutEnd;
                    user.UpdatedAt = utcNow;

                    // BR-RISK-05: audit log ghi nhận Suspend + expiresAt.
                    _db.PlayerActionHistories.Add(new PlayerActionHistory
                    {
                        Id = Guid.NewGuid(),
                        UserId = targetUserId,
                        ActionType = AdminActionType.Suspend,
                        ActionBy = adminUserId,
                        Reason = reason,
                        Metadata = JsonSerializer.Serialize(new
                        {
                            previousStatus = previousStatusBeforeSuspend.ToString(),
                            newStatus = user.AccountStatus.ToString(),
                            durationDays = request.DurationDays.Value
                        }),
                        CreatedAt = utcNow,
                        ExpiresAt = lockoutEnd
                    });
                    break;

                case AdminPunishmentActionType.Ban:
                    var previousStatusBeforeBan = user.AccountStatus;

                    user.AccountStatus = UserAccountStatus.Banned;
                    user.BlockReason = reason;
                    user.BlockedAt = utcNow;
                    user.LockoutEndDate = null;
                    user.UpdatedAt = utcNow;

                    // BR-RISK-05: audit log ghi nhận Ban (vĩnh viễn).
                    _db.PlayerActionHistories.Add(new PlayerActionHistory
                    {
                        Id = Guid.NewGuid(),
                        UserId = targetUserId,
                        ActionType = AdminActionType.Ban,
                        ActionBy = adminUserId,
                        Reason = reason,
                        Metadata = JsonSerializer.Serialize(new
                        {
                            previousStatus = previousStatusBeforeBan.ToString(),
                            newStatus = user.AccountStatus.ToString()
                        }),
                        CreatedAt = utcNow
                    });
                    break;

                default:
                    throw new BadRequestException(ApiErrorMessages.AdminModeration.InvalidPunishmentAction);
            }

            await _repository.SaveChangesAsync();

            return new AdminPunishUserResponseDto
            {
                UserId = user.Id,
                ActionType = request.ActionType.ToString(),
                AccountStatus = user.AccountStatus.ToString(),
                LockoutEndDate = user.LockoutEndDate,
                Reason = reason
            };
        }

        public async Task<AdminAdjustKarmaResponseDto> AdjustKarmaAsync(
            Guid adminUserId,
            Guid targetUserId,
            AdminAdjustKarmaRequestDto request, CancellationToken cancellationToken = default)
        {
            // K-07: Enforce karma adjustment range [-100, 100] at server-side.
            if (request.Amount < -100 || request.Amount > 100)
            {
                throw new BadRequestException(ApiErrorMessages.AdminModeration.KarmaAdjustmentRange);
            }

            if (request.Amount == 0)
            {
                throw new BadRequestException(ApiErrorMessages.AdminModeration.KarmaAdjustmentZeroNotAllowed);
            }

            var profile = await _repository.GetProfileForUpdateAsync(targetUserId);
            if (profile == null)
            {
                throw new NotFoundException(ApiErrorMessages.AdminModeration.ProfileNotFound(targetUserId));
            }

            var karmaBefore = profile.KarmaPoints;
            var karmaAfter = KarmaRatingHelper.ApplyDeltaToKarmaPoints(karmaBefore, request.Amount);
            var actualDelta = karmaAfter - karmaBefore;
            profile.KarmaPoints = karmaAfter;
            profile.GamerTier = KarmaRatingHelper.ResolveTier(karmaAfter);
            profile.UpdatedAt = DateTime.UtcNow;

            var log = new KarmaLog
            {
                Id = Guid.NewGuid(),
                UserId = targetUserId,
                ViolationCategory = KarmaViolationCategory.AdminManual,
                Source = KarmaLogSource.AdminManual,
                KarmaPointsChange = actualDelta,
                KarmaBefore = karmaBefore,
                KarmaAfter = karmaAfter,
                Reason = request.Reason.Trim(),
                PerformedByUserId = adminUserId,
                IsAdminAdjustment = true,
                CreatedAt = DateTime.UtcNow
            };

            await _repository.AddKarmaLogAsync(log);

            // BR-RISK-05: audit log ghi nhận admin adjust karma.
            _db.PlayerActionHistories.Add(new PlayerActionHistory
            {
                Id = Guid.NewGuid(),
                UserId = targetUserId,
                ActionType = AdminActionType.AdminCredit,
                ActionBy = adminUserId,
                Reason = request.Reason.Trim(),
                Metadata = JsonSerializer.Serialize(new
                {
                    karmaBefore,
                    karmaAfter,
                    delta = request.Amount,
                    actualDelta,
                    karmaLogId = log.Id
                }),
                CreatedAt = DateTime.UtcNow
            });

            await _repository.SaveChangesAsync();

            return new AdminAdjustKarmaResponseDto
            {
                UserId = targetUserId,
                PreviousKarma = karmaBefore,
                NewKarma = karmaAfter,
                AdjustedAmount = request.Amount,
                KarmaLogId = log.Id
            };
        }

        public async Task<PaginatedResponse<CoolingOffUserDto>> GetCoolingOffUsersAsync(
            PaginationParams pagination,
            CancellationToken cancellationToken = default)
        {
            var page = await _repository.GetCoolingOffUsersAsync(pagination, cancellationToken);

            // BR-NEW-10 §XI.1: mỗi user trong cooling-off phải kèm signals dùng cho admin review.
            // Repo chỉ map wallet → DTO (basic fields). Service enrich bằng cách gọi
            // ICoolingOffService.DetectSignalsAsync song song để tránh block UI khi page lớn.
            if (page.Data.Any())
            {
                var now = DateTime.UtcNow;
                var enrichmentTasks = page.Data.Select(async dto =>
                {
                    try
                    {
                        var (timeoutCount, cancelCount, forfeitAmount) =
                            await _coolingOffService.DetectSignalsAsync(dto.UserId, now, cancellationToken);
                        dto.FailedLobbiesInWeek = timeoutCount;
                        dto.CancelledLobbiesInWeek = cancelCount;
                        dto.TotalForfeitedBvc = forfeitAmount;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Nếu 1 user fail, vẫn trả các user khác — log warning + giữ 0.
                        _logger.LogWarning(ex,
                            "[BR-NEW-10] Failed to enrich cooling-off signals for userId={UserId}",
                            dto.UserId);
                    }
                });

                await Task.WhenAll(enrichmentTasks);
            }

            return page;
        }

        public async Task<ReleaseCoolingOffResponseDto> ReleaseCoolingOffAsync(Guid adminUserId, Guid targetUserId, string reason)
        {
            var wallet = await _repository.GetWalletForUpdateAsync(targetUserId);
            if (wallet == null)
            {
                throw new NotFoundException(ApiErrorMessages.AdminModeration.WalletNotFound(targetUserId));
            }

            if (!wallet.IsCoolingOff)
            {
                throw new ConflictException(ApiErrorMessages.AdminModeration.UserNotInCoolingOff);
            }

            var user = await _repository.GetUserWithProfileForUpdateAsync(targetUserId);
            if (user == null)
            {
                throw new UserNotFoundException(ApiErrorMessages.AdminUsers.UserNotFound(targetUserId));
            }

            var previousCoolingOffExpiresAt = wallet.CoolingOffExpiresAt;

            wallet.IsCoolingOff = false;
            wallet.CoolingOffExpiresAt = null;
            wallet.UpdatedAt = DateTime.UtcNow;

            await _repository.SaveChangesAsync();

            return new ReleaseCoolingOffResponseDto
            {
                UserId = targetUserId,
                Username = user.Username,
                WasCoolingOff = true,
                PreviousCoolingOffExpiresAt = previousCoolingOffExpiresAt,
                ReleaseReason = reason.Trim(),
                ReleasedBy = adminUserId,
                ReleasedAt = DateTime.UtcNow
            };
        }
    }
}
