using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class SystemConfigurationRepository : ISystemConfigurationRepository
    {
        private readonly BoardVerseDbContext _context;

        public SystemConfigurationRepository(BoardVerseDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<SystemConfiguration>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var list = await _context.SystemConfigurations
                .AsNoTracking()
                .OrderBy(c => c.ConfigKey)
                .ToListAsync();

            return list;
        }

        public Task<SystemConfiguration?> GetByKeyAsync(string configKey, CancellationToken cancellationToken = default) =>
            _context.SystemConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == configKey);

        public async Task UpsertAsync(IEnumerable<SystemConfiguration> configs, CancellationToken cancellationToken = default)
        {
            var utcNow = DateTime.UtcNow;
            foreach (var config in configs)
            {
                var existing = await _context.SystemConfigurations
                    .FirstOrDefaultAsync(c => c.ConfigKey == config.ConfigKey);

                if (existing == null)
                {
                    config.UpdatedAt = utcNow;
                    await _context.SystemConfigurations.AddAsync(config);
                    continue;
                }

                existing.ConfigValue = config.ConfigValue;
                existing.Description = string.IsNullOrWhiteSpace(config.Description)
                    ? existing.Description
                    : config.Description;
                existing.UpdatedAt = utcNow;
            }
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _context.SaveChangesAsync();
    }
}
