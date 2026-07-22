using BoardVerse.Core.DTOs.Friend;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

public class FriendNoteServiceTests
{
    private readonly Mock<IFriendNoteRepository> _noteRepo = new();
    private readonly Mock<IUserManagementRepository> _userRepo = new();
    private FriendNoteService CreateService() => new(_noteRepo.Object, _userRepo.Object);

    private static User BuildUser(Guid id, string username = "alice")
    {
        return new User
        {
            Id = id,
            Username = username,
            Email = $"{username}@boardverse.test",
            Role = UserRole.Player,
            Profile = new UserProfile { UserId = id, AvatarUrl = "avatar.png" }
        };
    }

    [Fact]
    public async Task UpsertNoteAsync_WhenOwnerIsSelf_ThrowsBadRequest()
    {
        var meId = Guid.NewGuid();
        var svc = CreateService();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            svc.UpsertNoteAsync(meId, meId, new UpsertFriendNoteDto { Alias = "self" }));
    }

    [Fact]
    public async Task UpsertNoteAsync_WhenFriendNotFound_ThrowsNotFound()
    {
        var meId = Guid.NewGuid();
        var friendId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(friendId)).ReturnsAsync((User?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.UpsertNoteAsync(meId, friendId, new UpsertFriendNoteDto { Alias = "alice" }));
    }

    [Fact]
    public async Task UpsertNoteAsync_WhenNew_CreatesNote()
    {
        var meId = Guid.NewGuid();
        var friendId = Guid.NewGuid();
        var friend = BuildUser(friendId, "alice");
        _userRepo.Setup(r => r.GetByIdAsync(friendId)).ReturnsAsync(friend);
        _noteRepo.Setup(r => r.GetByOwnerAndFriendAsync(meId, friendId)).ReturnsAsync((FriendNote?)null);

        FriendNote? captured = null;
        _noteRepo.Setup(r => r.AddAsync(It.IsAny<FriendNote>()))
            .Callback<FriendNote>(n => captured = n)
            .Returns(Task.CompletedTask);
        _noteRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((FriendNote?)null);

        var svc = CreateService();

        var result = await svc.UpsertNoteAsync(meId, friendId, new UpsertFriendNoteDto
        {
            Alias = "  alice-the-catan  ",
            Note = " chơi tốt ",
            Tags = " Catan,Wingman "
        });

        Assert.NotNull(captured);
        Assert.Equal("alice-the-catan", captured!.Alias);
        Assert.Equal("chơi tốt", captured.Note);
        Assert.Equal("Catan,Wingman", captured.Tags);
        _noteRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        Assert.Equal(friendId, result.FriendUserId);
        Assert.Equal("alice-the-catan", result.Alias);
    }

    [Fact]
    public async Task UpsertNoteAsync_WhenExisting_UpdatesAndCallsUpdate()
    {
        var meId = Guid.NewGuid();
        var friendId = Guid.NewGuid();
        var friend = BuildUser(friendId, "alice");
        var existing = new FriendNote
        {
            Id = Guid.NewGuid(),
            OwnerUserId = meId,
            FriendUserId = friendId,
            Alias = "old-alias",
            Note = "old-note",
            Tags = "old",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        };
        _userRepo.Setup(r => r.GetByIdAsync(friendId)).ReturnsAsync(friend);
        _noteRepo.Setup(r => r.GetByOwnerAndFriendAsync(meId, friendId)).ReturnsAsync(existing);
        _noteRepo.Setup(r => r.GetByIdAsync(existing.Id)).ReturnsAsync(existing);

        var svc = CreateService();

        var result = await svc.UpsertNoteAsync(meId, friendId, new UpsertFriendNoteDto
        {
            Alias = "new-alias",
            Note = "new-note",
            Tags = "new"
        });

        Assert.Equal("new-alias", existing.Alias);
        Assert.Equal("new-note", existing.Note);
        Assert.Equal("new", existing.Tags);
        _noteRepo.Verify(r => r.Update(existing), Times.Once);
        _noteRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        Assert.Equal("new-alias", result.Alias);
    }

    [Fact]
    public async Task DeleteNoteAsync_WhenNoteNotFound_ThrowsNotFound()
    {
        var meId = Guid.NewGuid();
        _noteRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((FriendNote?)null);

        var svc = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => svc.DeleteNoteAsync(meId, Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteNoteAsync_WhenNotOwner_ThrowsForbidden()
    {
        var meId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var note = new FriendNote { Id = Guid.NewGuid(), OwnerUserId = otherId, FriendUserId = Guid.NewGuid(), Alias = "x" };
        _noteRepo.Setup(r => r.GetByIdAsync(note.Id)).ReturnsAsync(note);

        var svc = CreateService();

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.DeleteNoteAsync(meId, note.Id));
    }

    [Fact]
    public async Task DeleteNoteAsync_WhenOwner_RemovesAndSaves()
    {
        var meId = Guid.NewGuid();
        var note = new FriendNote { Id = Guid.NewGuid(), OwnerUserId = meId, FriendUserId = Guid.NewGuid(), Alias = "x" };
        _noteRepo.Setup(r => r.GetByIdAsync(note.Id)).ReturnsAsync(note);

        var svc = CreateService();

        await svc.DeleteNoteAsync(meId, note.Id);

        _noteRepo.Verify(r => r.Remove(note), Times.Once);
        _noteRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task GetMyNotesAsync_ReturnsEmpty_WhenNoNotes()
    {
        var meId = Guid.NewGuid();
        _noteRepo.Setup(r => r.GetByOwnerAsync(meId)).ReturnsAsync(new List<FriendNote>());

        var svc = CreateService();

        var result = await svc.GetMyNotesAsync(meId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMyNotesAsync_MapsEachNote()
    {
        var meId = Guid.NewGuid();
        var friendId = Guid.NewGuid();
        var notes = new List<FriendNote>
        {
            new()
            {
                Id = Guid.NewGuid(),
                OwnerUserId = meId,
                FriendUserId = friendId,
                Alias = "alice",
                Note = "chơi tốt",
                Tags = "Catan",
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                UpdatedAt = DateTime.UtcNow.AddDays(-1),
                Friend = BuildUser(friendId, "alice-real")
            }
        };
        _noteRepo.Setup(r => r.GetByOwnerAsync(meId)).ReturnsAsync(notes);

        var svc = CreateService();

        var result = await svc.GetMyNotesAsync(meId);

        Assert.Single(result);
        Assert.Equal("alice", result[0].Alias);
        Assert.Equal("alice-real", result[0].FriendUsername);
        Assert.Equal("chơi tốt", result[0].Note);
    }
}