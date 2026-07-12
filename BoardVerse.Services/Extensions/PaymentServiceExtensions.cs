using BoardVerse.Core.Settings;
using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;
using BoardVerse.Services.Services.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Extensions;

public static class PaymentServiceExtensions
{
    public static IServiceCollection AddBoardVersePayment(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SePaySettings>(configuration.GetSection(SePaySettings.SectionName));
        services.Configure<PaymentGatewaySettings>(configuration.GetSection(PaymentGatewaySettings.SectionName));

        services.AddHttpClient<ISePayClient, SePayClient>();

        // VietQR client for fallback
        services.AddScoped<IVietQrClient, VietQrClient>();

        // Payment gateway with fallback chain
        services.AddScoped<IPaymentGatewayService, PaymentGatewayService>();

        // Manual payment service for staff override
        services.AddScoped<IManualPaymentService, ManualPaymentService>();

        services.AddScoped<IBookingDepositService, BookingDepositService>();
        services.AddScoped<IPaymentService, PaymentService>();

        return services;
    }
}
