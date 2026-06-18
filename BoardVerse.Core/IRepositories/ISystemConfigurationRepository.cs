using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface ISystemConfigurationRepository
    {
        Task<IReadOnlyList<SystemConfiguration>> GetAllAsync();

        Task<SystemConfiguration?> GetByKeyAsync(string configKey);

        Task UpsertAsync(IEnumerable<SystemConfiguration> configs);

        Task SaveChangesAsync();
    }
}
