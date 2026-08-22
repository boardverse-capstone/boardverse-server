using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Quản lý FCM device tokens cho push notification (mobile gap #9, #13).
/// Mobile gọi POST/DELETE qua <c>DeviceTokenController</c>;
/// <c>FcmPushNotificationService</c> đọc tokens qua
/// <see cref="GetActiveTokensByUserIdsAsync"/> khi broadcast.
/// </summary>
public interface IDeviceTokenRepository
{
    Task AddAsync(DeviceToken token, CancellationToken cancellationToken = default);
    Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeviceToken>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeviceToken>> GetActiveTokensByUserIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);
    Task UpdateAsync(DeviceToken token, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> DeleteStaleTokensAsync(DateTime staleCutoff, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
