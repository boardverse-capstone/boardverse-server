using BoardVerse.Services.HostedServices;
using Microsoft.Extensions.DependencyInjection;

namespace BoardVerse.Services;

/// <summary>
/// Đăng ký các scheduler hosted services (BR § XXI-H.8).
/// </summary>
public static class ReservationSchedulerExtensions
{
    /// <summary>
    /// Đăng ký 3 job scheduler gọi IReservationService:
    /// - RecruitmentDeadlineJob: mỗi 60s, xử lý lobby đến deadline.
    /// - CafeApprovalExpiryJob: mỗi 5 phút, xử lý lobby pendingCafeApproval quá 24h.
    /// - NoShowCheckJob: mỗi 5 phút, xử lý reservation Confirmed chưa check-in.
    /// </summary>
    public static IServiceCollection AddReservationSchedulers(this IServiceCollection services)
    {
        services.AddHostedService<RecruitmentDeadlineJob>();
        services.AddHostedService<CafeApprovalExpiryJob>();
        services.AddHostedService<NoShowCheckJob>();
        return services;
    }
}
