using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface ISystemConfigurationRepository
    {
        Task<IReadOnlyList<SystemConfiguration>> GetAllAsync(CancellationToken cancellationToken = default);

        Task<SystemConfiguration?> GetByKeyAsync(string configKey, CancellationToken cancellationToken = default);

        Task UpsertAsync(IEnumerable<SystemConfiguration> configs, CancellationToken cancellationToken = default);

        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
