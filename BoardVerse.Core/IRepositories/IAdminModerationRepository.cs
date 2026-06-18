using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.IRepositories
{
    public interface IAdminModerationRepository
    {
        Task<PaginatedResponse<KarmaLogDto>> GetKarmaLogsAsync(
            Guid? userId,
            KarmaViolationCategory? violationCategory,
            DateTime? fromUtc,
            DateTime? toUtc,
            PaginationParams pagination);

        Task<IReadOnlyList<UserKarmaAlertDto>> GetKarmaAlertsAsync(int threshold);

        Task<User?> GetUserWithProfileForUpdateAsync(Guid userId);

        Task<UserProfile?> GetProfileForUpdateAsync(Guid userId);

        Task AddKarmaLogAsync(KarmaLog log);

        Task SaveChangesAsync();
    }
}
