using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class AdminMasterCatalogService : IAdminMasterCatalogService
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly IGameComponentTemplateRepository _componentRepository;
        private readonly IGameTemplateRepository _gameTemplateRepository;

        public AdminMasterCatalogService(
            ICategoryRepository categoryRepository,
            IGameComponentTemplateRepository componentRepository,
            IGameTemplateRepository gameTemplateRepository)
        {
            _categoryRepository = categoryRepository;
            _componentRepository = componentRepository;
            _gameTemplateRepository = gameTemplateRepository;
        }

        public async Task<List<AdminCategoryResponseDto>> GetCategoriesAsync(bool includeInactive)
        {
            var categories = await _categoryRepository.GetAllAsync(includeInactive);
            return categories.Select(MapCategory).ToList();
        }

        public async Task<AdminCategoryResponseDto> CreateCategoryAsync(AdminCreateCategoryRequestDto request)
        {
            var name = request.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new BadRequestException(ApiErrorMessages.AdminCatalog.CategoryNameRequired);

            var slug = ResolveSlug(request.Slug, name);
            if (await _categoryRepository.SlugExistsAsync(slug))
                throw new ConflictException(ApiErrorMessages.AdminCatalog.CategorySlugTaken(slug));

            var utcNow = DateTime.UtcNow;
            var category = new Category
            {
                Id = Guid.NewGuid(),
                Name = name,
                Slug = slug,
                Description = request.Description?.Trim(),
                SortOrder = request.SortOrder,
                IsActive = true,
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            };

            await _categoryRepository.AddAsync(category);
            await _categoryRepository.SaveChangesAsync();
            return MapCategory(category);
        }

        public async Task<AdminCategoryResponseDto> UpdateCategoryAsync(
            Guid id,
            AdminUpdateCategoryRequestDto request)
        {
            var category = await _categoryRepository.GetByIdAsync(id);
            if (category == null)
                throw new NotFoundException(ApiErrorMessages.AdminCatalog.CategoryNotFound(id));

            if (!string.IsNullOrWhiteSpace(request.Name))
                category.Name = request.Name.Trim();

            if (request.Slug != null)
            {
                var slug = ResolveSlug(request.Slug, category.Name);
                if (await _categoryRepository.SlugExistsAsync(slug, id))
                    throw new ConflictException(ApiErrorMessages.AdminCatalog.CategorySlugTaken(slug));
                category.Slug = slug;
            }

            if (request.Description != null)
                category.Description = string.IsNullOrWhiteSpace(request.Description)
                    ? null
                    : request.Description.Trim();

            if (request.SortOrder.HasValue)
                category.SortOrder = request.SortOrder.Value;

            if (request.IsActive.HasValue)
                category.IsActive = request.IsActive.Value;

            category.UpdatedAt = DateTime.UtcNow;
            await _categoryRepository.SaveChangesAsync();
            return MapCategory(category);
        }

        public async Task<AdminCategoryResponseDto> DeleteCategoryAsync(Guid id)
        {
            var category = await _categoryRepository.GetByIdAsync(id);
            if (category == null)
                throw new NotFoundException(ApiErrorMessages.AdminCatalog.CategoryNotFound(id));

            category.IsActive = false;
            category.UpdatedAt = DateTime.UtcNow;
            await _categoryRepository.SaveChangesAsync();
            return MapCategory(category);
        }

        public async Task<List<GameComponentTemplateDto>> GetGameComponentsAsync(Guid gameTemplateId)
        {
            await EnsureGameTemplateExistsAsync(gameTemplateId);
            var components = await _componentRepository.GetByGameTemplateIdAsync(gameTemplateId);
            return components.Select(MapComponent).ToList();
        }

        public async Task<GameComponentTemplateDto> CreateGameComponentAsync(
            Guid gameTemplateId,
            AdminCreateGameComponentRequestDto request)
        {
            await EnsureGameTemplateExistsAsync(gameTemplateId);
            ValidateComponentKind(request.ComponentKind);

            var component = new GameComponentTemplate
            {
                Id = Guid.NewGuid(),
                GameTemplateId = gameTemplateId,
                ComponentName = request.ComponentName.Trim(),
                ComponentKind = request.ComponentKind
                    ?? ComponentCatalog.ResolveKindFromName(request.ComponentName),
                DefaultQuantity = request.DefaultQuantity,
                CreatedAt = DateTime.UtcNow
            };

            await _componentRepository.AddAsync(component);
            await _componentRepository.SaveChangesAsync();
            return MapComponent(component);
        }

        public async Task<GameComponentTemplateDto> UpdateGameComponentAsync(
            Guid gameTemplateId,
            Guid componentId,
            AdminUpdateGameComponentRequestDto request)
        {
            await EnsureGameTemplateExistsAsync(gameTemplateId);
            ValidateComponentKind(request.ComponentKind);

            var component = await _componentRepository.GetByIdAndGameTemplateIdAsync(componentId, gameTemplateId);
            if (component == null)
                throw new NotFoundException(
                    ApiErrorMessages.AdminCatalog.ComponentNotFound(gameTemplateId, componentId));

            if (!string.IsNullOrWhiteSpace(request.ComponentName))
                component.ComponentName = request.ComponentName.Trim();

            if (request.ComponentKind.HasValue)
                component.ComponentKind = request.ComponentKind;

            if (request.DefaultQuantity.HasValue)
                component.DefaultQuantity = request.DefaultQuantity.Value;

            await _componentRepository.SaveChangesAsync();
            return MapComponent(component);
        }

        public async Task DeleteGameComponentAsync(Guid gameTemplateId, Guid componentId)
        {
            await EnsureGameTemplateExistsAsync(gameTemplateId);

            var component = await _componentRepository.GetByIdAndGameTemplateIdAsync(componentId, gameTemplateId);
            if (component == null)
                throw new NotFoundException(
                    ApiErrorMessages.AdminCatalog.ComponentNotFound(gameTemplateId, componentId));

            if (await _componentRepository.IsReferencedByInventoryPenaltyAsync(componentId))
                throw new ConflictException(ApiErrorMessages.AdminCatalog.ComponentInUse(componentId));

            _componentRepository.Remove(component);
            await _componentRepository.SaveChangesAsync();
        }

        public async Task<List<CategoryDto>> GetGameCategoriesAsync(Guid gameTemplateId)
        {
            var game = await _gameTemplateRepository.GetByIdWithComponentsAsync(gameTemplateId);
            if (game == null)
                throw new NotFoundException(ApiErrorMessages.AdminCatalog.GameTemplateNotFound(gameTemplateId));

            return game.Categories
                .Where(gc => gc.Category.IsActive)
                .OrderBy(gc => gc.Category.SortOrder)
                .Select(gc => MapCategoryDto(gc.Category))
                .ToList();
        }

        public async Task<List<CategoryDto>> SetGameCategoriesAsync(
            Guid gameTemplateId,
            AdminSetGameCategoriesRequestDto request)
        {
            var game = await _gameTemplateRepository.GetByIdWithCategoriesForUpdateAsync(gameTemplateId);
            if (game == null)
                throw new NotFoundException(ApiErrorMessages.AdminCatalog.GameTemplateNotFound(gameTemplateId));

            var distinctIds = request.CategoryIds.Distinct().ToList();
            if (distinctIds.Count > 0)
            {
                var foundCount = await _categoryRepository.CountByIdsAsync(distinctIds, activeOnly: true);
                if (foundCount != distinctIds.Count)
                {
                    throw new BadRequestException(
                        ApiErrorMessages.AdminCatalog.CategoriesNotFound(distinctIds));
                }
            }

            game.Categories.Clear();
            var utcNow = DateTime.UtcNow;
            foreach (var categoryId in distinctIds)
            {
                game.Categories.Add(new GameTemplateCategory
                {
                    GameTemplateId = gameTemplateId,
                    CategoryId = categoryId,
                    CreatedAt = utcNow
                });
            }

            game.UpdatedAt = utcNow;
            await _gameTemplateRepository.SaveChangesAsync();
            return await GetGameCategoriesAsync(gameTemplateId);
        }

        private async Task EnsureGameTemplateExistsAsync(Guid gameTemplateId)
        {
            if (!await _gameTemplateRepository.ExistsAsync(gameTemplateId))
                throw new NotFoundException(ApiErrorMessages.AdminCatalog.GameTemplateNotFound(gameTemplateId));
        }

        private static string ResolveSlug(string? requestedSlug, string name)
        {
            var slug = string.IsNullOrWhiteSpace(requestedSlug)
                ? VietnameseTextNormalizer.ToSlug(name)
                : VietnameseTextNormalizer.ToSlug(requestedSlug);

            if (string.IsNullOrWhiteSpace(slug))
                throw new BadRequestException(ApiErrorMessages.AdminCatalog.CategorySlugRequired);

            return slug;
        }

        private static void ValidateComponentKind(BoardGameComponentKind? kind)
        {
            if (!kind.HasValue)
                return;

            if (!Enum.IsDefined(kind.Value))
                throw new BadRequestException(ApiErrorMessages.AdminCatalog.InvalidComponentKind((int)kind.Value));
        }

        private static AdminCategoryResponseDto MapCategory(Category category) =>
            new()
            {
                Id = category.Id,
                Name = category.Name,
                Slug = category.Slug,
                Description = category.Description,
                SortOrder = category.SortOrder,
                IsActive = category.IsActive,
                CreatedAt = category.CreatedAt,
                UpdatedAt = category.UpdatedAt
            };

        private static CategoryDto MapCategoryDto(Category category) =>
            new()
            {
                Id = category.Id,
                Name = category.Name,
                Slug = category.Slug,
                Description = category.Description,
                SortOrder = category.SortOrder
            };

        private static GameComponentTemplateDto MapComponent(GameComponentTemplate component) =>
            new()
            {
                Id = component.Id,
                GameTemplateId = component.GameTemplateId,
                ComponentName = component.ComponentName,
                ComponentKind = component.ComponentKind,
                DefaultQuantity = component.DefaultQuantity,
                CreatedAt = component.CreatedAt
            };
    }
}
