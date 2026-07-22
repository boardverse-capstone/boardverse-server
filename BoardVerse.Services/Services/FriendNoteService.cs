using BoardVerse.Core.DTOs.Friend;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services;

public class FriendNoteService : IFriendNoteService
{
    private readonly IFriendNoteRepository _noteRepository;
    private readonly IUserManagementRepository _userRepository;

    public FriendNoteService(
        IFriendNoteRepository noteRepository,
        IUserManagementRepository userRepository)
    {
        _noteRepository = noteRepository;
        _userRepository = userRepository;
    }

    public async Task<IReadOnlyList<FriendNoteDto>> GetMyNotesAsync(Guid ownerUserId)
    {
        var notes = await _noteRepository.GetByOwnerAsync(ownerUserId);
        return notes.Select(MapToDto).ToList();
    }

    public async Task<FriendNoteDto> UpsertNoteAsync(Guid ownerUserId, Guid friendUserId, UpsertFriendNoteDto dto)
    {
        if (friendUserId == ownerUserId)
            throw new BadRequestException(ApiErrorMessages.Friend.CannotNoteSelf);

        var friend = await _userRepository.GetByIdAsync(friendUserId)
            ?? throw new NotFoundException(ApiErrorMessages.Friend.UserNotFound(friendUserId));

        var existing = await _noteRepository.GetByOwnerAndFriendAsync(ownerUserId, friendUserId);
        var now = DateTime.UtcNow;

        if (existing == null)
        {
            existing = new FriendNote
            {
                Id = Guid.NewGuid(),
                OwnerUserId = ownerUserId,
                FriendUserId = friendUserId,
                Alias = dto.Alias.Trim(),
                Note = dto.Note?.Trim(),
                Tags = dto.Tags?.Trim(),
                CreatedAt = now,
                UpdatedAt = now
            };
            await _noteRepository.AddAsync(existing);
        }
        else
        {
            existing.Alias = dto.Alias.Trim();
            existing.Note = dto.Note?.Trim();
            existing.Tags = dto.Tags?.Trim();
            existing.UpdatedAt = now;
            _noteRepository.Update(existing);
        }

        await _noteRepository.SaveChangesAsync();

        // Reload để có navigation Friend
        var reload = await _noteRepository.GetByIdAsync(existing.Id) ?? existing;
        return MapToDto(reload);
    }

    public async Task DeleteNoteAsync(Guid ownerUserId, Guid noteId)
    {
        var note = await _noteRepository.GetByIdAsync(noteId)
            ?? throw new NotFoundException(ApiErrorMessages.Friend.NoteNotFound(noteId));

        if (note.OwnerUserId != ownerUserId)
        {
            throw new ForbiddenException(ApiErrorMessages.Friend.NoteNotOwner(noteId));
        }

        _noteRepository.Remove(note);
        await _noteRepository.SaveChangesAsync();
    }

    private static FriendNoteDto MapToDto(FriendNote n)
    {
        return new FriendNoteDto
        {
            NoteId = n.Id,
            FriendUserId = n.FriendUserId,
            FriendUsername = n.Friend?.Username ?? string.Empty,
            FriendAvatarUrl = n.Friend?.Profile?.AvatarUrl,
            Alias = n.Alias,
            Note = n.Note,
            Tags = n.Tags,
            CreatedAt = n.CreatedAt,
            UpdatedAt = n.UpdatedAt
        };
    }
}
