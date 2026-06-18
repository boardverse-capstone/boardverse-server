using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Enum;

namespace BoardVerse.Services.IServices
{
    public interface IAdminModerationService
    {
        Task<PaginatedResponse<KarmaLogDto>> GetKarmaLogsAsync(
            Guid? userId,
            KarmaViolationCategory? violationCategory,
            DateTime? fromUtc,
            DateTime? toUtc,
            PaginationParams pagination);

        Task<IReadOnlyList<UserKarmaAlertDto>> GetKarmaAlertsAsync();

        Task<AdminPunishUserResponseDto> PunishUserAsync(
            Guid adminUserId,
            Guid targetUserId,
            AdminPunishUserRequestDto request);

        Task<AdminAdjustKarmaResponseDto> AdjustKarmaAsync(
            Guid adminUserId,
            Guid targetUserId,
            AdminAdjustKarmaRequestDto request);
    }
}
