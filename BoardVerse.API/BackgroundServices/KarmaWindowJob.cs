using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Background job tự động mở cửa sổ đánh giá Karma sau khi phiên chơi kết thúc.
/// State Machine: CLOSED → mở RatingOpenedAt → trigger Karma rating window.
/// </summary>
public class KarmaWindowJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KarmaWindowJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    public KarmaWindowJob(IServiceProvider serviceProvider, ILogger<KarmaWindowJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("KarmaWindowJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessKarmaWindowsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in KarmaWindowJob");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ProcessKarmaWindowsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();

        var now = DateTime.UtcNow;

        var lobbiesToOpen = await db.Lobbies
            .Where(l => l.Status == LobbyStatus.Closed && l.RatingOpenedAt == null)
            .ToListAsync(stoppingToken);

        if (lobbiesToOpen.Count == 0)
            return;

        _logger.LogInformation("Found {Count} lobbies to open karma window.", lobbiesToOpen.Count);

        foreach (var lobby in lobbiesToOpen)
        {
            lobby.RatingOpenedAt = now;
            _logger.LogInformation("Opened karma window for lobby {LobbyId}.", lobby.Id);
        }

        await db.SaveChangesAsync(stoppingToken);
        _logger.LogInformation("Processed {Count} karma windows.", lobbiesToOpen.Count);
    }
}
