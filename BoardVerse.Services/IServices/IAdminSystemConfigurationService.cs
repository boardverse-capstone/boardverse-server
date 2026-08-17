using BoardVerse.Core.DTOs.Admin;

namespace BoardVerse.Services.IServices
{
    public interface IAdminSystemConfigurationService
    {
        Task<IReadOnlyList<SystemConfigEntryDto>> GetAllConfigsAsync();
        Task<IReadOnlyList<SystemConfigEntryDto>> BulkUpdateConfigsAsync(SystemConfigBulkUpdateRequestDto request);
        Task<SystemConfigEntryDto> SetConfigValueAsync(string key, string value);
        Task<bool> IsBypassTimeWindowEnabledAsync();
        Task<bool> IsDemoLoosenLobbyConstraintsEnabledAsync();
        Task InvalidateCacheAsync();
    }
}
