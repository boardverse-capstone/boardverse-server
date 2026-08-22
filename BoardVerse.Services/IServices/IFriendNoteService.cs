using BoardVerse.Core.DTOs.Friend;

using System.Threading;
namespace BoardVerse.Services.IServices;

public interface IFriendNoteService
{
    Task<IReadOnlyList<FriendNoteDto>> GetMyNotesAsync(Guid ownerUserId, CancellationToken cancellationToken = default);
    Task<FriendNoteDto> UpsertNoteAsync(Guid ownerUserId, Guid friendUserId, UpsertFriendNoteDto dto, CancellationToken cancellationToken = default);
    Task DeleteNoteAsync(Guid ownerUserId, Guid noteId, CancellationToken cancellationToken = default);
}
