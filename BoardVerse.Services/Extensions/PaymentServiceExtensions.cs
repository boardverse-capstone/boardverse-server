using BoardVerse.Core.Settings;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using BoardVerse.Services.Services.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BoardVerse.Services.Extensions;

public static class PaymentServiceExtensions
{
    public static IServiceCollection AddBoardVersePayment(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SePaySettings>(configuration.GetSection(SePaySettings.SectionName));

        services.AddHttpClient<ISePayClient, SePayClient>()
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        services.AddScoped<IVietQrClient, VietQrClient>();

        // VietQR gateway — tạo QR tĩnh, không cần retry
        services.AddScoped<IPaymentGatewayService, PaymentGatewayService>();

        services.AddScoped<IManualPaymentService, ManualPaymentService>();
        services.AddScoped<IBookingDepositService, BookingDepositService>();
        services.AddScoped<IPaymentService, PaymentService>();

        return services;
    }
}
