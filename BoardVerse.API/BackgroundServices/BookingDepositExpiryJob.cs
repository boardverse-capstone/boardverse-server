using BoardVerse.Services.IServices;
using BoardVerse.Services.Services;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Background job xử lý các đơn cọc PENDING quá hạn thanh toán (5 phút).
/// BR-02: Quá 5 phút không thanh toán → đơn đặt chỗ tự động chuyển sang EXPIRED,
/// giải phóng ghế trống về kho trực tuyến.
/// Deposit chuyển sang trạng thái Refunded (hoàn cọc).
/// </summary>
public class BookingDepositExpiryJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BookingDepositExpiryJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    public BookingDepositExpiryJob(IServiceProvider serviceProvider, ILogger<BookingDepositExpiryJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BookingDepositExpiryJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredDepositsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in BookingDepositExpiryJob");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ProcessExpiredDepositsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();

        await paymentService.ProcessExpiredDepositsAsync();
    }
}
