using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

public interface IFriendNoteRepository
{
    Task<FriendNote?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<FriendNote?> GetByOwnerAndFriendAsync(Guid ownerUserId, Guid friendUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FriendNote>> GetByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default);
    Task AddAsync(FriendNote note, CancellationToken cancellationToken = default);
    void Update(FriendNote note);
    void Remove(FriendNote note);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
