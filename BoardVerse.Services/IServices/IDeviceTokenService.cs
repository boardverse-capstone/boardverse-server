using BoardVerse.Core.DTOs.Notification;

using System.Threading;
namespace BoardVerse.Services.IServices;

public interface IDeviceTokenService
{
    Task<DeviceTokenResponseDto> RegisterAsync(Guid userId, RegisterDeviceTokenRequestDto request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid userId, Guid tokenId, CancellationToken cancellationToken = default);
}
