using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

public interface IPlayerBoardGameSaveRepository
{
    Task<PlayerBoardGameSave?> GetByUserAndGameAsync(
        Guid userId,
        Guid gameTemplateId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlayerBoardGameSave>> GetByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetSavedGameTemplateIdsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid userId,
        Guid gameTemplateId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        PlayerBoardGameSave save,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid userId,
        Guid gameTemplateId,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
