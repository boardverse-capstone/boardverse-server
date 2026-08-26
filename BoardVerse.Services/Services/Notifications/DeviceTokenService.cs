using BoardVerse.Core.DTOs.Notification;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services.Notifications;

public class DeviceTokenService : IDeviceTokenService
{
    private static readonly HashSet<string> AllowedPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "android", "ios", "web"
    };

    private readonly IDeviceTokenRepository _repository;
    private readonly ILogger<DeviceTokenService> _logger;

    public DeviceTokenService(
        IDeviceTokenRepository repository,
        ILogger<DeviceTokenService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<DeviceTokenResponseDto> RegisterAsync(
        Guid userId,
        RegisterDeviceTokenRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!AllowedPlatforms.Contains(request.Platform))
        {
            throw new BadRequestException(ApiErrorMessages.Notification.PlatformInvalid);
        }

        // Idempotent: cùng token (có thể rotate sau khi refresh) → update.
        var existing = await _repository.GetByTokenAsync(request.Token);
        if (existing != null)
        {
            // Token rotated sang user khác (vd: device reused) → reassign.
            existing.UserId = userId;
            existing.Platform = request.Platform.ToLowerInvariant();
            existing.AppVersion = request.AppVersion;
            existing.DeviceModel = request.DeviceModel;
            existing.LastSeenAt = DateTime.UtcNow;
            existing.IsInvalidated = false;
            await _repository.UpdateAsync(existing);
            await _repository.SaveChangesAsync();
            _logger.LogInformation(
                "Re-registered FCM token id={TokenId} for user={UserId}", existing.Id, userId);
            return DeviceTokenResponseDto.FromEntity(existing);
        }

        var token = new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = request.Token,
            Platform = request.Platform.ToLowerInvariant(),
            AppVersion = request.AppVersion,
            DeviceModel = request.DeviceModel,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        await _repository.AddAsync(token);
        await _repository.SaveChangesAsync();
        _logger.LogInformation(
            "Registered new FCM token id={TokenId} for user={UserId}", token.Id, userId);
        return DeviceTokenResponseDto.FromEntity(token);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid tokenId, CancellationToken cancellationToken = default)
    {
        var tokens = await _repository.GetByUserIdAsync(userId);
        var existing = tokens.FirstOrDefault(t => t.Id == tokenId);
        if (existing == null)
        {
            throw new NotFoundException(ApiErrorMessages.Notification.DeviceTokenNotFound);
        }
        await _repository.DeleteAsync(tokenId);
        await _repository.SaveChangesAsync();
        _logger.LogInformation(
            "Deleted FCM token id={TokenId} for user={UserId}", tokenId, userId);
        return true;
    }
}
