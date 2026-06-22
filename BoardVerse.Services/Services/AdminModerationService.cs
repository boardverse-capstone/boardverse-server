using BoardVerse.Core.Common;
using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class AdminModerationService : IAdminModerationService
    {
        private readonly IAdminModerationRepository _repository;

        public AdminModerationService(IAdminModerationRepository repository)
        {
            _repository = repository;
        }

        public Task<PaginatedResponse<KarmaLogDto>> GetKarmaLogsAsync(
            Guid? userId,
            KarmaViolationCategory? violationCategory,
            DateTime? fromUtc,
            DateTime? toUtc,
            PaginationParams pagination) =>
            _repository.GetKarmaLogsAsync(userId, violationCategory, fromUtc, toUtc, pagination);

        public Task<IReadOnlyList<UserKarmaAlertDto>> GetKarmaAlertsAsync() =>
            _repository.GetKarmaAlertsAsync(SystemConfigKeys.KarmaSafetyThreshold);

        public async Task<AdminPunishUserResponseDto> PunishUserAsync(
            Guid adminUserId,
            Guid targetUserId,
            AdminPunishUserRequestDto request)
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
                    break;

                case AdminPunishmentActionType.Suspend:
                    if (!request.DurationDays.HasValue || request.DurationDays.Value < 1)
                    {
                        throw new BadRequestException(ApiErrorMessages.AdminModeration.SuspendDurationRequired);
                    }

                    user.AccountStatus = UserAccountStatus.Suspended;
                    user.BlockReason = reason;
                    user.BlockedAt = utcNow;
                    user.LockoutEndDate = utcNow.AddDays(request.DurationDays.Value);
                    user.UpdatedAt = utcNow;
                    break;

                case AdminPunishmentActionType.Ban:
                    user.AccountStatus = UserAccountStatus.Banned;
                    user.BlockReason = reason;
                    user.BlockedAt = utcNow;
                    user.LockoutEndDate = null;
                    user.UpdatedAt = utcNow;
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
            AdminAdjustKarmaRequestDto request)
        {
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
            profile.KarmaPoints = karmaAfter;
            profile.GamerTier = KarmaRatingHelper.ResolveTier(karmaAfter);
            profile.UpdatedAt = DateTime.UtcNow;

            var log = new KarmaLog
            {
                Id = Guid.NewGuid(),
                UserId = targetUserId,
                ViolationCategory = KarmaViolationCategory.AdminManual,
                Source = KarmaLogSource.AdminManual,
                KarmaPointsChange = request.Amount,
                KarmaBefore = karmaBefore,
                KarmaAfter = karmaAfter,
                Reason = request.Reason.Trim(),
                PerformedByUserId = adminUserId,
                IsAdminAdjustment = true,
                CreatedAt = DateTime.UtcNow
            };

            await _repository.AddKarmaLogAsync(log);
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
    }
}
