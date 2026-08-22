using BoardVerse.Core.DTOs.Admin;

using System.Threading;
namespace BoardVerse.Services.IServices;

/// <summary>
/// Service interface for Admin reporting and statistics.
/// </summary>
public interface IAdminReportService
{
    /// <summary>
    /// Lấy tổng quan dashboard: users, cafes, tournaments, lobbies, bookings, deposits, revenue.
    /// </summary>
    Task<AdminDashboardOverviewDto> GetDashboardOverviewAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Báo cáo lobby failures: tổng hợp theo loại (timeout, host-cancelled, cafe-rejected, cafe-expired).
    /// </summary>
    Task<AdminLobbyFailuresReportDto> GetLobbyFailuresReportAsync(
        int page,
        int pageSize,
        DateTime? fromUtc,
        DateTime? toUtc,
        string? failureType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Báo cáo deposits: tổng hợp theo trạng thái (pending, paid, refunded, forfeited).
    /// </summary>
    Task<AdminDepositsReportDto> GetDepositsReportAsync(
        int page,
        int pageSize,
        DateTime? fromUtc,
        DateTime? toUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Báo cáo performance của tất cả cafes: bookings, lobbies, tournaments, revenue.
    /// </summary>
    Task<AdminCafePerformanceReportDto> GetCafePerformanceReportAsync(
        int page,
        int pageSize,
        string sortBy,
        bool sortDescending, CancellationToken cancellationToken = default);

    /// <summary>
    /// Báo cáo chi tiết performance của một cafe cụ thể.
    /// </summary>
    Task<AdminCafePerformanceDto?> GetCafePerformanceDetailAsync(
        Guid cafeId,
        DateTime? fromUtc,
        DateTime? toUtc, CancellationToken cancellationToken = default);
}
