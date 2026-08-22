using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface IGameComponentTemplateRepository
    {
        Task<List<GameComponentTemplate>> GetByGameTemplateIdAsync(Guid gameTemplateId, CancellationToken cancellationToken = default);
        Task<GameComponentTemplate?> GetByIdAndGameTemplateIdAsync(Guid componentId, Guid gameTemplateId, CancellationToken cancellationToken = default);
        Task<bool> IsReferencedByInventoryPenaltyAsync(Guid componentId, CancellationToken cancellationToken = default);
        Task AddAsync(GameComponentTemplate component, CancellationToken cancellationToken = default);
        void Remove(GameComponentTemplate component);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
