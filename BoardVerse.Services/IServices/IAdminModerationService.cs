using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Services.IServices
{
    public interface IAdminModerationService
    {
        Task<PaginatedResponse<KarmaLogDto>> GetKarmaLogsAsync(
            Guid? userId,
            KarmaViolationCategory? violationCategory,
            DateTime? fromUtc,
            DateTime? toUtc,
            PaginationParams pagination, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<UserKarmaAlertDto>> GetKarmaAlertsAsync(CancellationToken cancellationToken = default);

        Task<AdminPunishUserResponseDto> PunishUserAsync(
            Guid adminUserId,
            Guid targetUserId,
            AdminPunishUserRequestDto request, CancellationToken cancellationToken = default);

        Task<AdminAdjustKarmaResponseDto> AdjustKarmaAsync(
            Guid adminUserId,
            Guid targetUserId,
            AdminAdjustKarmaRequestDto request, CancellationToken cancellationToken = default);

        Task<PaginatedResponse<CoolingOffUserDto>> GetCoolingOffUsersAsync(PaginationParams pagination);

        Task<ReleaseCoolingOffResponseDto> ReleaseCoolingOffAsync(Guid adminUserId, Guid targetUserId, string reason);

        // A-03: BR-RISK-05 — Liệt kê PlayerActionHistory của 1 user (audit log admin).
        Task<PaginatedResponse<PlayerActionHistoryDto>> GetPlayerActionHistoryAsync(PlayerActionHistoryQuery query);
    }
}
