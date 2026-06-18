using BoardVerse.Core.DTOs.Admin;

namespace BoardVerse.Services.IServices
{
    public interface IAdminSystemConfigurationService
    {
        Task<IReadOnlyList<SystemConfigEntryDto>> GetAllConfigsAsync();
        Task<IReadOnlyList<SystemConfigEntryDto>> BulkUpdateConfigsAsync(SystemConfigBulkUpdateRequestDto request);
    }
}
