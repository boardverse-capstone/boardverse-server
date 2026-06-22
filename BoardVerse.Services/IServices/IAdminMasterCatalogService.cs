using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.DTOs.Game;

namespace BoardVerse.Services.IServices
{
    public interface IAdminMasterCatalogService
    {
        Task<List<AdminCategoryResponseDto>> GetCategoriesAsync(bool includeInactive);
        Task<AdminCategoryResponseDto> CreateCategoryAsync(AdminCreateCategoryRequestDto request);
        Task<AdminCategoryResponseDto> UpdateCategoryAsync(Guid id, AdminUpdateCategoryRequestDto request);
        Task<AdminCategoryResponseDto> DeleteCategoryAsync(Guid id);

        Task<List<GameComponentTemplateDto>> GetGameComponentsAsync(Guid gameTemplateId);
        Task<GameComponentTemplateDto> CreateGameComponentAsync(
            Guid gameTemplateId,
            AdminCreateGameComponentRequestDto request);
        Task<GameComponentTemplateDto> UpdateGameComponentAsync(
            Guid gameTemplateId,
            Guid componentId,
            AdminUpdateGameComponentRequestDto request);
        Task DeleteGameComponentAsync(Guid gameTemplateId, Guid componentId);

        Task<List<CategoryDto>> GetGameCategoriesAsync(Guid gameTemplateId);
        Task<List<CategoryDto>> SetGameCategoriesAsync(
            Guid gameTemplateId,
            AdminSetGameCategoriesRequestDto request);
    }
}
