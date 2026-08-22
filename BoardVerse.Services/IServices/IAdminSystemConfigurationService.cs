using BoardVerse.Core.DTOs.Admin;

using System.Threading;
namespace BoardVerse.Services.IServices
{
    public interface IAdminSystemConfigurationService
    {
        Task<IReadOnlyList<SystemConfigEntryDto>> GetAllConfigsAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SystemConfigEntryDto>> BulkUpdateConfigsAsync(SystemConfigBulkUpdateRequestDto request, CancellationToken cancellationToken = default);
        Task<SystemConfigEntryDto> SetConfigValueAsync(string key, string value, CancellationToken cancellationToken = default);
        Task<bool> IsBypassTimeWindowEnabledAsync(CancellationToken cancellationToken = default);
        Task<bool> IsDemoLoosenLobbyConstraintsEnabledAsync(CancellationToken cancellationToken = default);
        Task InvalidateCacheAsync(CancellationToken cancellationToken = default);
    }
}
