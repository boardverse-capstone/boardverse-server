using BoardVerse.Services.HostedServices;
using Microsoft.Extensions.DependencyInjection;

namespace BoardVerse.Services;

/// <summary>
/// Đăng ký các scheduler hosted services (BR § XXI-H.8).
///
/// Lưu ý: Recruitment deadline + Cafe approval + NoShow schedulers được gộp
/// trong <c>ReservationDeadlineJob</c> (BoardVerse.API/BackgroundServices) — đăng ký
/// trực tiếp ở <c>Program.cs</c>. KHÔNG đăng ký duplicate ở đây.
/// </summary>
public static class ReservationSchedulerExtensions
{
    /// <summary>
    /// Chỉ đăng ký các scheduler KHÔNG bị gộp trong API project:
    /// - BvcCaptureRetryJob: mỗi 5 phút, retry BVC capture cho phiên đã PAID nhưng capture fail.
    /// </summary>
    public static IServiceCollection AddReservationSchedulers(this IServiceCollection services)
    {
        services.AddHostedService<BvcCaptureRetryJob>(); // GAP-9: BVC capture retry
        return services;
    }
}
