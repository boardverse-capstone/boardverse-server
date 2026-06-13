using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Inventory;
using BoardVerse.Core.Helpers;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class CafeInventoryService : ICafeInventoryService
    {
        private readonly ICafeRepository _cafeRepository;
        private readonly ICafeInventoryRepository _inventoryRepository;
        private readonly IGameTemplateRepository _gameTemplateRepository;

        public CafeInventoryService(
            ICafeRepository cafeRepository,
            ICafeInventoryRepository inventoryRepository,
            IGameTemplateRepository gameTemplateRepository)
        {
            _cafeRepository = cafeRepository;
            _inventoryRepository = inventoryRepository;
            _gameTemplateRepository = gameTemplateRepository;
        }

        public async Task<CafeInventoryResponseDto> AddToInventoryAsync(
            Guid cafeId,
            Guid managerId,
            AddCafeInventoryRequestDto dto)
        {
            await EnsureManagerOwnsCafeAsync(cafeId, managerId);

            var gameTemplate = await _gameTemplateRepository.GetActiveByIdWithComponentsAsync(dto.GameTemplateId);
            if (gameTemplate == null)
            {
                throw new NotFoundException("Master game not found.");
            }

            var existing = await _inventoryRepository.GetByCafeAndGameTemplateAsync(cafeId, dto.GameTemplateId);
            if (existing != null)
            {
                throw new ConflictException("This game is already in the cafe inventory. Use update instead.");
            }

            var inactive = await _inventoryRepository.GetByCafeAndGameTemplateIncludingInactiveAsync(
                cafeId, dto.GameTemplateId);
            if (inactive is { IsActive: false })
            {
                throw new ConflictException(
                    "This game was previously removed from inventory. Use restore instead.");
            }

            var now = DateTime.UtcNow;
            var inventoryId = Guid.NewGuid();
            var inventory = new CafeGameInventory
            {
                Id = inventoryId,
                CafeId = cafeId,
                GameTemplateId = dto.GameTemplateId,
                BoxQuantity = dto.BoxQuantity,
                Status = dto.Status,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true,
                ComponentPenalties = BuildPenalties(
                    inventoryId,
                    gameTemplate.Components,
                    dto.ComponentPenalties,
                    now)
            };

            await _inventoryRepository.AddAsync(inventory);
            await _inventoryRepository.SaveChangesAsync();

            var saved = await _inventoryRepository.GetByIdWithDetailsAsync(inventory.Id);
            return MapToFullDto(saved!);
        }

        public async Task<object> GetInventoryForViewerAsync(
            Guid cafeId,
            Guid? viewerId,
            string? viewerRole,
            GetCafeInventoryQuery query)
        {
            await EnsureCafeIsBrowsableAsync(cafeId);
            var canViewFull = await CanViewFullInventoryAsync(cafeId, viewerId, viewerRole);

            var result = await _inventoryRepository.GetPagedByCafeAsync(cafeId, query);

            if (canViewFull)
            {
                return new PaginatedResponse<CafeInventoryResponseDto>
                {
                    Data = result.Data.Select(MapToFullDto).ToList(),
                    Meta = result.Meta
                };
            }

            return new PaginatedResponse<CafeInventoryBrowseDto>
            {
                Data = result.Data.Select(MapToBrowseDto).ToList(),
                Meta = result.Meta
            };
        }

        public async Task<PaginatedResponse<CafeInventoryResponseDto>> GetDeletedInventoryAsync(
            Guid cafeId,
            Guid managerId,
            GetCafeInventoryQuery query)
        {
            await EnsureManagerOwnsCafeAsync(cafeId, managerId);

            var result = await _inventoryRepository.GetPagedByCafeAsync(cafeId, query, deletedOnly: true);

            return new PaginatedResponse<CafeInventoryResponseDto>
            {
                Data = result.Data.Select(MapToFullDto).ToList(),
                Meta = result.Meta
            };
        }

        public async Task<object> GetInventoryItemForViewerAsync(
            Guid cafeId,
            Guid inventoryId,
            Guid? viewerId,
            string? viewerRole)
        {
            await EnsureCafeIsBrowsableAsync(cafeId);
            var canViewFull = await CanViewFullInventoryAsync(cafeId, viewerId, viewerRole);

            var inventory = await _inventoryRepository.GetByIdWithDetailsAsync(inventoryId);
            if (inventory == null || inventory.CafeId != cafeId)
            {
                throw new NotFoundException("Inventory item not found.");
            }

            return canViewFull ? MapToFullDto(inventory) : MapToBrowseDto(inventory);
        }

        public async Task<CafeInventoryResponseDto> UpdateInventoryAsync(
            Guid cafeId,
            Guid inventoryId,
            Guid managerId,
            UpdateCafeInventoryRequestDto dto)
        {
            await EnsureManagerOwnsCafeAsync(cafeId, managerId);

            var inventory = await _inventoryRepository.GetByIdWithDetailsAsync(inventoryId);
            if (inventory == null || inventory.CafeId != cafeId)
            {
                throw new NotFoundException("Inventory item not found.");
            }

            if (dto.BoxQuantity.HasValue)
            {
                inventory.BoxQuantity = dto.BoxQuantity.Value;
            }

            if (dto.Status.HasValue)
            {
                inventory.Status = dto.Status.Value;
            }

            if (dto.ComponentPenalties != null)
            {
                var componentIds = inventory.ComponentPenalties
                    .Select(p => p.GameComponentTemplateId)
                    .ToHashSet();

                foreach (var request in dto.ComponentPenalties)
                {
                    if (!componentIds.Contains(request.GameComponentTemplateId))
                    {
                        throw new BadRequestException(
                            $"Component {request.GameComponentTemplateId} does not belong to this game.");
                    }

                    var penalty = inventory.ComponentPenalties
                        .First(p => p.GameComponentTemplateId == request.GameComponentTemplateId);
                    penalty.PenaltyFee = request.PenaltyFee;
                    penalty.UpdatedAt = DateTime.UtcNow;
                }
            }

            inventory.UpdatedAt = DateTime.UtcNow;
            await _inventoryRepository.SaveChangesAsync();

            var updated = await _inventoryRepository.GetByIdWithDetailsAsync(inventoryId);
            return MapToFullDto(updated!);
        }

        public async Task<CafeInventoryResponseDto> RestoreInventoryAsync(
            Guid cafeId,
            Guid inventoryId,
            Guid managerId)
        {
            await EnsureManagerOwnsCafeAsync(cafeId, managerId);

            var inventory = await _inventoryRepository.GetByIdWithDetailsIncludingInactiveAsync(inventoryId);
            if (inventory == null || inventory.CafeId != cafeId)
            {
                throw new NotFoundException("Inventory item not found.");
            }

            if (inventory.IsActive)
            {
                throw new BadRequestException("Inventory item is already active.");
            }

            var activeDuplicate = await _inventoryRepository.GetByCafeAndGameTemplateAsync(
                cafeId, inventory.GameTemplateId);
            if (activeDuplicate != null)
            {
                throw new ConflictException(
                    "An active inventory entry for this game already exists.");
            }

            inventory.IsActive = true;
            inventory.UpdatedAt = DateTime.UtcNow;
            await _inventoryRepository.SaveChangesAsync();

            var restored = await _inventoryRepository.GetByIdWithDetailsAsync(inventoryId);
            return MapToFullDto(restored!);
        }

        public async Task<CafeInventoryResponseDto> SyncPenaltiesAsync(
            Guid cafeId,
            Guid inventoryId,
            Guid managerId)
        {
            await EnsureManagerOwnsCafeAsync(cafeId, managerId);

            var inventory = await _inventoryRepository.GetByIdWithDetailsIncludingInactiveAsync(inventoryId);
            if (inventory == null || inventory.CafeId != cafeId || !inventory.IsActive)
            {
                throw new NotFoundException("Inventory item not found.");
            }

            var gameTemplate = await _gameTemplateRepository.GetByIdWithComponentsAsync(inventory.GameTemplateId);
            if (gameTemplate == null)
            {
                throw new NotFoundException("Master game not found.");
            }

            var existingComponentIds = inventory.ComponentPenalties
                .Select(p => p.GameComponentTemplateId)
                .ToHashSet();
            var now = DateTime.UtcNow;

            foreach (var component in gameTemplate.Components)
            {
                if (existingComponentIds.Contains(component.Id))
                {
                    continue;
                }

                inventory.ComponentPenalties.Add(new CafeGameComponentPenalty
                {
                    Id = Guid.NewGuid(),
                    CafeGameInventoryId = inventory.Id,
                    GameComponentTemplateId = component.Id,
                    PenaltyFee = 0m,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            inventory.UpdatedAt = now;
            await _inventoryRepository.SaveChangesAsync();

            var updated = await _inventoryRepository.GetByIdWithDetailsAsync(inventoryId);
            return MapToFullDto(updated!);
        }

        public async Task RemoveFromInventoryAsync(Guid cafeId, Guid inventoryId, Guid managerId)
        {
            await EnsureManagerOwnsCafeAsync(cafeId, managerId);

            var inventory = await _inventoryRepository.GetByIdWithDetailsAsync(inventoryId);
            if (inventory == null || inventory.CafeId != cafeId)
            {
                throw new NotFoundException("Inventory item not found.");
            }

            inventory.IsActive = false;
            inventory.UpdatedAt = DateTime.UtcNow;
            await _inventoryRepository.SaveChangesAsync();
        }

        private async Task EnsureCafeIsBrowsableAsync(Guid cafeId)
        {
            var cafe = await _cafeRepository.GetActiveByIdAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException("Cafe not found.");
            }
        }

        private async Task<bool> CanViewFullInventoryAsync(Guid cafeId, Guid? viewerId, string? viewerRole)
        {
            if (!viewerId.HasValue || string.IsNullOrWhiteSpace(viewerRole))
            {
                return false;
            }

            var cafe = await _cafeRepository.GetByIdAsync(cafeId);
            if (cafe == null)
            {
                return false;
            }

            if (viewerRole == UserRole.Manager.ToString() && cafe.ManagerId == viewerId.Value)
            {
                return true;
            }

            if (viewerRole == UserRole.CafeStaff.ToString())
            {
                return await _cafeRepository.IsStaffMemberExistsAsync(cafeId, viewerId.Value);
            }

            return false;
        }

        private async Task EnsureManagerOwnsCafeAsync(Guid cafeId, Guid managerId)
        {
            var cafe = await _cafeRepository.GetByIdAsync(cafeId);
            if (cafe == null)
            {
                throw new NotFoundException("Cafe not found.");
            }

            if (cafe.ManagerId != managerId)
            {
                throw new ForbiddenException("You are not authorized to manage inventory for this cafe.");
            }
        }

        private static List<CafeGameComponentPenalty> BuildPenalties(
            Guid inventoryId,
            ICollection<GameComponentTemplate> components,
            List<ComponentPenaltyRequestDto>? requestedPenalties,
            DateTime now)
        {
            var feeLookup = requestedPenalties?
                .ToDictionary(p => p.GameComponentTemplateId, p => p.PenaltyFee)
                ?? [];

            if (requestedPenalties != null)
            {
                var validIds = components.Select(c => c.Id).ToHashSet();
                var invalid = requestedPenalties
                    .Where(p => !validIds.Contains(p.GameComponentTemplateId))
                    .Select(p => p.GameComponentTemplateId)
                    .ToList();

                if (invalid.Count > 0)
                {
                    throw new BadRequestException("One or more component IDs do not belong to the selected game.");
                }
            }

            return components.Select(component => new CafeGameComponentPenalty
            {
                Id = Guid.NewGuid(),
                CafeGameInventoryId = inventoryId,
                GameComponentTemplateId = component.Id,
                PenaltyFee = feeLookup.TryGetValue(component.Id, out var fee) ? fee : 0m,
                CreatedAt = now,
                UpdatedAt = now
            }).ToList();
        }

        private static CafeInventoryBrowseDto MapToBrowseDto(CafeGameInventory inventory) => new()
        {
            Id = inventory.Id,
            GameTemplateId = inventory.GameTemplateId,
            GameName = inventory.GameTemplate?.Name ?? string.Empty,
            ThumbnailUrl = inventory.GameTemplate?.ThumbnailUrl,
            Description = inventory.GameTemplate?.Description,
            MinPlayers = inventory.GameTemplate?.MinPlayers ?? 0,
            MaxPlayers = inventory.GameTemplate?.MaxPlayers ?? 0,
            PlayTime = inventory.GameTemplate?.PlayTime ?? 0,
            BoxQuantity = inventory.BoxQuantity,
            Status = inventory.Status,
            Categories = GameCatalogMapper.MapCategories(inventory.GameTemplate),
            Components = GameCatalogMapper.MapComponents(inventory.GameTemplate?.Components)
        };

        private static CafeInventoryResponseDto MapToFullDto(CafeGameInventory inventory) => new()
        {
            Id = inventory.Id,
            CafeId = inventory.CafeId,
            GameTemplateId = inventory.GameTemplateId,
            GameName = inventory.GameTemplate?.Name ?? string.Empty,
            ThumbnailUrl = inventory.GameTemplate?.ThumbnailUrl,
            Description = inventory.GameTemplate?.Description,
            MinPlayers = inventory.GameTemplate?.MinPlayers ?? 0,
            MaxPlayers = inventory.GameTemplate?.MaxPlayers ?? 0,
            PlayTime = inventory.GameTemplate?.PlayTime ?? 0,
            BoxQuantity = inventory.BoxQuantity,
            Status = inventory.Status,
            CreatedAt = inventory.CreatedAt,
            UpdatedAt = inventory.UpdatedAt,
            IsActive = inventory.IsActive,
            ComponentPenalties = inventory.ComponentPenalties
                .OrderBy(p => p.GameComponentTemplate?.ComponentName)
                .Select(p => new ComponentPenaltyResponseDto
                {
                    Id = p.Id,
                    GameComponentTemplateId = p.GameComponentTemplateId,
                    ComponentName = p.GameComponentTemplate?.ComponentName ?? string.Empty,
                    DefaultQuantity = p.GameComponentTemplate?.DefaultQuantity ?? 0,
                    PenaltyFee = p.PenaltyFee
                })
                .ToList()
        };
    }
}
