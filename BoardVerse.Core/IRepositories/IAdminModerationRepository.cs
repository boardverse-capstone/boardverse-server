using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface IAdminModerationRepository
    {
        Task<PaginatedResponse<KarmaLogDto>> GetKarmaLogsAsync(
            Guid? userId,
            KarmaViolationCategory? violationCategory,
            DateTime? fromUtc,
            DateTime? toUtc,
            PaginationParams pagination, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<UserKarmaAlertDto>> GetKarmaAlertsAsync(int threshold, CancellationToken cancellationToken = default);

        Task<User?> GetUserWithProfileForUpdateAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<UserProfile?> GetProfileForUpdateAsync(Guid userId, CancellationToken cancellationToken = default);

        Task AddKarmaLogAsync(KarmaLog log, CancellationToken cancellationToken = default);

        Task SaveChangesAsync(CancellationToken cancellationToken = default);

        Task<PaginatedResponse<CoolingOffUserDto>> GetCoolingOffUsersAsync(PaginationParams pagination, CancellationToken cancellationToken = default);

        Task<Wallet?> GetWalletForUpdateAsync(Guid userId, CancellationToken cancellationToken = default);

        // A-03: BR-RISK-05 — Đọc lịch sử admin action.
        Task<PaginatedResponse<PlayerActionHistoryDto>> GetPlayerActionHistoryAsync(PlayerActionHistoryQuery query, CancellationToken cancellationToken = default);
    }
}
