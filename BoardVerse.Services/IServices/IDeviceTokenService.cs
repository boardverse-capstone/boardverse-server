using BoardVerse.Core.DTOs.Notification;

namespace BoardVerse.Services.IServices;

public interface IDeviceTokenService
{
    Task<DeviceTokenResponseDto> RegisterAsync(Guid userId, RegisterDeviceTokenRequestDto request);
    Task<bool> DeleteAsync(Guid userId, Guid tokenId);
}
