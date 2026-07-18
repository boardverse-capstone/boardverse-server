using BoardVerse.Core.IRepositories;
using BoardVerse.Data.Repositories;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using BoardVerse.Services.Services.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace BoardVerse.Services.Extensions;

public static class PaymentServiceExtensions
{
    public static IServiceCollection AddBoardVersePayment(this IServiceCollection services)
    {
        services.AddHttpClient<ISePayClient, SePayClient>()
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        services.AddScoped<IVietQrClient, VietQrClient>();

        // VietQR gateway — tạo QR tĩnh, không cần retry
        services.AddScoped<IPaymentGatewayService, PaymentGatewayService>();

        // SePay Account Repository & Service
        services.AddScoped<ISePayAccountRepository, SePayAccountRepository>();
        services.AddScoped<ISePayAccountService, SePayAccountService>();

        // Current user service
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        services.AddScoped<IManualPaymentService, ManualPaymentService>();
        services.AddScoped<IBookingDepositService, BookingDepositService>();
        services.AddScoped<IPaymentService, PaymentService>();

        return services;
    }
}
