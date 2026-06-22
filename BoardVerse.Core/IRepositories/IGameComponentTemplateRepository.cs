using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface IGameComponentTemplateRepository
    {
        Task<List<GameComponentTemplate>> GetByGameTemplateIdAsync(Guid gameTemplateId);
        Task<GameComponentTemplate?> GetByIdAndGameTemplateIdAsync(Guid componentId, Guid gameTemplateId);
        Task<bool> IsReferencedByInventoryPenaltyAsync(Guid componentId);
        Task AddAsync(GameComponentTemplate component);
        void Remove(GameComponentTemplate component);
        Task SaveChangesAsync();
    }
}
