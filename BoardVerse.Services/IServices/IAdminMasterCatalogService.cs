using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.DTOs.Game;

using System.Threading;
namespace BoardVerse.Services.IServices
{
    public interface IAdminMasterCatalogService
    {
        Task<List<AdminCategoryResponseDto>> GetCategoriesAsync(bool includeInactive, CancellationToken cancellationToken = default);
        Task<AdminCategoryResponseDto> CreateCategoryAsync(AdminCreateCategoryRequestDto request, CancellationToken cancellationToken = default);
        Task<AdminCategoryResponseDto> UpdateCategoryAsync(Guid id, AdminUpdateCategoryRequestDto request, CancellationToken cancellationToken = default);
        Task<AdminCategoryResponseDto> DeleteCategoryAsync(Guid id, CancellationToken cancellationToken = default);

        Task<List<GameComponentTemplateDto>> GetGameComponentsAsync(Guid gameTemplateId, CancellationToken cancellationToken = default);
        Task<GameComponentTemplateDto> CreateGameComponentAsync(
            Guid gameTemplateId,
            AdminCreateGameComponentRequestDto request, CancellationToken cancellationToken = default);
        Task<GameComponentTemplateDto> UpdateGameComponentAsync(
            Guid gameTemplateId,
            Guid componentId,
            AdminUpdateGameComponentRequestDto request, CancellationToken cancellationToken = default);
        Task DeleteGameComponentAsync(Guid gameTemplateId, Guid componentId);

        Task<List<CategoryDto>> GetGameCategoriesAsync(Guid gameTemplateId);
        Task<List<CategoryDto>> SetGameCategoriesAsync(
            Guid gameTemplateId,
            AdminSetGameCategoriesRequestDto request, CancellationToken cancellationToken = default);

        Task<AdminBoardGameResponseDto> UpdateBoardGameAsync(
            Guid gameTemplateId,
            AdminUpdateBoardGameRequestDto request, CancellationToken cancellationToken = default);
        Task<AdminBoardGameResponseDto> UpdateThumbnailAsync(
            Guid gameTemplateId,
            AdminUpdateThumbnailRequestDto request, CancellationToken cancellationToken = default);
    }
}
