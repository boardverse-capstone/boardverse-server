using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Services.Services;

/// <inheritdoc cref="IPlayerRiskQueryService"/>
public class PlayerRiskQueryService : IPlayerRiskQueryService
{
    private readonly IWalletRepository _walletRepository;
    private readonly BoardVerseDbContext _db;

    public PlayerRiskQueryService(
        IWalletRepository walletRepository,
        BoardVerseDbContext db)
    {
        _walletRepository = walletRepository;
        _db = db;
    }

    public async Task<PlayerRiskDetailDto> GetPlayerRiskDetailAsync(Guid userId, CancellationToken ct = default)
    {
        var wallet = await _walletRepository.GetWalletWithUserAsync(userId);
        if (wallet == null)
        {
            throw new NotFoundException(ApiErrorMessages.AdminModeration.WalletNotFound(userId));
        }

        var signals = await LoadLatestSignalsAsync(userId, ct);
        var actionCount = await _db.PlayerActionHistories.CountAsync(p => p.UserId == userId, ct);

        return new PlayerRiskDetailDto
        {
            UserId = wallet.UserId,
            Username = wallet.User?.Username ?? string.Empty,
            RiskScore = wallet.RiskScore,
            RiskLevel = wallet.RiskLevel.ToString().ToLowerInvariant(),
            RiskMultiplier = wallet.RiskMultiplier,
            AccountStatus = wallet.AccountStatus.ToString().ToLowerInvariant(),
            IsCoolingOff = wallet.IsCoolingOff,
            CoolingOffExpiresAt = wallet.CoolingOffExpiresAt,
            Signals = signals,
            ActionHistoryCount = actionCount,
            LastUpdated = wallet.UpdatedAt
        };
    }

    /// <summary>
    /// BR-RISK-01 — Load signals snapshot từ PlayerActionHistory gần nhất có chứa signals JSON.
    /// </summary>
    private async Task<Dictionary<string, int>> LoadLatestSignalsAsync(Guid userId, CancellationToken ct)
    {
        // Metadata là jsonb column — không thể dùng `.Contains("...")` (EF Core sẽ generate
        // `jsonb ~~ unknown` operator không tồn tại trên Postgres). Cast sang text rồi ILIKE.
        var latestRiskAction = await _db.PlayerActionHistories
            .Where(p => p.UserId == userId && p.Metadata != null)
            .OrderByDescending(p => p.CreatedAt)
            .Take(50) // giới hạn candidate set trước khi filter JSON ở client
            .ToListAsync(ct);

        var withSignals = latestRiskAction
            .Where(p => p.Metadata != null
                && p.Metadata.Contains("\"signals\"", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefault();

        if (withSignals?.Metadata == null)
        {
            return new Dictionary<string, int>();
        }

        try
        {
            var json = System.Text.Json.JsonDocument.Parse(withSignals.Metadata);
            if (json.RootElement.TryGetProperty("signals", out var signalsElement))
            {
                var dict = new Dictionary<string, int>();
                foreach (var prop in signalsElement.EnumerateObject())
                {
                    if (prop.Value.TryGetInt32(out var value))
                    {
                        dict[prop.Name] = value;
                    }
                }
                return dict;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed JSON → return empty.
        }

        return new Dictionary<string, int>();
    }
}
