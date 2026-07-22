using BoardVerse.Core.DTOs.Friend;

namespace BoardVerse.Services.IServices;

public interface IFriendNoteService
{
    Task<IReadOnlyList<FriendNoteDto>> GetMyNotesAsync(Guid ownerUserId);
    Task<FriendNoteDto> UpsertNoteAsync(Guid ownerUserId, Guid friendUserId, UpsertFriendNoteDto dto);
    Task DeleteNoteAsync(Guid ownerUserId, Guid noteId);
}
