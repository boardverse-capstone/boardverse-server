using BoardVerse.Core.Data;
using BoardVerse.Core.DTOs.Bgg;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services.Bgg
{
    public class BggGameService : IBggGameService
    {
        private readonly BggApiClient _apiClient;
        private readonly BoardVerseDbContext _context;
        private readonly ILogger<BggGameService> _logger;

        public BggGameService(
            BggApiClient apiClient,
            BoardVerseDbContext context,
            ILogger<BggGameService> logger)
        {
            _apiClient = apiClient;
            _context = context;
            _logger = logger;
        }

        public Task<IReadOnlyList<BggComponentCatalogItemDto>> GetComponentCatalogAsync(CancellationToken cancellationToken = default)
        {
            var items = ComponentCatalog.GetAll()
                .Select(d => new BggComponentCatalogItemDto
                {
                    Kind = d.Kind,
                    NameEn = d.NameEn,
                    NameVi = d.NameVi,
                    Description = d.Description,
                    TypicalDefaultQuantity = d.TypicalDefaultQuantity
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<BggComponentCatalogItemDto>>(items);
        }

        public async Task<IReadOnlyList<BggSearchResultItemDto>> SearchGamesAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
                throw new BadRequestException(ApiErrorMessages.Bgg.SearchQueryTooShort);

            try
            {
                var xml = await _apiClient.SearchXmlAsync(query);
                return BggXmlParser.ParseSearch(xml)
                    .Select(i => new BggSearchResultItemDto
                    {
                        BggId = i.Id,
                        Name = i.Name,
                        YearPublished = i.YearPublished
                    })
                    .ToList();
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "BGG search failed for query '{Query}'.", query);
                throw new InternalServerErrorException(ApiErrorMessages.Bgg.SearchUpstreamUnavailable, ex);
            }
        }

        public async Task<BggGamePreviewDto> GetGamePreviewAsync(int bggId, bool curatedComponentsOnly = false, CancellationToken cancellationToken = default)
        {
            ValidatePreviewBggId(bggId);
            var thing = await FetchThingForPreviewAsync(bggId);
            return MapPreview(thing, curatedComponentsOnly);
        }

        public async Task<ImportGameFromBggResponseDto> ImportGameAsync(ImportGameFromBggRequestDto request, CancellationToken cancellationToken = default)
        {
            ValidateImportBggId(request.BggId);
            var thing = await FetchThingForImportAsync(request.BggId);
            var preview = MapPreview(thing, request.CuratedComponentsOnly);

            if (preview.Components.Count == 0)
                throw new BadRequestException(ApiErrorMessages.Bgg.ImportNoComponentsResolved(request.BggId));

            var existing = await _context.GameTemplates
                .Include(g => g.Components)
                .Include(g => g.Categories)
                .FirstOrDefaultAsync(g => g.BggId == request.BggId);

            existing ??= await _context.GameTemplates
                .Include(g => g.Components)
                .Include(g => g.Categories)
                .FirstOrDefaultAsync(g => g.Name == thing.Name);

            var syncedAt = DateTime.UtcNow;
            var created = existing == null;
            var preservedCount = 0;

            if (existing == null)
            {
                existing = new GameTemplate
                {
                    Id = Guid.NewGuid(),
                    Name = thing.Name,
                    ThumbnailUrl = thing.ThumbnailUrl,
                    Description = thing.Description,
                    MinPlayers = thing.MinPlayers,
                    MaxPlayers = thing.MaxPlayers,
                    PlayTime = thing.PlayTime,
                    BggId = thing.Id,
                    BggSyncedAt = syncedAt,
                    CreatedAt = syncedAt,
                    UpdatedAt = syncedAt,
                    Components = []
                };

                ApplySearchAliases(existing);
                ApplyComponents(existing, preview.Components);
                await ApplyCategoriesAsync(existing, thing, replaceExisting: true);
                await _context.GameTemplates.AddAsync(existing);
            }
            else if (request.OverwriteExisting)
            {
                existing.Name = thing.Name;
                existing.ThumbnailUrl = thing.ThumbnailUrl ?? existing.ThumbnailUrl;
                existing.Description = thing.Description ?? existing.Description;
                existing.MinPlayers = thing.MinPlayers;
                existing.MaxPlayers = thing.MaxPlayers;
                existing.PlayTime = thing.PlayTime;
                existing.BggId = thing.Id;
                existing.BggSyncedAt = syncedAt;
                existing.UpdatedAt = syncedAt;
                ApplySearchAliases(existing);

                var preservedCountForBranch = await MergeComponentsAsync(existing, preview.Components, syncedAt);
                preservedCount = preservedCountForBranch;
                await ApplyCategoriesAsync(existing, thing, replaceExisting: true);

                _logger.LogInformation(
                    "BGG overwrite for GameTemplate {GameId} (BggId={BggId}) preserved {PreservedCount} component(s) referenced by CafeGameComponentPenalties.",
                    existing.Id,
                    request.BggId,
                    preservedCount);
            }
            else
            {
                throw new ConflictException(ApiErrorMessages.Bgg.ImportAlreadyExists(existing.Id, request.BggId));
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Imported BGG game {BggId} ({Name}) as GameTemplate {GameId}, created={Created}.",
                request.BggId,
                thing.Name,
                existing.Id,
                created);

            return new ImportGameFromBggResponseDto
            {
                GameTemplateId = existing.Id,
                BggId = thing.Id,
                Name = existing.Name,
                Created = created,
                ComponentCount = existing.Components.Count,
                CategoryCount = existing.Categories.Count,
                PrimaryComponentSource = preview.HasCuratedComponents
                    ? GameComponentCatalogSource.CuratedCatalog
                    : preview.Components.FirstOrDefault()?.Source ?? GameComponentCatalogSource.Unknown,
                PreservedComponentCount = preservedCount
            };
        }

        /// <summary>
        /// Merge components từ BGG vào GameTemplate hiện có thay vì xóa + tạo lại.
        /// Bảo toàn ID của các component đang được <see cref="CafeGameComponentPenalty"/> tham chiếu
        /// (FK RESTRICT trên <c>FK_CafeGameComponentPenalties_GameComponentTemplates_GameCompon</c>).
        /// </summary>
        /// <remarks>
        /// Quy tắc merge:
        /// <list type="bullet">
        /// <item>Khớp theo cặp <c>(ComponentName, ComponentKind)</c> → cập nhật <c>DefaultQuantity</c> tại chỗ (giữ ID).</item>
        /// <item>Component mới từ BGG không khớp → thêm mới.</item>
        /// <item>Component cũ không có trong BGG và không có FK reference → xóa.</item>
        /// <item>Component cũ không có trong BGG nhưng đang được <see cref="CafeGameComponentPenalty"/> tham chiếu → giữ lại, log warning.</item>
        /// </list>
        /// </remarks>
        /// <returns>Số component được giữ lại do đang có FK reference từ CafeGameComponentPenalties.</returns>
        private async Task<int> MergeComponentsAsync(
            GameTemplate game,
            IReadOnlyList<BggResolvedComponentDto> newComponents,
            DateTime syncedAt)
        {
            var existingIds = game.Components.Select(c => c.Id).ToList();

            // 1. Tìm các component ID đang được CafeGameComponentPenalty tham chiếu (FK RESTRICT — không thể xóa).
            var protectedIds = new HashSet<Guid>();
            if (existingIds.Count > 0)
            {
                var referencedIds = await _context.CafeGameComponentPenalties
                    .Where(p => existingIds.Contains(p.GameComponentTemplateId))
                    .Select(p => p.GameComponentTemplateId)
                    .ToListAsync();
                protectedIds = referencedIds.ToHashSet();
            }

            // 2. Index các component hiện có theo (Name, Kind).
            var existingByKey = game.Components
                .ToDictionary(c => (Name: c.ComponentName, Kind: c.ComponentKind));

            // 3. Track các key xuất hiện trong dữ liệu BGG mới.
            var newKeys = new HashSet<(string Name, BoardGameComponentKind? Kind)>(newComponents.Count);

            foreach (var newComp in newComponents)
            {
                var key = (newComp.Name, newComp.Kind);
                newKeys.Add(key);

                if (existingByKey.TryGetValue(key, out var existingComp))
                {
                    // Cập nhật tại chỗ — giữ nguyên ID để không phá FK reference từ CafeGameComponentPenalties.
                    existingComp.DefaultQuantity = newComp.DefaultQuantity;
                }
                else
                {
                    // Component hoàn toàn mới.
                    game.Components.Add(new GameComponentTemplate
                    {
                        Id = Guid.NewGuid(),
                        GameTemplateId = game.Id,
                        ComponentName = newComp.Name,
                        ComponentKind = newComp.Kind,
                        DefaultQuantity = newComp.DefaultQuantity,
                        CreatedAt = syncedAt
                    });
                }
            }

            // 4. Xóa các component cũ không còn trong BGG, trừ khi đang có FK reference.
            var toRemove = game.Components
                .Where(c => !newKeys.Contains((c.ComponentName, c.ComponentKind)) && !protectedIds.Contains(c.Id))
                .ToList();

            foreach (var comp in toRemove)
            {
                game.Components.Remove(comp);
                _context.GameComponentTemplates.Remove(comp);
            }

            // 5. Đếm component được bảo toàn do FK reference (có trong DB nhưng không có trong BGG mới).
            var preservedCount = game.Components
                .Count(c => !newKeys.Contains((c.ComponentName, c.ComponentKind)) && protectedIds.Contains(c.Id));

            return preservedCount;
        }

        private async Task ApplyCategoriesAsync(
            GameTemplate game,
            BggThingData thing,
            bool replaceExisting)
        {
            var slugs = BggCategoryMapper.ResolveCategorySlugs(
                thing.Categories,
                thing.Mechanics,
                thing.Name);

            if (slugs.Count == 0)
                return;

            var categories = await _context.Categories
                .AsNoTracking()
                .Where(c => c.IsActive && slugs.Contains(c.Slug))
                .ToListAsync();

            if (categories.Count == 0)
                return;

            if (replaceExisting && game.Categories.Count > 0)
            {
                _context.GameTemplateCategories.RemoveRange(game.Categories.ToList());
                game.Categories.Clear();
            }

            var linkedCategoryIds = game.Categories
                .Select(gc => gc.CategoryId)
                .ToHashSet();

            var utcNow = DateTime.UtcNow;
            foreach (var category in categories)
            {
                if (linkedCategoryIds.Contains(category.Id))
                    continue;

                game.Categories.Add(new GameTemplateCategory
                {
                    GameTemplateId = game.Id,
                    CategoryId = category.Id,
                    CreatedAt = utcNow
                });
            }
        }

        private async Task<BggThingData> FetchThingForPreviewAsync(int bggId)
        {
            try
            {
                var xml = await _apiClient.GetThingXmlAsync(bggId);
                return BggXmlParser.ParseThing(xml);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "BGG preview fetch failed for {BggId}.", bggId);
                throw new NotFoundException(ApiErrorMessages.Bgg.PreviewGameNotFound(bggId));
            }
        }

        private async Task<BggThingData> FetchThingForImportAsync(int bggId)
        {
            try
            {
                var xml = await _apiClient.GetThingXmlAsync(bggId);
                return BggXmlParser.ParseThing(xml);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "BGG import fetch failed for {BggId}.", bggId);
                throw new NotFoundException(ApiErrorMessages.Bgg.ImportGameNotFound(bggId));
            }
        }

        private static BggGamePreviewDto MapPreview(BggThingData thing, bool curatedComponentsOnly)
        {
            var (components, hasCurated, _) =
                BggComponentCatalogResolver.Resolve(thing, curatedComponentsOnly);

            return new BggGamePreviewDto
            {
                BggId = thing.Id,
                Name = thing.Name,
                ThumbnailUrl = thing.ThumbnailUrl,
                Description = thing.Description,
                MinPlayers = thing.MinPlayers,
                MaxPlayers = thing.MaxPlayers,
                PlayTime = thing.PlayTime,
                Categories = thing.Categories,
                Mechanics = thing.Mechanics,
                Components = components,
                HasCuratedComponents = hasCurated,
                ComponentResolutionNote = BggComponentCatalogResolver.GetResolutionNote()
            };
        }

        private static void ApplyComponents(GameTemplate game, IEnumerable<BggResolvedComponentDto> components)
        {
            foreach (var component in components)
            {
                game.Components.Add(new GameComponentTemplate
                {
                    Id = Guid.NewGuid(),
                    GameTemplateId = game.Id,
                    ComponentName = component.Name,
                    ComponentKind = component.Kind,
                    DefaultQuantity = component.DefaultQuantity,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        private static void ApplySearchAliases(GameTemplate game)
        {
            var aliases = GameCategorySeedMap.GetSearchAliases(game.Name);
            if (!string.IsNullOrWhiteSpace(aliases))
                game.SearchAliases = aliases;
        }

        private static void ValidatePreviewBggId(int bggId)
        {
            if (bggId <= 0)
                throw new BadRequestException(ApiErrorMessages.Bgg.PreviewInvalidBggId);
        }

        private static void ValidateImportBggId(int bggId)
        {
            if (bggId <= 0)
                throw new BadRequestException(ApiErrorMessages.Bgg.ImportInvalidBggId);
        }
    }
}
