using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class GameTemplateService : IGameTemplateService
    {
        private readonly IGameTemplateRepository _gameTemplateRepository;

        public GameTemplateService(IGameTemplateRepository gameTemplateRepository)
        {
            _gameTemplateRepository = gameTemplateRepository;
        }

        public async Task<PaginatedResponse<MasterGameResponseDto>> GetMasterGamesAsync(GetMasterGamesQuery query)
        {
            var result = await _gameTemplateRepository.GetPagedAsync(query);

            var dtoData = result.Data.Select(game => new MasterGameResponseDto
            {
                Id = game.Id,
                Name = game.Name,
                ThumbnailUrl = game.ThumbnailUrl,
                Description = game.Description,
                MinPlayers = game.MinPlayers,
                MaxPlayers = game.MaxPlayers,
                PlayTime = game.PlayTime,
                CreatedAt = game.CreatedAt,
                UpdatedAt = game.UpdatedAt,
                Components = game.Components.Select(c => new GameComponentTemplateDto
                {
                    Id = c.Id,
                    GameTemplateId = c.GameTemplateId,
                    ComponentName = c.ComponentName,
                    DefaultQuantity = c.DefaultQuantity,
                    CreatedAt = c.CreatedAt
                }).ToList()
            }).ToList();

            return new PaginatedResponse<MasterGameResponseDto>
            {
                Data = dtoData,
                Meta = result.Meta
            };
        }
    }
}
