using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Quản lý FCM device tokens cho push notification (mobile gap #9, #13).
/// Mobile gọi POST/DELETE qua <c>DeviceTokenController</c>;
/// <c>FcmPushNotificationService</c> đọc tokens qua
/// <see cref="GetActiveTokensByUserIdsAsync"/> khi broadcast.
/// </summary>
public interface IDeviceTokenRepository
{
    Task AddAsync(DeviceToken token);
    Task<DeviceToken?> GetByTokenAsync(string token);
    Task<IReadOnlyList<DeviceToken>> GetByUserIdAsync(Guid userId);
    Task<IReadOnlyList<DeviceToken>> GetActiveTokensByUserIdsAsync(IReadOnlyCollection<Guid> userIds);
    Task UpdateAsync(DeviceToken token);
    Task DeleteAsync(Guid id);
    Task SaveChangesAsync();
}
