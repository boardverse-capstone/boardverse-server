using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.HostedServices;

/// <summary>
/// Base class cho các scheduler hosted services (BR §21A.5, XXI-H.8).
/// Chạy một delegate theo interval cố định, tự xử lý crash + logging.
/// </summary>
public abstract class PollingHostedService : BackgroundService
{
    private readonly TimeSpan _interval;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;

    protected PollingHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        TimeSpan interval)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = interval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "{Service} started. Interval={Interval}",
            GetType().Name, _interval);

        // Stagger để không phải service nào cũng chạy đúng 0 giây mỗi giờ.
        var jitter = TimeSpan.FromSeconds(Random.Shared.Next(0, (int)Math.Min(30, _interval.TotalSeconds)));
        try
        {
            await Task.Delay(jitter, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "{Service} tick failed. Sẽ retry sau {Interval}.",
                    GetType().Name, _interval);
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("{Service} stopped.", GetType().Name);
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        await ExecuteTickAsync(scope.ServiceProvider, ct);
    }

    /// <summary>Override để thực hiện 1 tick của job.</summary>
    protected abstract Task ExecuteTickAsync(IServiceProvider sp, CancellationToken ct);
}
